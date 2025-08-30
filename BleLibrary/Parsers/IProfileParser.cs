using BleLibrary.Domain;

namespace BleLibrary.Parsers
{
    public interface IProfileParser
    {
        bool CanParse(Guid serviceUuid, Guid characteristicUuid);
        bool TryParse(ReadOnlySpan<byte> payload, out IDeviceData? data);
    }
}
