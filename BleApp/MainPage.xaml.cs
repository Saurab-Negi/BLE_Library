using BleLibrary.Abstractions;
using System.Diagnostics;

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

        private async void OnScanClicked(object sender, EventArgs e)
        {
            try
            {
                Debug.WriteLine("==== Requesting permissions ====");
                await EnsureBluetoothPermissions();

                _cts?.Cancel();
                _cts = new CancellationTokenSource();

                Debug.WriteLine("==== BLE SCAN START ====");

                try { await _ble.StartScanForDevicesAsync(_cts.Token); } catch (OperationCanceledException) { }


                await Task.Delay(10000); // scan for 10 seconds

                await _ble.StopScanForDevicesAsync();

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

        private async void OnConnectClicked(object sender, EventArgs e)
        {
            await _ble.StopScanForDevicesAsync();

            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            //var target = new DeviceIdentifier();

            Debug.WriteLine("==== BLE CONNECT START ====");

            var target = new DeviceIdentifier
            (
                Id: "00000000-0000-0000-0000-45412ad23195",  // or Guid/Uuid
                Name: "LCONNECT ACE",
                Address: null
            );

            var ok = await _ble.ConnectToDeviceAsync(target, _cts.Token);

            Debug.WriteLine($"==== BLE CONNECT DONE. ====");
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
