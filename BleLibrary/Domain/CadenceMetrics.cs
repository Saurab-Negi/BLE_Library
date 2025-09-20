namespace BleLibrary.Domain
{
    public sealed class CadenceMetrics : IDeviceData
    {
        public double? SpeedKph { get; init; } // from wheel data
        public double? CadenceRpm { get; init; } // from crank data

        // (Optional) Raw snapshot for debugging/telemetry
        public CadenceSensorData Raw { get; init; }

        public override string ToString()
        {
            string spd = SpeedKph is null ? "" : $"{SpeedKph:0.0} km/h";
            string cad = CadenceRpm is null ? "" : $"{CadenceRpm:0} rpm";
            return $"Speed={spd}, Cadence={cad}";
        }
    }
}
