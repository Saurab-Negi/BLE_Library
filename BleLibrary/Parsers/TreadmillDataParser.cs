using BleLibrary.Domain;
using BleLibrary.Services;

namespace BleLibrary.Parsers
{
    public sealed class TreadmillDataParser : IProfileParser
    {
        private const int FLAG_MORE_DATA = 0;                 // 0 => Instantaneous Speed present
        private const int FLAG_AVG_SPEED_PRESENT = 1;         // uint16 (0.01 km/h)
        private const int FLAG_TOTAL_DISTANCE_PRESENT = 2;    // uint24 (meters)
        private const int FLAG_INCLINATION_RAMP_PRESENT = 3;  // two sint16 (inclination, ramp angle) -> 4 bytes
        private const int FLAG_ELEVATION_GAIN_PRESENT = 4;    // two uint16 (pos gain, neg gain) -> 4 bytes
        private const int FLAG_INSTANT_PACE_PRESENT = 5;      // uint16 (0.1 s/m)
        private const int FLAG_AVG_PACE_PRESENT = 6;          // uint16 (0.1 s/m)
        private const int FLAG_EXPENDED_ENERGY_PRESENT = 7;   // total(16) + perHour(16) + perMin(8) -> 5 bytes
        private const int FLAG_HEART_RATE_PRESENT = 8;        // uint8
        private const int FLAG_MET_PRESENT = 9;               // uint8
        private const int FLAG_ELAPSED_TIME_PRESENT = 10;     // uint16 (seconds)
        private const int FLAG_REMAINING_TIME_PRESENT = 11;   // uint16 (seconds)
        private const int FLAG_FORCE_POWER_PRESENT = 12;      // force on belt (sint16) + power output (sint16) -> 4 bytes

        public bool CanParse(Guid serviceUuid, Guid characteristicUuid)
        {
            return serviceUuid == Uuids.Ftms && characteristicUuid == Uuids.Ftms_TreadmillData;
        }

        public bool TryParse(ReadOnlySpan<byte> payload, out IDeviceData? data)
        {
            data = null;
            if (payload.Length < 2)
            {
                return false;
            }

            ushort flags = (ushort)(payload[0] | (payload[1] << 8));
            Console.WriteLine($"Treadmill payload={Convert.ToHexString(payload.ToArray())}");

            int i = 2;

            float? speedKmph = null;
            int? totalMeters = null;
            int? elapsedSeconds = null;

            // ===== Instantaneous Speed =====
            // Present when bit0 == 0
            bool speedPresent = ((flags >> FLAG_MORE_DATA) & 0x1) == 0;
            if (speedPresent)
            {
                if (payload.Length < i + 2) return false;
                ushort speedRaw = (ushort)(payload[i] | (payload[i + 1] << 8)); // 0.01 km/h
                i += 2;
                speedKmph = (float)(speedRaw / 100.0);
                Console.WriteLine($"=== speedKmph === {speedKmph}");
            }

            // Skip fields we don't expose
            if (((flags >> FLAG_AVG_SPEED_PRESENT) & 0x1) != 0) { if (payload.Length < i + 2) return false; i += 2; }

            if (((flags >> FLAG_TOTAL_DISTANCE_PRESENT) & 0x1) != 0)
            {
                if (payload.Length < i + 3) return false;
                int dist = (payload[i] | (payload[i + 1] << 8) | (payload[i + 2] << 16));
                i += 3;
                totalMeters = dist;
                Console.WriteLine($"=== totalMeters === {totalMeters}");
            }

            if (((flags >> FLAG_INCLINATION_RAMP_PRESENT) & 0x1) != 0) { if (payload.Length < i + 4) return false; i += 4; }
            if (((flags >> FLAG_ELEVATION_GAIN_PRESENT) & 0x1) != 0) { if (payload.Length < i + 4) return false; i += 4; }
            if (((flags >> FLAG_INSTANT_PACE_PRESENT) & 0x1) != 0) { if (payload.Length < i + 2) return false; i += 2; }
            if (((flags >> FLAG_AVG_PACE_PRESENT) & 0x1) != 0) { if (payload.Length < i + 2) return false; i += 2; }
            if (((flags >> FLAG_EXPENDED_ENERGY_PRESENT) & 0x1) != 0) { if (payload.Length < i + 5) return false; i += 5; }
            if (((flags >> FLAG_HEART_RATE_PRESENT) & 0x1) != 0) { if (payload.Length < i + 1) return false; i += 1; }
            if (((flags >> FLAG_MET_PRESENT) & 0x1) != 0) { if (payload.Length < i + 1) return false; i += 1; }

            // ===== Elapsed Time (seconds) =====
            if (((flags >> FLAG_ELAPSED_TIME_PRESENT) & 0x1) != 0)
            {
                if (payload.Length < i + 2) return false;
                ushort el = (ushort)(payload[i] | (payload[i + 1] << 8));
                i += 2;
                elapsedSeconds = el;
                Console.WriteLine($"=== elapsedSeconds === {elapsedSeconds}");
            }

            if (((flags >> FLAG_REMAINING_TIME_PRESENT) & 0x1) != 0) { if (payload.Length < i + 2) return false; i += 2; }
            if (((flags >> FLAG_FORCE_POWER_PRESENT) & 0x1) != 0) { if (payload.Length < i + 4) return false; i += 4; }

            data = new TreadmillData(speedKmph, totalMeters, elapsedSeconds);
            return true;
        }
    }
}
