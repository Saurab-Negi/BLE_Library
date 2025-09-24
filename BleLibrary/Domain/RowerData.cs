namespace BleLibrary.Domain
{
    public sealed class RowerData : IDeviceData
    {
        public float? StrokeRateSpm { get; init; }
        public int? InstantaneousPowerWatt { get; init; }
        public int? HeartRateBpm { get; init; }

        public RowerData(float? strokeRateSpm, int? instantaneousPowerWatt)
        {
            StrokeRateSpm = strokeRateSpm;
            InstantaneousPowerWatt = instantaneousPowerWatt;
        }

        public override string ToString()
        {
            var sr = StrokeRateSpm?.ToString("0.#") ?? "none";
            var p = InstantaneousPowerWatt?.ToString() ?? "none";
            return $"StrokeRate={sr} spm, Power={p} W";
        }
    }
}
