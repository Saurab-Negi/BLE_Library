using BleLibrary.Domain;
using BleLibrary.Services;

namespace BleLibrary.Parsers
{
    public sealed class HeartRateParser : IProfileParser
    {
        public bool CanParse(Guid serviceUuid, Guid characteristicUuid)
        {
            return serviceUuid == Uuids.Hrs && characteristicUuid == Uuids.Hrs_HeartRateMeasurement;
        }

        public bool TryParse(ReadOnlySpan<byte> payload, out IDeviceData? data)
        {
            data = null;
            if (payload.Length < 2)
            {
                return false;
            }

            // Flags bit 0: 0 = HR 8-bit at [1], 1 = HR 16-bit at [1..2] little-endian
            byte flags = payload[0];
            bool is16 = (flags & 0x01) != 0;

            int hr;
            if (!is16)
            {
                hr = payload[1];
            }
            else
            {
                if (payload.Length < 3)
                {
                    return false;
                }
                hr = payload[1] | (payload[2] << 8);
            }

            data = new HeartRateData { HeartRateBpm = hr };
            return true;
            // TODO: parse optional Energy Expended & RR-intervals based on flags
        }
    }
}
