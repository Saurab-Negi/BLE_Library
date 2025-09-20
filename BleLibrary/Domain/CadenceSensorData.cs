namespace BleLibrary.Domain
{
    public sealed class CadenceSensorData : IDeviceData
    {
        public uint? WheelRev { get; init; } // cumulative
        public ushort? LastWheelEventTime { get; init; } // 1/1024 s
        public ushort? CrankRev { get; init; } // cumulative
        public ushort? LastCrankEventTime { get; init; } // 1/1024 s

        public CadenceSensorData(uint? WheelRev, ushort? LastWheelEventTime, ushort? CrankRev, ushort? LastCrankEventTime)
        {
            this.WheelRev = WheelRev;
            this.LastWheelEventTime = LastWheelEventTime;
            this.CrankRev = CrankRev;
            this.LastCrankEventTime = LastCrankEventTime;
        }

        //public override string ToString()
        //{
        //    var wR = WheelRev?.ToString() ?? "none";
        //    var lW = LastWheelEventTime?.ToString() ?? "none";
        //    var cR = CrankRev?.ToString() ?? "none";
        //    var lC = LastCrankEventTime?.ToString() ?? "none";
        //    return $"WheelRev={wR}, LastWheelEventTime={lW} ms, CrankRev={cR}, LastCrankEventTime={lC} ms";
        //}
    }
}
