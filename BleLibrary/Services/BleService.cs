using BleLibrary.Abstractions;
using BleLibrary.Exceptions;
using BleLibrary.Parsers;
using Microsoft.Extensions.Logging;
using Plugin.BLE;
using Plugin.BLE.Abstractions.Contracts;
using Plugin.BLE.Abstractions.EventArgs;
using System.Collections.Concurrent;

namespace BleLibrary.Services
{
    /// <summary>
    /// Wrapper over Plugin.BLE
    /// </summary>
    public sealed class BleService : IBleService, IDisposable
    {
        private readonly IBluetoothLE _ble;
        private readonly IAdapter _adapter;
        private readonly ILogger<BleService> _logger;

        private readonly List<IProfileParser> _parsers;
        private readonly ConcurrentDictionary<string, IDevice> _deviceCache = new();
        private readonly HashSet<string> _seenDevices = [];
        private IDevice? _connected;
        private ICharacteristic? _ftmsControlPoint;

        private volatile bool _isScanning;
        private const int MinRssi = -85;

        public event EventHandler<DeviceFoundEventArgs>? DeviceFound;
        public event EventHandler<DeviceConnectionEventArgs>? ConnectionStateChanged;
        public event EventHandler<DeviceDataReceivedEventArgs>? DataReceived;

        public BleService(ILogger<BleService> logger)
        {
            _ble = CrossBluetoothLE.Current;
            _adapter = _ble.Adapter;
            _logger = logger;

            // Subscribe to adapter events
            _adapter.DeviceDiscovered += OnDeviceDiscovered;
            _adapter.DeviceConnected += OnDeviceConnected;
            _adapter.DeviceDisconnected += OnDeviceDisconnected;

            // Register parsers
            _parsers = [
                new HeartRateParser(),
                new CyclingPowerParser(),
                new FtmsIndoorBikeDataParser()
            ];
        }

        public async Task StartScanForDevicesAsync(CancellationToken ct = default)
        {
            if (!_ble.IsAvailable || !_ble.IsOn)
            {
                RaiseConnectionEvent(null, ConnectionStatus.PermissionDenied, "Bluetooth unavailable or powered off.");
                return;
            }

            if (_isScanning)
            {
                return;
            }

            _seenDevices.Clear();
            _isScanning = true;

            try
            {
                // Filter by target services (lets Plugin.BLE do the heavy lifting)
                var targetServices = new[] { Uuids.Ftms, Uuids.Hrs, Uuids.Cps };
                _logger.LogInformation("Starting BLE scan filtered by FTMS/HRS/CPS");

                await _adapter.StartScanningForDevicesAsync(
                    serviceUuids: null, // set serviceUuids: null if you want to see anything nearby
                    deviceFilter: null,
                    cancellationToken: ct);

                // Note: DeviceDiscovered will fire during scan
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Scan failed");
                _isScanning = false;
                throw new BleServiceException("Failed to start scan", ex);
            }
        }

        public async Task StopScanForDevicesAsync()
        {
            if (!_isScanning)
            {
                return;
            }

            try
            {
                await _adapter.StopScanningForDevicesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Stop scan failed");
            }
            finally
            {
                _isScanning = false;
            }
        }

        public async Task<bool> ConnectToDeviceAsync(DeviceIdentifier deviceId, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(deviceId);

            if (!_deviceCache.TryGetValue(deviceId.Id, out var device))
            {
                _logger.LogWarning("Device not in cache. Scan first.");
                return false;
            }

            // Retry with backoff
            try
            {
                bool success = await WithRetries(async () =>
                {
                    await _adapter.ConnectToDeviceAsync(device, cancellationToken: ct);
                    return device.State == Plugin.BLE.Abstractions.DeviceState.Connected;
                }, attempts: 3);

                if (!success)
                {
                    _logger.LogWarning("Connection failed.");
                    return false;
                }

                _connected = device;
                RaiseConnectionEvent(deviceId, ConnectionStatus.Connected, "Connected");

                // Service discovery & subscriptions
                await DiscoverAndSubscribeAsync(device, ct);
                return true;
            }
            catch (BleServiceException ex)
            {
                _logger.LogError(ex, "Connection retries exhausted");
                RaiseConnectionEvent(deviceId, ConnectionStatus.ConnectionFailed, ex.Message, ex);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected connect error");
                RaiseConnectionEvent(deviceId, ConnectionStatus.ConnectionFailed, ex.Message, ex);
                return false;
            }
        }

