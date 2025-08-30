namespace BleLibrary.Abstractions
{
    public interface IBleService
    {
        event EventHandler<DeviceFoundEventArgs> DeviceFound;
        event EventHandler<DeviceConnectionEventArgs> ConnectionStateChanged;
        event EventHandler<DeviceDataReceivedEventArgs> DataReceived;

        Task StartScanForDevicesAsync(CancellationToken ct = default);
        Task StopScanForDevicesAsync();

        Task<bool> ConnectToDeviceAsync(DeviceIdentifier deviceId, CancellationToken ct = default);
        Task DisconnectDeviceAsync(DeviceIdentifier deviceId);

        /// <summary>FTMS Control Point (0x2AD9) write: foundation for ERG mode.</summary>
        Task<bool> WriteFtmsControlCommandAsync(byte[] command, CancellationToken ct = default);
    }
}
