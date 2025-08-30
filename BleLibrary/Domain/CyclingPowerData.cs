namespace BleLibrary.Domain
{
    public sealed class CyclingPowerData : IDeviceData
    {
        public int InstantaneousPowerWatts { get; init; }
        public int? CadenceRpm { get; init; } // optional if derived or present via flags
    }
}
