using Plugin.BLE.Abstractions;

namespace BleLibrary.Abstractions
{
    /// <summary>
    /// Stable app-level identifier decoupled from Plugin.BLE.
    /// </summary>
    public sealed record DeviceIdentifier(Guid Id, string Name, int Rssi, object NativeDevice, DeviceState State, IReadOnlyList<AdvertisementRecord> AdvertisementRecords);
}
