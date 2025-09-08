namespace BleLibrary.Domain
{
    public sealed class HeartRateData : IDeviceData
    {
        public int HeartRateBpm { get; init; }
        public int? EnergyExpended { get; }
        public IReadOnlyList<int> RrIntervals { get; }

        public HeartRateData(int heartRate, int? energyExpended, IReadOnlyList<int> rrIntervals)
        {
            HeartRateBpm = heartRate;
            EnergyExpended = energyExpended;
            RrIntervals = rrIntervals ?? Array.Empty<int>();
        }

        public override string ToString()
        {
            var rr = (RrIntervals?.Count ?? 0) > 0 ? string.Join(",", RrIntervals) : "none";
            var energy = EnergyExpended.HasValue ? EnergyExpended.Value.ToString() : "none";
            return $"HR={HeartRateBpm}, RR=[{rr}]";
        }
    }
}
