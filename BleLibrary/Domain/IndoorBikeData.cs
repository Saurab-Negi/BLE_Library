namespace BleLibrary.Domain
{
    public sealed class IndoorBikeData : IDeviceData
    {
        public int? InstantaneousPowerWatt { get; init; }
        public int? InstantaneousCadenceRpm { get; init; }
        public float? InstantaneousSpeedKmph { get; init; }
        public int? HeartRateBpm { get; init; } // optional FTMS field

        public IndoorBikeData(int? powerWatts, int? cadenceRpm, float? speedKmph)
        {
            InstantaneousPowerWatt = powerWatts;
            InstantaneousCadenceRpm = cadenceRpm;
            InstantaneousSpeedKmph = speedKmph;
        }

        public override string ToString()
        {
            var p = InstantaneousPowerWatt?.ToString() ?? "none";
            var c = InstantaneousCadenceRpm?.ToString() ?? "none";
            var s = InstantaneousSpeedKmph?.ToString() ?? "none";
            return $"Power={p} W, Cadence={c} rpm, Speed={s} km/h";
        }
    }
}
