namespace BleLibrary.Domain
{
    public sealed class IndoorBikeData : IDeviceData
    {
        public int? InstantaneousPowerWatts { get; init; }
        public int? CadenceRpm { get; init; }
        public int? SpeedDecimetersPerSecond { get; init; } // optional FTMS field
        public int? HeartRateBpm { get; init; } // optional FTMS field
    }
}
