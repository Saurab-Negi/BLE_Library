namespace BleLibrary.Abstractions
{
    public enum ConnectionStatus
    {
        Connected,
        Disconnected,
        ConnectionFailed,
        PermissionDenied,
        ConnectionLost, // Unexpected disconnect
        OutOfRange, // RSSI degraded / consecutive read failures
        BatteryLow
    }
    public enum DeviceType
    {
        Unknown,
        HeartRateMonitor,
        PowerMeter,
        SpeedCadenceSensor,
        FitnessMachine,
        VirtualSteering
    }
}
