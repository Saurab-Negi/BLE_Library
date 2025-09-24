namespace BleLibrary.Domain
{
    public sealed class TreadmillData : IDeviceData
    {
        public float? InstantaneousSpeedKmph { get; init; }
        public int? TotalDistanceMeters { get; init; }
        public int? ElapsedTimeSeconds { get; init; }
        public int? HeartRateBpm { get; init; }

        public TreadmillData(float? speedKmph, int? totalDistanceMeters, int? elapsedSeconds)
        {
            InstantaneousSpeedKmph = speedKmph;
            TotalDistanceMeters = totalDistanceMeters;
            ElapsedTimeSeconds = elapsedSeconds;
        }

        public override string ToString()
        {
            var s = InstantaneousSpeedKmph?.ToString("0.##") ?? "none";
            var d = TotalDistanceMeters?.ToString() ?? "none";
            var t = ElapsedTimeSeconds?.ToString() ?? "none";
            return $"Speed={s} km/h, Distance={d} m, Elapsed={t} s";
        }
    }
}
