namespace BleLibrary.Abstractions
{
    public interface IBleService
    {
        /// <summary>
        /// Raised whenever a new device is discovered during scanning.
        /// </summary>
        event EventHandler<DeviceFoundEventArgs> DeviceFound;

        /// <summary>
        /// Raised whenever a device's connection state changes.
        /// </summary>
        event EventHandler<DeviceConnectionEventArgs> ConnectionStateChanged;
        
        /// <summary>
        /// Raised whenever a device sends strongly-typed data.
        /// </summary>
        event EventHandler<DeviceDataReceivedEventArgs> DataReceived;

        Task StartScanForDevicesAsync(CancellationToken ct = default);
        Task StopScanForDevicesAsync();

        Task<bool> ConnectToDeviceAsync(DeviceIdentifier deviceId, CancellationToken ct = default);
        Task DisconnectDeviceAsync(DeviceIdentifier deviceId);

        /// <summary>FTMS Control Point (0x2AD9) write: foundation for ERG mode.</summary>
        Task<bool> WriteFtmsControlCommandAsync(byte[] command, CancellationToken ct = default);
    }
}
