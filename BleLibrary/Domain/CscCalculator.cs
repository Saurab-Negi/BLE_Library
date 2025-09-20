using System.Collections.Concurrent;

namespace BleLibrary.Domain
{
    public sealed class CscCalculator
    {
        public double WheelCircumferenceMeters { get; set; } = 2.10; // ~700x25c ≈ 2.096m
        private const double MAX_SPEED_KPH = 120.0;
        private const double MAX_CADENCE_RPM = 250.0;

        private sealed class State
        {
            public uint? PrevWheelRev;
            public ushort? PrevWheelTime; // 1/1024 s ticks
            public ushort? PrevCrankRev;  // cumulative (16-bit)
            public ushort? PrevCrankTime; // 1/1024 s ticks
        }

        private readonly ConcurrentDictionary<Guid, State> _byDevice = new();

        private const double MAX_ACCEPT_DT_SEC = 10.0; // drop packets that imply >10s gap

        private static double ForwardDtSec16(ushort curr, ushort prev)
        {
            ushort ticks = (ushort)(curr - prev); // forward distance modulo 65536
            return ticks / 1024.0;
        }

        private static bool LooksLikeOutOfOrderWheel(uint curr, uint prev)
        {
            return curr < prev; // true => treat as OOO, not a real wrap
        }

        public CadenceMetrics Update(Guid deviceId, CadenceSensorData current)
        {
            var st = _byDevice.GetOrAdd(deviceId, _ => new State());

            double? speedKph = null;
            double? cadenceRpm = null;

            // --- Speed from wheel data ---
            if (current.WheelRev.HasValue && current.LastWheelEventTime.HasValue)
            {
                if (st.PrevWheelRev.HasValue && st.PrevWheelTime.HasValue)
                {
                    // Reject out-of-order by time and by 32-bit “wrap”
                    double dtSecFwd = ForwardDtSec16(current.LastWheelEventTime.Value, st.PrevWheelTime.Value);
                    if (dtSecFwd > 0 && dtSecFwd <= MAX_ACCEPT_DT_SEC && !LooksLikeOutOfOrderWheel(current.WheelRev.Value, st.PrevWheelRev.Value))
                    {
                        ulong dRevs = DeltaUInt32(current.WheelRev.Value, st.PrevWheelRev.Value);
                        if (dRevs == 0) speedKph = 0.0;
                        else
                        {
                            double mps = (dRevs * WheelCircumferenceMeters) / dtSecFwd;
                            double kph = mps * 3.6;
                            if (kph <= MAX_SPEED_KPH) speedKph = kph; // else drop
                        }
                    }
                }

                st.PrevWheelRev = current.WheelRev.Value;
                st.PrevWheelTime = current.LastWheelEventTime.Value;
            }

            // --- Cadence from crank data ---
            if (current.CrankRev.HasValue && current.LastCrankEventTime.HasValue)
            {
                if (st.PrevCrankRev.HasValue && st.PrevCrankTime.HasValue)
                {
                    double dtSecFwd = ForwardDtSec16(current.LastCrankEventTime.Value, st.PrevCrankTime.Value);
                    if (dtSecFwd > 0 && dtSecFwd <= MAX_ACCEPT_DT_SEC)
                    {
                        uint dRevs = DeltaUInt16(current.CrankRev.Value, st.PrevCrankRev.Value);
                        if (dRevs == 0) cadenceRpm = 0.0;
                        else
                        {
                            double rpm = (dRevs / dtSecFwd) * 60.0;
                            if (rpm <= MAX_CADENCE_RPM) cadenceRpm = rpm; // else drop
                        }
                    }
                }

                st.PrevCrankRev = current.CrankRev.Value;
                st.PrevCrankTime = current.LastCrankEventTime.Value;
            }

            return new CadenceMetrics
            {
                SpeedKph = speedKph,
                CadenceRpm = cadenceRpm,
                Raw = current
            };
        }

        // --- Helpers ---

        private static uint DeltaUInt16(ushort curr, ushort prev)
        {
            // For cumulative 16-bit counters (crank revolutions)
            return (uint)((ushort)(curr - prev)); // wrap-safe
        }

        private static ulong DeltaUInt32(uint curr, uint prev)
        {
            // For cumulative 32-bit counters (wheel revolutions)
            return curr >= prev
                ? (ulong)(curr - prev)
                : (ulong)curr + (1UL << 32) - prev; // wrap-safe
        }
    }
}
