using BleLibrary.Domain;
using BleLibrary.Services;

namespace BleLibrary.Parsers
{
    public sealed class CyclingPowerParser : IProfileParser
    {
        public bool CanParse(Guid serviceUuid, Guid characteristicUuid)
        {
            return serviceUuid == Uuids.Cps && characteristicUuid == Uuids.Cps_CyclingPowerMeasurement;
        }

        public bool TryParse(ReadOnlySpan<byte> payload, out IDeviceData? data)
        {
            data = null;
            if (payload.Length < 4)
            {
                return false;
            }

            // First 2 bytes: flags (little-endian)
            // Next 2 bytes: Instantaneous Power (signed, watts)
            short power = (short)(payload[2] | (payload[3] << 8));

            data = new CyclingPowerData
            {
                InstantaneousPowerWatts = power,
                CadenceRpm = null // TODO: derive from optional fields if present in flags
            };
            return true;

            // TODO: parse pedal smoothness, torque, cadence if flags indicate presence
        }
    }
}
