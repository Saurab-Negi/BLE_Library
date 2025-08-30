using System.Diagnostics;
using BleLibrary.Abstractions;

namespace BleApp
{
    public partial class MainPage : ContentPage
    {
        private readonly IBleService _ble;
        private bool _subscribed;
        private CancellationTokenSource? _cts;

        public MainPage(IBleService ble)
        {
            InitializeComponent();
            _ble = ble;
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            if (!_subscribed)
            {
                _ble.DeviceFound += OnDeviceFound;
                _subscribed = true;
            }
        }
        protected override async void OnDisappearing()
        {
            base.OnDisappearing();
            try { await _ble.StopScanForDevicesAsync(); } catch { }
            if (_subscribed) { _ble.DeviceFound -= OnDeviceFound; _subscribed = false; }
        }

        private async void OnScanClicked(object sender, EventArgs e)
        {
            try
            {
                Debug.WriteLine("==== Requesting permissions ====");
                await EnsureBluetoothPermissions();

                _cts?.Cancel();
                _cts = new CancellationTokenSource();

                Debug.WriteLine("==== BLE SCAN START ====");

                // Option A: if your IBleService has a ScanAsync with a callback
                // Adjust to your actual signature
                //var devices = _ble.StartScanForDevicesAsync();
                try { await _ble.StartScanForDevicesAsync(_cts.Token); } catch (OperationCanceledException) { }

                // Option B: if it returns a list only
                // var devices = await _ble.ScanAsync(TimeSpan.FromSeconds(10), _cts.Token);
                // foreach (var d in devices)
                //    Debug.WriteLine($"FOUND: {d.Name ?? "(no name)"} | {d.Id} | RSSI={d.Rssi}");

                await Task.Delay(20000); // scan for 10 seconds

                await _ble.StopScanForDevicesAsync();

                //await devices;

                Debug.WriteLine($"==== BLE SCAN DONE. ====");
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine($"==== Scan canceled ====");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"====SCAN ERROR: {ex} ====");
            }
        }

        private void OnDeviceFound(object? sender, DeviceFoundEventArgs e)
        {
            var id = e.Device;         // DeviceIdentifier (Id, Name)
            Debug.WriteLine($"FOUND: {id.Name ?? "(no name)"} | {id.Id} | RSSI={e.Rssi}");
        }

        // Handle runtime permissions across Android versions & iOS
        private static async Task EnsureBluetoothPermissions()
        {
#if ANDROID
            // Android 12+ (API 31): BLUETOOTH_SCAN/CONNECT required
            var scan = await Permissions.CheckStatusAsync<Permissions.Bluetooth>();
            var loc = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();

            if (scan != PermissionStatus.Granted)
                scan = await Permissions.RequestAsync<Permissions.Bluetooth>();

            // Location can still be needed for legacy discovery / RSSI context
            if (loc != PermissionStatus.Granted)
                loc = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();

#elif IOS
        await Task.CompletedTask;
#else
        await Task.CompletedTask;
#endif
        }
    }
}
