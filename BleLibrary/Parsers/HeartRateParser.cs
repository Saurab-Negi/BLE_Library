using BleLibrary.Domain;
using BleLibrary.Services;

namespace BleLibrary.Parsers
{
    public sealed class HeartRateParser : IProfileParser
    {
        private const byte FLAG_HR_16BIT = 0x01; // bit0
        private const byte FLAG_ENERGY_EXPENDED = 0x08; // bit3
        private const byte FLAG_RR_INTERVALS = 0x10; // bit4

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
            int index = 0;
            byte flags = payload[index++];

            // --- Heart Rate ---
            bool hr16 = (flags & FLAG_HR_16BIT) != 0;
            if (hr16)
            {
                if (index + 1 >= payload.Length) return false;
                int hr = payload[index] | (payload[index + 1] << 8);
                index += 2;

                // --- Energy Expended (optional) ---
                int? energy = null;
                if ((flags & FLAG_ENERGY_EXPENDED) != 0)
                {
                    if (index + 1 >= payload.Length) return false;
                    energy = payload[index] | (payload[index + 1] << 8);
                    index += 2;
                }

                // --- RR-Intervals (optional, 0..N) ---
                var rrList = new List<int>();
                if ((flags & FLAG_RR_INTERVALS) != 0)
                {
                    // Each RR is uint16 in units of 1/1024 second; convert to ms
                    while (index + 1 < payload.Length)
                    {
                        int rr1024 = payload[index] | (payload[index + 1] << 8);
                        index += 2;
                        // ms = rr * 1000 / 1024; round to nearest int
                        int rrMs = (int)Math.Round(rr1024 * 1000.0 / 1024.0);
                        rrList.Add(rrMs);
                    }
                }

                data = new HeartRateData(hr, energy, rrList);
                return true;
            }
            else
            {
                // HR is 8-bit
                int hr = payload[index++];
                if (hr < 0) hr = 0;

                int? energy = null;
                if ((flags & FLAG_ENERGY_EXPENDED) != 0)
                {
                    if (index + 1 >= payload.Length) return false;
                    energy = payload[index] | (payload[index + 1] << 8);
                    index += 2;
                }

                var rrList = new List<int>();
                if ((flags & FLAG_RR_INTERVALS) != 0)
                {
                    while (index + 1 < payload.Length)
                    {
                        int rr1024 = payload[index] | (payload[index + 1] << 8);
                        index += 2;
                        int rrMs = (int)Math.Round(rr1024 * 1000.0 / 1024.0);
                        rrList.Add(rrMs);
                    }
                }

                data = new HeartRateData(hr, energy, rrList);
                return true;
            }
        }
    }
}
