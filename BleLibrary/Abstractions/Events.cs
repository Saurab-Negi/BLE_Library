namespace BleLibrary.Abstractions
{
    public sealed class DeviceFoundEventArgs : EventArgs
    {
        public DeviceIdentifier Device { get; }

        public DeviceFoundEventArgs(DeviceIdentifier device)
        {
            ArgumentNullException.ThrowIfNull(device);
            Device = device;
        }
    }

    public sealed class DeviceConnectionEventArgs : EventArgs
    {
        public DeviceIdentifier Device { get; }
        public ConnectionStatus Status { get; }
        public string? Message { get; }
        public Exception? Error { get; }

        public DeviceConnectionEventArgs(DeviceIdentifier device, ConnectionStatus status, string? message = null, Exception? error = null)
        {
            ArgumentNullException.ThrowIfNull(device);
            Device = device;
            Status = status;
            Message = message;
            Error = error;
        }
    }

    /// <summary>
    /// Strongly-typed payloads only (no raw bytes).
    /// </summary>
    public sealed class DeviceDataReceivedEventArgs : EventArgs
    {
        public DeviceIdentifier Device { get; }
        public object Data { get; } // e.g., IndoorBikeData / HeartRateData / CyclingPowerData

        public DeviceDataReceivedEventArgs(DeviceIdentifier device, object data)
        {
            ArgumentNullException.ThrowIfNull(device);
            Device = device;
            Data = data;
        }
    }
}
