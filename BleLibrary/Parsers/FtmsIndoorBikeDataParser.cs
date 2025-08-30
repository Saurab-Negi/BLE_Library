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
        public bool CanParse(Guid serviceUuid, Guid characteristicUuid)
        {
            return serviceUuid == Uuids.Ftms && characteristicUuid == Uuids.Ftms_IndoorBikeData;
        }

        public bool TryParse(ReadOnlySpan<byte> payload, out IDeviceData? data)
        {
            data = null;
            if (payload.Length < 4)
            {
                return false;
            }

            // Flags: 2 bytes little-endian
            ushort flags = (ushort)(payload[0] | (payload[1] << 8));
            int index = 2;

            int? speedDds = null;
            int? cadenceRpm = null;
            int? powerW = null;

            // Example (incomplete): if flags indicate speed present (bit positions per spec)
            // NOTE: Replace bit checks with exact FTMS spec bits for Indoor Bike Data (0x2AD2).
            // The below is a placeholder structure for extension.
            bool instantaneousSpeedPresent = (flags & (1 << 0)) != 0;     // placeholder
            bool instantaneousCadencePresent = (flags & (1 << 2)) != 0;   // placeholder
            bool instantaneousPowerPresent = (flags & (1 << 5)) != 0;     // placeholder

            if (instantaneousSpeedPresent)
            {
                if (payload.Length < index + 2)
                {
                    return false;
                }
                // FTMS speed unit for indoor bike data is typically in 0.01 km/h or 0.1 m/s. Adjust as per spec.
                ushort speedRaw = (ushort)(payload[index] | (payload[index + 1] << 8));
                index += 2;
                // Store as decimeters per second (placeholder conversion)
                speedDds = speedRaw; // TODO: correct unit scaling per SIG spec
            }

            if (instantaneousCadencePresent)
            {
                if (payload.Length < index + 2)
                {
                    return false;
                }
                ushort cadenceRaw = (ushort)(payload[index] | (payload[index + 1] << 8));
                index += 2;
                cadenceRpm = cadenceRaw / 2; // TODO: correct scaling (placeholder)
            }

            if (instantaneousPowerPresent)
            {
                if (payload.Length < index + 2)
                {
                    return false;
                }
                short powerRaw = (short)(payload[index] | (payload[index + 1] << 8));
                index += 2;
                powerW = powerRaw;
            }

            data = new IndoorBikeData
            {
                InstantaneousPowerWatts = powerW,
                CadenceRpm = cadenceRpm,
                SpeedDecimetersPerSecond = speedDds,
                HeartRateBpm = null // sometimes exposed via other characteristics; optional merge if available
            };
            return true;
        }
    }
}
