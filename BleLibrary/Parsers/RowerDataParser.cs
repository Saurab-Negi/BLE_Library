using BleLibrary.Domain;
using BleLibrary.Services;

namespace BleLibrary.Parsers
{
    public sealed class RowerDataParser : IProfileParser
    {
        // FTMS Rower Data (0x2AD1) flags (bit positions) — Table 4.9
        private const int FLAG_MORE_DATA = 0;  // 0 => Stroke Rate & Stroke Count present
        private const int FLAG_AVG_STROKE_RATE_PRESENT = 1;  // skip (uint8/uint16 per spec; not exposed here)
        private const int FLAG_TOTAL_DISTANCE_PRESENT = 2;  // skip (uint24)
        private const int FLAG_INSTANT_PACE_PRESENT = 3;  // skip (uint16, time/500m)
        private const int FLAG_AVG_PACE_PRESENT = 4;  // skip (uint16)
        private const int FLAG_INSTANT_POWER_PRESENT = 5;  // parse (sint16, W)
        private const int FLAG_AVG_POWER_PRESENT = 6;  // skip (sint16)
        private const int FLAG_RESISTANCE_LEVEL_PRESENT = 7;  // skip (sint16)
        private const int FLAG_EXPENDED_ENERGY_PRESENT = 8;  // skip (uint16 + uint16 + uint8)
        private const int FLAG_HEART_RATE_PRESENT = 9;  // skip (uint8)
        private const int FLAG_MET_PRESENT = 10; // skip (uint8)
        private const int FLAG_ELAPSED_TIME_PRESENT = 11; // skip (uint16)
        private const int FLAG_REMAINING_TIME_PRESENT = 12; // skip (uint16)

        public bool CanParse(Guid serviceUuid, Guid characteristicUuid)
        {
            return serviceUuid == Uuids.Ftms && characteristicUuid == Uuids.Ftms_RowerData;
        }

        public bool TryParse(ReadOnlySpan<byte> payload, out IDeviceData? data)
        {
            data = null;
            if (payload.Length < 2)
            {
                return false;
            }

            ushort flags = (ushort)(payload[0] | (payload[1] << 8));
            Console.WriteLine($"RowerData payload={Convert.ToHexString(payload.ToArray())}");

            int i = 2;

            float? strokeRateSpm = null;
            int? powerWatt = null;

            // ===== Stroke Rate & Stroke Count are present when bit0 == 0 =====
            bool baseFieldsPresent = ((flags >> FLAG_MORE_DATA) & 0x1) == 0;
            if (baseFieldsPresent)
            {
                // Stroke Rate (UINT16, 0.5 spm)
                if (payload.Length < i + 2) return false;
                ushort srRaw = (ushort)(payload[i] | (payload[i + 1] << 8));
                i += 2;
                strokeRateSpm = srRaw / 2f;
                Console.WriteLine($"=== strokeRateSpm === {strokeRateSpm}");

                // Stroke Count (UINT16) — required but we don't expose it; skip 2 bytes
                if (payload.Length < i + 2) return false;
                i += 2;
            }

            if (((flags >> FLAG_AVG_STROKE_RATE_PRESENT) & 0x1) != 0) { if (payload.Length < i + 2) return false; i += 2; }
            if (((flags >> FLAG_TOTAL_DISTANCE_PRESENT) & 0x1) != 0) { if (payload.Length < i + 3) return false; i += 3; }
            if (((flags >> FLAG_INSTANT_PACE_PRESENT) & 0x1) != 0) { if (payload.Length < i + 2) return false; i += 2; }
            if (((flags >> FLAG_AVG_PACE_PRESENT) & 0x1) != 0) { if (payload.Length < i + 2) return false; i += 2; }

            // ===== Instantaneous Power (sint16, W) =====
            if (((flags >> FLAG_INSTANT_POWER_PRESENT) & 0x1) != 0)
            {
                if (payload.Length < i + 2) return false;
                short pRaw = (short)(payload[i] | (payload[i + 1] << 8));
                i += 2;
                powerWatt = pRaw;
                Console.WriteLine($"=== powerWatt === {powerWatt}");
            }

            if (((flags >> FLAG_AVG_POWER_PRESENT) & 0x1) != 0) { if (payload.Length < i + 2) return false; i += 2; }
            if (((flags >> FLAG_RESISTANCE_LEVEL_PRESENT) & 0x1) != 0) { if (payload.Length < i + 2) return false; i += 2; }
            if (((flags >> FLAG_EXPENDED_ENERGY_PRESENT) & 0x1) != 0) { if (payload.Length < i + 2) return false; i += 2; if (payload.Length < i + 2) return false; i += 2; if (payload.Length < i + 1) return false; i += 1; }
            if (((flags >> FLAG_HEART_RATE_PRESENT) & 0x1) != 0) { if (payload.Length < i + 1) return false; i += 1; }
            if (((flags >> FLAG_MET_PRESENT) & 0x1) != 0) { if (payload.Length < i + 1) return false; i += 1; }
            if (((flags >> FLAG_ELAPSED_TIME_PRESENT) & 0x1) != 0) { if (payload.Length < i + 2) return false; i += 2; }
            if (((flags >> FLAG_REMAINING_TIME_PRESENT) & 0x1) != 0) { if (payload.Length < i + 2) return false; i += 2; }

            data = new RowerData(strokeRateSpm, powerWatt);
            return true;
        }
    }
}
