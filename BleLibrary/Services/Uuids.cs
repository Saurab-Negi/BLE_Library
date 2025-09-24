namespace BleLibrary.Services
{
    internal static class Uuids
    {
        // Services
        public static readonly Guid Ftms = Guid.Parse("00001826-0000-1000-8000-00805f9b34fb"); // Fitness Machine
        public static readonly Guid Hrs = Guid.Parse("0000180d-0000-1000-8000-00805f9b34fb"); // Heart Rate
        public static readonly Guid Csc = Guid.Parse("00001816-0000-1000-8000-00805f9b34fb"); // Cadence
        public static readonly Guid Cps = Guid.Parse("00001818-0000-1000-8000-00805f9b34fb"); // Cycling Power
        public static readonly Guid Battery = Guid.Parse("0000180f-0000-1000-8000-00805f9b34fb"); // Battery Level

        // Characteristics
        public static readonly Guid Ftms_IndoorBikeData = Guid.Parse("00002ad2-0000-1000-8000-00805f9b34fb");
        public static readonly Guid Ftms_TreadmillData = Guid.Parse("00002acd-0000-1000-8000-00805f9b34fb");
        public static readonly Guid Ftms_RowerData = Guid.Parse("00002ad1-0000-1000-8000-00805f9b34fb");
        public static readonly Guid Ftms_FitnessMachineCtrlPoint = Guid.Parse("00002ad9-0000-1000-8000-00805f9b34fb");
        public static readonly Guid Csc_CadenceMeasurement = Guid.Parse("00002a5b-0000-1000-8000-00805f9b34fb");
        public static readonly Guid Hrs_HeartRateMeasurement = Guid.Parse("00002a37-0000-1000-8000-00805f9b34fb");

        public static readonly Guid Cps_CyclingPowerMeasurement = Guid.Parse("00002a63-0000-1000-8000-00805f9b34fb");
        public static readonly Guid BatteryLevel = Guid.Parse("00002a19-0000-1000-8000-00805f9b34fb");
    }
}
