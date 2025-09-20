using BleLibrary.Domain;
using BleLibrary.Services;

namespace BleLibrary.Parsers
{
    public sealed class CadenceSensorParser : IProfileParser
    {
        // Flags (CSC Measurement 0x2A5B)
        private const int FLAG_WHEEL_PRESENT = 0; // bit0: Wheel Revolution Data Present
        private const int FLAG_CRANK_PRESENT = 1; // bit1: Crank Revolution Data Present

        public bool CanParse(Guid serviceUuid, Guid characteristicUuid)
        {
            return serviceUuid == Uuids.Csc && characteristicUuid == Uuids.Csc_CadenceMeasurement;
        }

        public bool TryParse(ReadOnlySpan<byte> payload, out IDeviceData? data)
        {
            data = null;
            if (payload.Length < 1) return false;

            byte flags = payload[0];
            Console.WriteLine($"CSC payload={Convert.ToHexString(payload.ToArray())}");

            int i = 1;

            bool wheelPresent = ((flags >> FLAG_WHEEL_PRESENT) & 1) != 0;
            bool crankPresent = ((flags >> FLAG_CRANK_PRESENT) & 1) != 0;

            uint? wheelRev = null;
            ushort? lastWheelEventTime = null;
            ushort? crankRev = null;
            ushort? lastCrankEventTime = null;

            if (wheelPresent)
            {
                // Cumulative Wheel Revolutions: uint32
                // Last Wheel Event Time: uint16 (1/1024 s)
                if (payload.Length < i + 6) return false;
                uint wheelRevs = (uint)(payload[i]
                                      | (payload[i + 1] << 8)
                                      | (payload[i + 2] << 16)
                                      | (payload[i + 3] << 24));
                i += 4;

                ushort wheelTime = (ushort)(payload[i] | (payload[i + 1] << 8));
                i += 2;

                wheelRev = unchecked(wheelRevs);
                Console.WriteLine($"=== wheelRev === {wheelRev}");
                lastWheelEventTime = wheelTime;
                Console.WriteLine($"=== lastWheelEventTime === {lastWheelEventTime}");
            }

            if (crankPresent)
            {
                // Cumulative Crank Revolutions: uint16
                // Last Crank Event Time: uint16 (1/1024 s)
                if (payload.Length < i + 4) return false;
                ushort crankRevs = (ushort)(payload[i] | (payload[i + 1] << 8));
                i += 2;

                ushort crankTime = (ushort)(payload[i] | (payload[i + 1] << 8));
                i += 2;

                crankRev = crankRevs;
                Console.WriteLine($"=== crankRev === {crankRev}");
                lastCrankEventTime = crankTime;
                Console.WriteLine($"=== lastCrankEventTime === {lastCrankEventTime}");
            }

            data = new CadenceSensorData(wheelRev, lastWheelEventTime, crankRev, lastCrankEventTime);
            return true;
        }
    }
}