        public async Task DisconnectDeviceAsync(DeviceIdentifier deviceId)
        {
            try
            {
                if (_connected != null)
                {
                    await _adapter.DisconnectDeviceAsync(_connected);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Disconnect failed");
            }
            finally
            {
                _connected = null;
                _ftmsControlPoint = null;
                RaiseConnectionEvent(deviceId, ConnectionStatus.Disconnected, "Disconnected");
            }
        }

        public async Task<bool> WriteFtmsControlCommandAsync(byte[] command, CancellationToken ct = default)
        {
            if (_ftmsControlPoint is null)
            {
                _logger.LogWarning("FTMS control point not available.");
                return false;
            }

            try
            {
                return await WithRetries(async () =>
                {
                    await _ftmsControlPoint.WriteAsync(command, cancellationToken: ct);
                    return true;
                }, attempts: 2);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FTMS control write failed");
                return false;
            }
        }

        // ----------------------- Internals -----------------------

        private void OnDeviceDiscovered(object? sender, DeviceEventArgs e)
        {
            try
            {
                // Extra RSSI filter
                if (e.Device is null || e.Device.Rssi < MinRssi)
                {
                    return;
                }

                var id = ToIdentifier(e.Device);
                if (!_seenDevices.Add(id.Id))
                {
                    return;
                }

                _deviceCache[id.Id] = e.Device;
                DeviceFound?.Invoke(this, new DeviceFoundEventArgs(id, e.Device.Rssi));
                _logger.LogInformation("Found device: {Name} ({Id}) RSSI {Rssi}", id.Name, id.Id, e.Device.Rssi);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in DeviceDiscovered");
            }
        }

        private void OnDeviceConnected(object? sender, DeviceEventArgs e)
        {
            if (e.Device is null)
            {
                return;
            }
            var id = ToIdentifier(e.Device);
            _logger.LogInformation("Device connected: {Name} ({Id})", id.Name, id.Id);
            RaiseConnectionEvent(id, ConnectionStatus.Connected, "Connected");
        }

        private void OnDeviceDisconnected(object? sender, DeviceEventArgs e)
        {
            if (e.Device is null)
            {
                return;
            }
            var id = ToIdentifier(e.Device);
            _connected = null;
            _ftmsControlPoint = null;
            _logger.LogInformation("Device disconnected: {Name} ({Id})", id.Name, id.Id);
            RaiseConnectionEvent(id, ConnectionStatus.Disconnected, "Disconnected");
        }

        private async Task DiscoverAndSubscribeAsync(IDevice device, CancellationToken ct)
        {
            var services = await device.GetServicesAsync(ct);
            foreach (var svc in services)
            {
                if (svc.Id != Uuids.Ftms && svc.Id != Uuids.Hrs && svc.Id != Uuids.Cps)
                {
                    continue;
                }

                var chars = await svc.GetCharacteristicsAsync();

                // FTMS: Indoor Bike Data
                var ftmsData = chars.FirstOrDefault(c => c.Id == Uuids.Ftms_IndoorBikeData);
                if (ftmsData != null)
                {
                    await SubscribeCharacteristicAsync(device, svc.Id, ftmsData, ct);
                }

                // FTMS: Control Point
                var ctrl = chars.FirstOrDefault(c => c.Id == Uuids.Ftms_FitnessMachineCtrlPoint);
                if (ctrl != null)
                {
                    _ftmsControlPoint = ctrl;
                }

                // HRS: Heart Rate Measurement
                var hr = chars.FirstOrDefault(c => c.Id == Uuids.Hrs_HeartRateMeasurement);
                if (hr != null)
                {
                    await SubscribeCharacteristicAsync(device, svc.Id, hr, ct);
                }

                // CPS: Cycling Power Measurement
                var cp = chars.FirstOrDefault(c => c.Id == Uuids.Cps_CyclingPowerMeasurement);
                if (cp != null)
                {
                    await SubscribeCharacteristicAsync(device, svc.Id, cp, ct);
                }
            }
        }

        private async Task SubscribeCharacteristicAsync(IDevice device, Guid serviceId, ICharacteristic ch, CancellationToken ct)
        {
            ch.ValueUpdated += (s, args) =>
            {
                try
                {
                    var payload = args.Characteristic.Value.AsSpan();
                    foreach (var parser in _parsers)
                    {
                        if (!parser.CanParse(serviceId, ch.Id))
                        {
                            continue;
                        }
                        if (parser.TryParse(payload, out var parsed) && parsed is not null)
                        {
                            var id = ToIdentifier(device);
                            DataReceived?.Invoke(this, new DeviceDataReceivedEventArgs(id, parsed));
                            break; // first match wins
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Parse error for characteristic {Char}", ch.Id);
                }
            };

            await ch.StartUpdatesAsync(ct);
            _logger.LogInformation("Subscribed to {Char} on {Svc}", ch.Id, serviceId);
        }

        private static async Task<T> WithRetries<T>(Func<Task<T>> action, int attempts = 3)
        {
            Exception? last = null;
            for (int i = 0; i < attempts; i++)
            {
                try { return await action(); }
                catch (Exception ex)
                {
                    last = ex;
                    await Task.Delay(TimeSpan.FromMilliseconds((250 * i * i) + 150));
                }
            }
            throw new BleServiceException("Operation failed after retries", last);
        }

        private void RaiseConnectionEvent(DeviceIdentifier? id, ConnectionStatus status, string? msg, Exception? ex = null)
        {
            var device = id ?? (_connected is null ? new DeviceIdentifier("unknown") : ToIdentifier(_connected));
            ConnectionStateChanged?.Invoke(this, new DeviceConnectionEventArgs(device, status, msg, ex));
        }

        private static DeviceIdentifier ToIdentifier(IDevice d)
        {
            return new(d.Id.ToString(), d.Name);
        }

        public void Dispose()
        {
            _adapter.DeviceDiscovered -= OnDeviceDiscovered;
            _adapter.DeviceConnected -= OnDeviceConnected;
            _adapter.DeviceDisconnected -= OnDeviceDisconnected;
        }
    }
}
