using BleLibrary.Abstractions;
using BleLibrary.Domain;
using BleLibrary.Exceptions;
using BleLibrary.Parsers;
using Microsoft.Extensions.Logging;
using Plugin.BLE;
using Plugin.BLE.Abstractions;
using Plugin.BLE.Abstractions.Contracts;
using Plugin.BLE.Abstractions.EventArgs;
using Plugin.BLE.Abstractions.Exceptions;
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

        private readonly CscCalculator _cscCalc = new CscCalculator();

        private volatile bool _isScanning;
        private const int MinRssi = -85;

        private readonly ConcurrentDictionary<Guid, DateTimeOffset> _lastSeen = new();
        private CancellationTokenSource? _reconnectCts;
        private const int OutOfRangeGraceMs = 10_000; // last seen > 10s → likely out of range
        private const int ConnectTimeoutMs = 7_000; // iOS needs our own timeout
        private const int ScanWindowMs = 3_000; // brief scan between attempts to refresh cache
        private volatile bool _autoReconnectEnabled = true;

        private readonly ConcurrentQueue<IDevice> _verificationQueue = new();
        private Task? _verificationWorker;
        private readonly SemaphoreSlim _verificationLock = new(1, 1);
        private volatile bool _verificationRunning;

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
            _adapter.DeviceConnectionLost += OnDeviceConnectionLost;

            // Register parsers
            _parsers = [
                new HeartRateParser(),
                new CyclingPowerParser(),
                new FtmsIndoorBikeDataParser(),
                new TreadmillDataParser(),
                new RowerDataParser(),
                new CadenceSensorParser()
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
                    await ConnectKnownWithTimeoutAsync(device.Id, ct);

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
            catch (DeviceConnectionException ex)
            {
                // Android usually throws quickly if OOR; iOS might reach here if our token timed out
                var last = _lastSeen.TryGetValue(deviceId.Id, out var ts) ? ts : DateTimeOffset.MinValue;
                var isOutOfRange = (DateTimeOffset.UtcNow - last).TotalMilliseconds > OutOfRangeGraceMs
                                   || GattOutOfRange(ex);

                RaiseConnectionEvent(deviceId, isOutOfRange ? ConnectionStatus.OutOfRange : ConnectionStatus.ConnectionFailed,
                    ex.Message, ex);
                _logger.LogWarning(ex, "ConnectToDeviceAsync failed ({Status})", isOutOfRange ? "OutOfRange" : "ConnectionFailed");

                if (isOutOfRange && _autoReconnectEnabled)
                    _ = StartAutoReconnect(deviceId, default);

                return false;
            }
            catch (BleServiceException ex)
            {
                _logger.LogError(ex, "Connection retries exhausted");
                RaiseConnectionEvent(deviceId, ConnectionStatus.ConnectionFailed, ex.Message, ex);
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
                _autoReconnectEnabled = false; // optional: prevent loop restarting
                _reconnectCts?.Cancel();

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

                //if (e.Device.AdvertisementRecords?.Any(r => r.Type == AdvertisementRecordType.UuidsComplete16Bit) != true)
                //{
                //    return;
                //}

                var id = ToIdentifier(e.Device);
                if (!_seenDevices.Add(id.Id))
                {
                    return;
                }

                _lastSeen[e.Device.Id] = DateTimeOffset.UtcNow;
                _deviceCache[id.Id] = e.Device;

                // enqueue for validation
                _verificationQueue.Enqueue(e.Device);
                StartDeviceVerification();

                //DeviceFound?.Invoke(this, new DeviceFoundEventArgs(id));
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
                //RaiseConnectionEvent(id, ConnectionStatus.Connected, "Connected");
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
                //RaiseConnectionEvent(id, ConnectionStatus.Disconnected, "Disconnected");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in OnDeviceDisconnected");
            }
        }

        private void OnDeviceConnectionLost(object? sender, DeviceErrorEventArgs e)
        {
            try
            {
                if (e.Device is null) return;
                var id = ToIdentifier(e.Device);

                _connected = null;
                _ftmsControlPoint = null;

                _logger.LogWarning("Device connection lost: Name: {Name}, Id: {Id}, RSSI: {Rssi}, State: {State}," +
                    " Address: {Address}, Advertisement {Advertisement}", id.Name, id.Id, id.Rssi, id.State, id.NativeDevice,
                    id.AdvertisementRecords);

                // Declare why we think it was lost
                var last = _lastSeen.TryGetValue(id.Id, out var ts) ? ts : DateTimeOffset.MinValue;
                var isOutOfRange = (DateTimeOffset.UtcNow - last).TotalMilliseconds > OutOfRangeGraceMs;

                RaiseConnectionEvent(id, isOutOfRange ? ConnectionStatus.OutOfRange : ConnectionStatus.ConnectionLost,
                    isOutOfRange ? "Device likely out of range." : "Connection lost.");

                if (_autoReconnectEnabled)
                {
                    _ = StartAutoReconnect(id, default); // fire-and-forget
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in OnDeviceConnectionLost");
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

                // FTMS: Treadmill Data
                var tm = characteristics.FirstOrDefault(c => c.Id == Uuids.Ftms_TreadmillData);
                if (tm != null)
                {
                    await SubscribeCharacteristicAsync(device, svc.Id, tm, ct);
                }

                // FTMS: Rower Data
                var rd = characteristics.FirstOrDefault(c => c.Id == Uuids.Ftms_RowerData);
                if (rd != null)
                {
                    await SubscribeCharacteristicAsync(device, svc.Id, rd, ct);
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

                // CSC: Cycling Power Measurement
                var cs = characteristics.FirstOrDefault(c => c.Id == Uuids.Csc_CadenceMeasurement);
                if (cs != null)
                {
                    await SubscribeCharacteristicAsync(device, svc.Id, cs, ct);
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

                            if (parsed is CadenceSensorData cscData)
                            {
                                var metrics = _cscCalc.Update(id.Id, cscData);
                                _logger.LogInformation("DataReceived: Name: {Name}, Id: {Id}, RSSI: {Rssi}, State: {State}," +
                                    " Address: {Address}, Advertisement {Advertisement}, Service: {Service}, Char: {Char}," +
                                    " Parsed: {Parsed}", id.Name, id.Id, id.Rssi, id.State, id.NativeDevice, id.AdvertisementRecords,
                                    serviceId, ch.Id, metrics);
                                DataReceived?.Invoke(this, new DeviceDataReceivedEventArgs(id, metrics));
                            }
                            else
                            {
                                _logger.LogInformation("DataReceived: Name: {Name}, Id: {Id}, RSSI: {Rssi}, State: {State}," +
                                    " Address: {Address}, Advertisement {Advertisement}, Service: {Service}, Char: {Char}," +
                                    " Parsed: {Parsed}", id.Name, id.Id, id.Rssi, id.State, id.NativeDevice, id.AdvertisementRecords,
                                    serviceId, ch.Id, parsed);
                                DataReceived?.Invoke(this, new DeviceDataReceivedEventArgs(id, parsed));
                            }
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

        private async Task StartAutoReconnect(DeviceIdentifier deviceId, CancellationToken ct)
        {
            // Cancel any existing loop
            try { _reconnectCts?.Cancel(); } catch { /* ignore */ }
            _reconnectCts = new CancellationTokenSource();

            var token = _reconnectCts.Token;
            var attempts = 0;
            var maxBackoffMs = 20_000;

            _logger.LogInformation("Auto-reconnect loop started for {Name} ({Id})", deviceId.Name, deviceId.Id);

            while (!token.IsCancellationRequested)
            {
                try
                {
                    // 1) Fast path: Connect to known device GUID
                    await ConnectKnownWithTimeoutAsync(deviceId.Id, token);

                    // 2) On success, wire up and exit loop
                    if (_deviceCache.TryGetValue(deviceId.Id, out var dev) &&
                        dev.State == Plugin.BLE.Abstractions.DeviceState.Connected)
                    {
                        _connected = dev;
                        RaiseConnectionEvent(deviceId, ConnectionStatus.Connected, "Reconnected");
                        await DiscoverAndSubscribeAsync(dev, token);
                        _logger.LogInformation("Auto-reconnect succeeded for {Id}", deviceId.Id);
                        return;
                    }
                }
                catch (OperationCanceledException)
                {
                    // Either we cancelled, or timeout hit
                    if (token.IsCancellationRequested) break;

                    // Timeout likely means iOS out-of-range
                    RaiseConnectionEvent(deviceId, ConnectionStatus.OutOfRange, "Reconnect timed out; likely out of range.");
                }
                catch (DeviceConnectionException ex)
                {
                    // Android OOR/GATT
                    var status = GattOutOfRange(ex) ? ConnectionStatus.OutOfRange : ConnectionStatus.ConnectionFailed;
                    RaiseConnectionEvent(deviceId, status, ex.Message, ex);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Reconnect attempt failed");
                    RaiseConnectionEvent(deviceId, ConnectionStatus.ConnectionFailed, ex.Message, ex);
                }

                // 3) Brief scan to refresh presence and update lastSeen
                try
                {
                    await BriefScanAsync(TimeSpan.FromMilliseconds(ScanWindowMs), token);
                }
                catch { /* non-fatal */ }

                // 4) Backoff
                attempts++;
                var delayMs = Math.Min(maxBackoffMs, 500 * (int)Math.Pow(1.8, attempts));
                await Task.Delay(delayMs, token);
            }

            _logger.LogInformation("Auto-reconnect loop ended for {Id}", deviceId.Id);
        }

        private async Task BriefScanAsync(TimeSpan window, CancellationToken ct)
        {
            if (_isScanning) return;

            try
            {
                _isScanning = true;
                using var scanCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                scanCts.CancelAfter(window);

                await _adapter.StartScanningForDevicesAsync(
                    serviceUuids: null,
                    cancellationToken: scanCts.Token);
            }
            catch (OperationCanceledException)
            {
                // expected when window ends
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Brief scan failed");
            }
            finally
            {
                try { await _adapter.StopScanningForDevicesAsync(); } catch { }
                _isScanning = false;
            }
        }

        private static bool GattOutOfRange(Exception ex)
        {
            var msg = ex.Message?.ToLowerInvariant() ?? "";
            // Heuristic: typical Android failures when device is gone
            return msg.Contains("gatt") || msg.Contains("133") || msg.Contains("status 8") ||
                   msg.Contains("status 19") || msg.Contains("device not found") || msg.Contains("failed to connect");
        }

        private async Task ConnectKnownWithTimeoutAsync(Guid deviceId, CancellationToken outer)
        {
            // iOS: ConnectToKnownDeviceAsync never times out by itself — we impose one.
            // Android: if OOR, this will throw quickly; our timeout is just a ceiling.
            using var timeout = new CancellationTokenSource(ConnectTimeoutMs);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(outer, timeout.Token);

            await _adapter.ConnectToKnownDeviceAsync(
                deviceId,
                new ConnectParameters(autoConnect: false, forceBleTransport: true),
                linked.Token);
        }

        private void StartDeviceVerification()
        {
            if (_verificationRunning) return;

            _verificationRunning = true;
            _verificationWorker = Task.Run(async () =>
            {
                while (_verificationQueue.TryDequeue(out var device))
                {
                    await VerifyDeviceAsync(device);
                }
                _verificationRunning = false;
            });
        }

        private async Task VerifyDeviceAsync(IDevice device)
        {
            using var cts = new CancellationTokenSource(3000); // 3s timeout
            try
            {
                await _adapter.ConnectToDeviceAsync(
                    device,
                    new ConnectParameters(autoConnect: false, forceBleTransport: true),
                    cts.Token);

                var services = await device.GetServicesAsync(cts.Token);
                bool isRelevant = services.Any(s =>
                    s.Id == Uuids.Ftms || s.Id == Uuids.Hrs || s.Id == Uuids.Cps || s.Id == Uuids.Csc);

                if (isRelevant)
                {
                    var id = ToIdentifier(device);
                    _logger.LogInformation("Device Name: {Name} supports FTMS/HRS/CPS/CSC", id.Name);
                    DeviceFound?.Invoke(this, new DeviceFoundEventArgs(id));
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Verification failed for {Name}", device.Name);
            }
            finally
            {
                try { await _adapter.DisconnectDeviceAsync(device); } catch { }
            }
        }

        public void Dispose()
        {
            _adapter.DeviceDiscovered -= OnDeviceDiscovered;
            _adapter.DeviceConnected -= OnDeviceConnected;
            _adapter.DeviceDisconnected -= OnDeviceDisconnected;
            _adapter.DeviceConnectionLost -= OnDeviceConnectionLost;
        }
    }
}
