using BleLibrary.Abstractions;
using BleLibrary.Exceptions;
using BleLibrary.Parsers;
using Microsoft.Extensions.Logging;
using Plugin.BLE;
using Plugin.BLE.Abstractions;
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
        private readonly ConcurrentDictionary<Guid, IDevice> _deviceCache = new();
        private readonly HashSet<Guid> _seenDevices = [];
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
                _logger.LogInformation("Bluetooth unavailable or powered off.");
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
                // Filter by target services
                var targetServices = new[] { Uuids.Ftms, Uuids.Hrs, Uuids.Cps };
                _logger.LogInformation("Starting BLE scan filtered by FTMS/HRS/CPS");

                await _adapter.StartScanningForDevicesAsync(
                    serviceUuids: null, // set serviceUuids: null if you want to see anything nearby
                    deviceFilter: null,
                    cancellationToken: ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in StartScanForDevicesAsync");
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
                _logger.LogError(ex, "Error in StopScanForDevicesAsync");
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
                _logger.LogInformation("Connection Initiated");
                bool success = await WithRetries(async () =>
                {
                    await _adapter.ConnectToDeviceAsync(
                        device,
                        new ConnectParameters(autoConnect: false, forceBleTransport: true),
                        ct);

                    return device.State == Plugin.BLE.Abstractions.DeviceState.Connected;
                }, attempts: 3);

                if (!success)
                {
                    _logger.LogWarning("Connection failed.");
                    return false;
                }

                _connected = device;

                // Service discovery & subscriptions
                await DiscoverAndSubscribeAsync(device, ct);
                return true;
            }
            catch (BleServiceException ex)
            {
                _logger.LogError(ex, "Connection retries exhausted");
                return false;
            }
            catch (Exception ex)
            {
                 _logger.LogError(ex, "Error in ConnectToDeviceAsync");
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
                _logger.LogError(ex, "Error in DisconnectDeviceAsync");
            }
            finally
            {
                _connected = null;
                _ftmsControlPoint = null;
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
                DeviceFound?.Invoke(this, new DeviceFoundEventArgs(id));
                _logger.LogInformation("Device found: Name: {Name}, Id: {Id}, RSSI: {Rssi}, State: {State}, Address: {Address}," +
                    " Advertisement {Advertisement}", id.Name, id.Id, id.Rssi, id.State, id.NativeDevice, id.AdvertisementRecords);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in OnDeviceDiscovered");
            }
        }

        private void OnDeviceConnected(object? sender, DeviceEventArgs e)
        {
            try
            {
                if (e.Device is null)
                {
                    return;
                }
                var id = ToIdentifier(e.Device);
                _logger.LogInformation("Device connected: Name: {Name}, Id: {Id}, RSSI: {Rssi}, State: {State}, Address: {Address}," +
                    " Advertisement {Advertisement}", id.Name, id.Id, id.Rssi, id.State, id.NativeDevice, id.AdvertisementRecords);
                RaiseConnectionEvent(id, ConnectionStatus.Connected, "Connected");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in OnDeviceConnected");
            }
        }

        private void OnDeviceDisconnected(object? sender, DeviceEventArgs e)
        {
            try
            {
                if (e.Device is null)
                {
                    return;
                }
                var id = ToIdentifier(e.Device);
                _connected = null;
                _ftmsControlPoint = null;
                _logger.LogInformation("Device disconnected: Name: {Name}, Id: {Id}, RSSI: {Rssi}, State: {State}," +
                    " Address: {Address}, Advertisement {Advertisement}", id.Name, id.Id, id.Rssi, id.State, id.NativeDevice,
                    id.AdvertisementRecords);
                RaiseConnectionEvent(id, ConnectionStatus.Disconnected, "Disconnected");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in OnDeviceDisconnected");
            }
        }

        private async Task DiscoverAndSubscribeAsync(IDevice device, CancellationToken ct)
        {
            var services = await device.GetServicesAsync(ct);
            foreach (var svc in services)
            {
                //if (svc.Id != Uuids.Ftms && svc.Id != Uuids.Hrs && svc.Id != Uuids.Cps)
                //{
                //    continue;
                //}

                _logger.LogInformation($"Service UUID: {svc.Id}");

                var characteristics = await svc.GetCharacteristicsAsync();

                foreach (var chars in characteristics)
                {
                    _logger.LogInformation($"Characteristic UUID: {chars.Id}, Notifiable: {chars.CanUpdate}");
                }

                // FTMS: Indoor Bike Data
                var ftmsData = characteristics.FirstOrDefault(c => c.Id == Uuids.Ftms_IndoorBikeData);
                if (ftmsData != null)
                {
                    await SubscribeCharacteristicAsync(device, svc.Id, ftmsData, ct);
                }

                // FTMS: Control Point
                var ctrl = characteristics.FirstOrDefault(c => c.Id == Uuids.Ftms_FitnessMachineCtrlPoint);
                if (ctrl != null)
                {
                    _ftmsControlPoint = ctrl;
                }

                // HRS: Heart Rate Measurement
                var hr = characteristics.FirstOrDefault(c => c.Id == Uuids.Hrs_HeartRateMeasurement);
                if (hr != null)
                {
                    await SubscribeCharacteristicAsync(device, svc.Id, hr, ct);
                }

                // CPS: Cycling Power Measurement
                var cp = characteristics.FirstOrDefault(c => c.Id == Uuids.Cps_CyclingPowerMeasurement);
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
                            _logger.LogInformation("DataReceived: Name: {Name}, Id: {Id}, RSSI: {Rssi}, State: {State}," +
                                " Address: {Address}, Advertisement {Advertisement}, Service: {Service}, Char: {Char}," +
                                " Parsed: {Parsed}", id.Name, id.Id, id.Rssi, id.State, id.NativeDevice, id.AdvertisementRecords,
                                serviceId, ch.Id, parsed);
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
            var device = id ?? (_connected is null ? new DeviceIdentifier(Guid.Empty, "unknown", 0, null!, DeviceState.Disconnected, null) : ToIdentifier(_connected));
            ConnectionStateChanged?.Invoke(this, new DeviceConnectionEventArgs(device, status, msg, ex));
        }

        private static DeviceIdentifier ToIdentifier(IDevice d)
        {
            return new(d.Id, d.Name ?? "unknown", d.Rssi, d.NativeDevice, d.State, d.AdvertisementRecords);
        }

        public void Dispose()
        {
            _adapter.DeviceDiscovered -= OnDeviceDiscovered;
            _adapter.DeviceConnected -= OnDeviceConnected;
            _adapter.DeviceDisconnected -= OnDeviceDisconnected;
        }
    }
}
