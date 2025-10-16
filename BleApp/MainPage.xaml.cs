using BleLibrary.Abstractions;
using Plugin.BLE;
using Plugin.BLE.Abstractions;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;

namespace BleApp
{
    public partial class MainPage : ContentPage
    {
        private readonly IBleService _ble;
        private bool _subscribed;

        private bool _connecting;

        public ObservableCollection<DiscoveredDevice> Devices { get; } = new();
        private readonly Dictionary<Guid, DiscoveredDevice> _byId = new();

        public string DeviceCountText => Devices.Count == 0
            ? "Found 0 devices"
            : $"Found {Devices.Count} device(s)";


        // --- Live data buffer
        private readonly List<string> _liveLines = new();
        private const int _maxLines = 150;
        private string _latestRawText = string.Empty;

        public string LatestRawText
        {
            get => _latestRawText;
            private set
            {
                if (_latestRawText == value) return;
                _latestRawText = value;
                OnPropertyChanged(nameof(LatestRawText));
            }
        }

        private void AppendLive(string block)
        {
            _liveLines.Insert(0, block);
            if (_liveLines.Count > _maxLines)
                _liveLines.RemoveAt(_liveLines.Count - 1);

            LatestRawText = string.Join(Environment.NewLine, _liveLines);
        }

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
                _ble.DataReceived += OnDataReceived;
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
                _ble.DataReceived -= OnDataReceived;
                _subscribed = false;
            }
        }

        private async void OnScanClicked(object sender, EventArgs e)
        {
            try
            {
                Debug.WriteLine("==== Requesting permissions ====");
                await EnsureBluetoothPermissions();

                ClearDevices();
                DateTime currentDateTime1 = DateTime.Now;
                Debug.WriteLine($"==== BLE SCAN START ==== {currentDateTime1}");

                try 
                { 
                    await _ble.StartScanForDevicesAsync(); 
                } 
                catch (OperationCanceledException) 
                { 
                    Debug.WriteLine("Scan was cancelled by user");
                }
                DateTime currentDateTime2 = DateTime.Now;

                await _ble.StopScanForDevicesAsync();
                OnPropertyChanged(nameof(DeviceCountText));

                Debug.WriteLine($"==== BLE SCAN DONE. ==== {currentDateTime2}");
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
            MainThread.BeginInvokeOnMainThread(() =>
            {
                var id = e.Device.Id;
                if (_byId.TryGetValue(id, out var existing))
                {
                    if (existing.Rssi != e.Device.Rssi)
                    {
                        existing.Rssi = e.Device.Rssi;
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
                        Id = e.Device.Id,
                        Name = e.Device.Name,
                        Rssi = e.Device.Rssi,
                        NativeDevice = e.Device.NativeDevice,
                        State = DeviceState.Disconnected
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

                var id = new DeviceIdentifier(item.Id, item.Name, item.Rssi, item.NativeDevice, DeviceState.Disconnected, item.AdvertisementRecords);
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
                var name = string.IsNullOrWhiteSpace(e.Device.Name) ? $"Device {e.Device.Id}" : e.Device.Name;
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

        // --- NEW: render raw feed lines into the box ---
        private void OnDataReceived(object? sender, DeviceDataReceivedEventArgs e)
        {
            var id = e.Device;

            // Build a multiline text block for this packet
            var sb = new StringBuilder();
            sb.AppendLine($"Name: {id.Name}");
            sb.AppendLine($"Id: {id.Id}");
            sb.AppendLine($"RSSI: {id.Rssi}");
            sb.AppendLine($"State: {id.State}");
            sb.AppendLine($"Address: {id.NativeDevice}");
            sb.AppendLine($"Type: {id.Type}");
            sb.AppendLine($"Data: {e.Data}");

            MainThread.BeginInvokeOnMainThread(() => AppendLive(sb.ToString()));
        }


        private void ClearDevices()
        {
            Devices.Clear();
            _byId.Clear();
            OnPropertyChanged(nameof(DeviceCountText));
        }

        public sealed class DiscoveredDevice
        {
            public Guid Id { get; init; }
            public string? Name { get; init; }
            public int Rssi { get; set; }
            public object? NativeDevice { get; init; }
            public DeviceState State { get; set; }
            public IReadOnlyList<AdvertisementRecord> AdvertisementRecords { get; init; }

            public string Title => string.IsNullOrWhiteSpace(Name) ? "Unknown" : Name!;
            public string Subtitle => $"{Id}";
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
