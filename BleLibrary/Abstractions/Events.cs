namespace BleLibrary.Abstractions
{
    public sealed class DeviceFoundEventArgs : EventArgs
    {
        public DeviceIdentifier Device { get; }
        public int Rssi { get; }

        public DeviceFoundEventArgs(DeviceIdentifier device, int rssi)
        {
            Device = device;
            Rssi = rssi;
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
            Device = device;
            Data = data;
        }
    }
}
