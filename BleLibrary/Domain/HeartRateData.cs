namespace BleLibrary.Domain
{
    public sealed class HeartRateData : IDeviceData
    {
        public int HeartRateBpm { get; init; }
    }
}
