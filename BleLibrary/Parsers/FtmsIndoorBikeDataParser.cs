using BleLibrary.Domain;
using BleLibrary.Services;

namespace BleLibrary.Parsers
{
    /// <summary>
    /// FTMS Indoor Bike Data (0x2AD2) — payload uses bit flags to indicate optional fields.
    /// This is a scaffold with basic fields; extend parsing per FTMS spec.
    /// </summary>
    public sealed class FtmsIndoorBikeDataParser : IProfileParser
    {
        // FTMS Indoor Bike Data flags (bit positions)
        private const int FLAG_MORE_DATA = 0;  // 0 => Instantaneous Speed present
        private const int FLAG_AVG_SPEED_PRESENT = 1;
        private const int FLAG_INSTANT_CADENCE_PRESENT = 2;  // uint16, 0.5 rpm units
        private const int FLAG_AVG_CADENCE_PRESENT = 3;
        private const int FLAG_TOTAL_DISTANCE_PRESENT = 4;  // 24-bit
        private const int FLAG_RESISTANCE_LEVEL_PRESENT = 5;  // sint16
        private const int FLAG_INSTANT_POWER_PRESENT = 6;  // sint16, watts
        private const int FLAG_AVG_POWER_PRESENT = 7;
        private const int FLAG_EXPENDED_ENERGY_PRESENT = 8;  // total(16) + perHour(16) + perMin(8)
        private const int FLAG_HEART_RATE_PRESENT = 9;  // uint8
        private const int FLAG_MET_PRESENT = 10; // uint8
        private const int FLAG_ELAPSED_TIME_PRESENT = 11; // uint16 (s)
        private const int FLAG_REMAINING_TIME_PRESENT = 12; // uint16 (s)

        public bool CanParse(Guid serviceUuid, Guid characteristicUuid)
        {
            return serviceUuid == Uuids.Ftms && characteristicUuid == Uuids.Ftms_IndoorBikeData;
        }

        public bool TryParse(ReadOnlySpan<byte> payload, out IDeviceData? data)
        {
            data = null;
            if (payload.Length < 2)
            {
                return false;
            }

            // Flags: 2 bytes little-endian
            ushort flags = (ushort)(payload[0] | (payload[1] << 8));
            int i = 2;

            float? speedKmph = null;
            int? cadenceRpm = null;
            int? powerWatt = null;

            // Instantaneous Speed present when bit0 == 0
            bool speedPresent = ((flags >> FLAG_MORE_DATA) & 0x1) == 0;
            if (speedPresent)
            {
                if (payload.Length < i + 2) return false;
                ushort speedRaw = (ushort)(payload[i] | (payload[i + 1] << 8));
                i += 2;
                // speedRaw in 0.01 km/h -> km/h
                speedKmph = (float)(speedRaw / 100.0);
                Console.WriteLine($"=== speedKmph === {speedKmph}");
            }

            // Instantaneous Cadence (0.5 rpm units)
            if (((flags >> FLAG_INSTANT_CADENCE_PRESENT) & 0x1) != 0)
            {
                if (payload.Length < i + 2) return false;
                ushort cadenceRaw = (ushort)(payload[i] | (payload[i + 1] << 8));
                i += 2;
                cadenceRpm = cadenceRaw / 2;
                Console.WriteLine($"=== cadenceRpm === {cadenceRpm}");
            }

            // Skip fields we don't expose
            if (((flags >> FLAG_AVG_SPEED_PRESENT) & 0x1) != 0) { if (payload.Length < i + 2) return false; i += 2; }
            if (((flags >> FLAG_AVG_CADENCE_PRESENT) & 0x1) != 0) { if (payload.Length < i + 2) return false; i += 2; }
            if (((flags >> FLAG_TOTAL_DISTANCE_PRESENT) & 0x1) != 0) { if (payload.Length < i + 3) return false; i += 3; }
            if (((flags >> FLAG_RESISTANCE_LEVEL_PRESENT) & 0x1) != 0) { if (payload.Length < i + 2) return false; i += 2; }

            // Instantaneous Power (watts)
            if (((flags >> FLAG_INSTANT_POWER_PRESENT) & 0x1) != 0)
            {
                if (payload.Length < i + 2) return false;
                short powerRaw = (short)(payload[i] | (payload[i + 1] << 8));
                i += 2;
                powerWatt = powerRaw;
                Console.WriteLine($"=== powerWatt === {powerWatt}");
            }

            // Skip Average Power, Energy, HR, MET, Elapsed/Remaining
            if (((flags >> FLAG_AVG_POWER_PRESENT) & 0x1) != 0) { if (payload.Length < i + 2) return false; i += 2; }
            if (((flags >> FLAG_EXPENDED_ENERGY_PRESENT) & 0x1) != 0) { if (payload.Length < i + 2) return false; i += 2; if (payload.Length < i + 2) return false; i += 2; if (payload.Length < i + 1) return false; i += 1; }
            if (((flags >> FLAG_HEART_RATE_PRESENT) & 0x1) != 0) { if (payload.Length < i + 1) return false; i += 1; }
            if (((flags >> FLAG_MET_PRESENT) & 0x1) != 0) { if (payload.Length < i + 1) return false; i += 1; }
            if (((flags >> FLAG_ELAPSED_TIME_PRESENT) & 0x1) != 0) { if (payload.Length < i + 2) return false; i += 2; }
            if (((flags >> FLAG_REMAINING_TIME_PRESENT) & 0x1) != 0) { if (payload.Length < i + 2) return false; i += 2; }

            data = new IndoorBikeData(powerWatt, cadenceRpm, speedKmph);
            return true;
        }
    }
}
