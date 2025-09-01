using BleLibrary.Abstractions;
using System.Diagnostics;
using Plugin.BLE;
using System.Collections.ObjectModel;

namespace BleApp
{
    public partial class MainPage : ContentPage
    {
        private readonly IBleService _ble;
        private bool _subscribed;
        private CancellationTokenSource? _cts;

        private bool _connecting;

        public ObservableCollection<DiscoveredDevice> Devices { get; } = new();
        private readonly Dictionary<string, DiscoveredDevice> _byId = new();

        public string DeviceCountText => Devices.Count == 0
            ? "Found 0 devices"
            : $"Found {Devices.Count} device(s)";

        // Static GUID from your scan log:
        private static readonly Guid TestGuid = Guid.Parse("00000000-0000-0000-0000-f44b1c881a39");


        public MainPage(IBleService ble)
        {
            InitializeComponent();
            _ble = ble;
            BindingContext = this;
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            if (!_subscribed)
            {
                _ble.DeviceFound += OnDeviceFound;
                _ble.ConnectionStateChanged += OnConnectionStateChanged; // optional UI feedback
                _subscribed = true;
            }
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            if (_subscribed)
            {
                _ble.DeviceFound -= OnDeviceFound;
                _ble.ConnectionStateChanged -= OnConnectionStateChanged;
                _subscribed = false;
            }
        }

        private async void OnScanClicked(object sender, EventArgs e)
        {
            try
            {
                Debug.WriteLine("==== Requesting permissions ====");
                await EnsureBluetoothPermissions();

                _cts?.Cancel();
                _cts = new CancellationTokenSource();
                ClearDevices();

                Debug.WriteLine("==== BLE SCAN START ====");

                try { await _ble.StartScanForDevicesAsync(_cts.Token); } catch (OperationCanceledException) { }
                await Task.Delay(10_000, _cts.Token);
                await _ble.StopScanForDevicesAsync();
                OnPropertyChanged(nameof(DeviceCountText));

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


            Debug.WriteLine("==== BLE CONNECT START ====");

            var adapter = CrossBluetoothLE.Current.Adapter;

            // Always pass a CancellationToken
            var device = await adapter.ConnectToKnownDeviceAsync(TestGuid, cancellationToken: _cts.Token);

            Debug.WriteLine($"==== Connected to {device?.Name} ({device?.Id}) ====");

            Debug.WriteLine($"==== BLE CONNECT DONE. ====");
        }

        private void OnDeviceFound(object? sender, DeviceFoundEventArgs e)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                var id = e.Device.Id ?? string.Empty;
                if (_byId.TryGetValue(id, out var existing))
                {
                    if (existing.Rssi != e.Rssi)
                    {
                        existing.Rssi = e.Rssi;
                        var idx = Devices.IndexOf(existing);
                        if (idx >= 0)
                        {
                            Devices.RemoveAt(idx);
                            Devices.Insert(idx, existing);
                        }
                    }
                }
                else
                {
                    var item = new DiscoveredDevice
                    {
                        Id = id,
                        Name = e.Device.Name,
                        Address = e.Device.Address,
                        Rssi = e.Rssi
                    };
                    _byId[id] = item;
                    Devices.Add(item);
                    OnPropertyChanged(nameof(DeviceCountText));
                }
            });
        }

        private async void OnDeviceSelected(object? sender, SelectionChangedEventArgs e)
        {
            // de-select immediately so user can tap again later
            if (sender is CollectionView cv) cv.SelectedItem = null;

            if (_connecting) return;

            var item = e.CurrentSelection?.FirstOrDefault() as DiscoveredDevice;
            if (item is null) return;

            _connecting = true;
            try
            {
                await _ble.StopScanForDevicesAsync();

                var id = new DeviceIdentifier(item.Id, item.Name, item.Address);
                var ok = await _ble.ConnectToDeviceAsync(id);
                if (!ok)
                {
                    await DisplayAlert("Connection", $"Failed to connect to {item.Title}", "OK");
                }
                // success feedback will also arrive via ConnectionStateChanged handler
            }
            catch (Exception ex)
            {
                await DisplayAlert("Connection Error", ex.Message, "OK");
            }
            finally
            {
                _connecting = false;
            }
        }

        private void OnConnectionStateChanged(object? sender, DeviceConnectionEventArgs e)
        {
            // Optional: toast/status. Keep it minimal for demo.
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                var name = string.IsNullOrWhiteSpace(e.Device.Name) ? e.Device.Id : e.Device.Name;
                switch (e.Status)
                {
                    case ConnectionStatus.Connected:
                        await DisplayAlert("Connected", $"Connected to {name}", "OK");
                        break;
                    case ConnectionStatus.Disconnected:
                        // optional: show alert on user-initiated disconnects
                        break;
                    case ConnectionStatus.ConnectionFailed:
                        await DisplayAlert("Connection Failed", e.Message ?? $"Could not connect to {name}", "OK");
                        break;
                }
            });
        }

        private void ClearDevices()
        {
            Devices.Clear();
            _byId.Clear();
            OnPropertyChanged(nameof(DeviceCountText));
        }

        public sealed class DiscoveredDevice
        {
            public string Id { get; init; } = "";
            public string? Name { get; init; }
            public string? Address { get; init; }
            public int Rssi { get; set; }

            public DeviceIdentifier DeviceIdentifier => new(Id, Name, Address);
            public string Title => string.IsNullOrWhiteSpace(Name) ? "Unknown" : Name!;
            public string Subtitle => string.IsNullOrWhiteSpace(Address) ? Id : $"{Id} • {Address}";
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
