namespace BleLibrary.Abstractions
{
    /// <summary>
    /// Stable app-level identifier decoupled from Plugin.BLE.
    /// </summary>
    public sealed record DeviceIdentifier(string Id, string? Name = null, string? Address = null);
}
