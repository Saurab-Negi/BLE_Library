namespace BleLibrary.Domain
{
    public sealed class CadenceSensorData : IDeviceData
    {
        public int? WheelRev { get; init; }
        public int? LastWheelEventTime { get; init; }
        public int? CrankRev { get; init; }
        public int? LastCrankEventTime { get; init; }

        public CadenceSensorData(int? WheelRev, int? LastWheelEventTime, int? CrankRev, int? LastCrankEventTime)
        {
            WheelRev = WheelRev;
            LastWheelEventTime = LastWheelEventTime;
            CrankRev = CrankRev;
            LastCrankEventTime = LastCrankEventTime;
        }

        public override string ToString()
        {
            var wR = WheelRev?.ToString() ?? "none";
            var lW = LastWheelEventTime?.ToString() ?? "none";
            var cR = CrankRev?.ToString() ?? "none";
            var lC = LastCrankEventTime?.ToString() ?? "none";
            return $"WheelRev={wR} rpm, LastWheelEventTime={lW} ms, CrankRev={cR} rpm, LastCrankEventTime={lC} ms";
        }
    }
}
