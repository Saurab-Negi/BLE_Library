using BleLibrary.Abstractions;
using BleLibrary.Parsers;
using Microsoft.Extensions.Logging;
using Plugin.BLE;
using Plugin.BLE.Abstractions.Contracts;
using Plugin.BLE.Abstractions.EventArgs;

namespace BleLibrary.Services
{
    public sealed class BleWindowService : IBleService
    {
        private readonly IBluetoothLE _ble;
        private readonly IAdapter _adapter;
        private readonly ILogger<BleWindowService> _logger;

        private readonly List<IProfileParser> _parsers;
        private IDevice? _connected;
        private const int MinRssi = -85;

        public event EventHandler<DeviceFoundEventArgs>? DeviceFound;
        public event EventHandler<DeviceConnectionEventArgs>? ConnectionStateChanged;
        public event EventHandler<DeviceDataReceivedEventArgs>? DataReceived;
        public BleWindowService(ILogger<BleWindowService> logger)
        {
            _ble = CrossBluetoothLE.Current;
            _adapter = _ble.Adapter;
            _logger = logger;

            // Subscribe to adapter events
            _adapter.DeviceDiscovered += OnDeviceDiscovered;
            _adapter.DeviceConnected += OnDeviceConnected;
            _adapter.DeviceDisconnected += OnDeviceDisconnected;
            _adapter.DeviceConnectionLost += OnDeviceConnectionLost;

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

        }

        public async Task StopScanForDevicesAsync()
        {

        }

        public async Task<bool> ConnectToDeviceAsync(DeviceIdentifier deviceId, CancellationToken ct = default)
        {

        }

        public async Task DisconnectDeviceAsync(DeviceIdentifier deviceId)
        {

        }

        private async Task DiscoverAndSubscribeAsync(IDevice device, CancellationToken ct = default)
        {

        }

        private async Task SubscribeCharacteristicAsync(IDevice device, Guid serviceId, ICharacteristic ch, CancellationToken ct)
        {

        }

        private void OnDeviceDiscovered(object? sender, DeviceEventArgs e)
        {

        }

        private void OnDeviceConnected(object? sender, DeviceEventArgs e)
        {

        }

        private void OnDeviceDisconnected(object? sender, DeviceEventArgs e)
        {

        }

        private void OnDeviceConnectionLost(object? sender, DeviceEventArgs e)
        {

        }
    }
}
