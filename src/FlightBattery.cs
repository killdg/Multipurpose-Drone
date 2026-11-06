using System;

namespace Killdg.MultipurposeDrone
{
    public sealed class FlightBattery
    {
        public bool Launched { get; private set; }
        public double ExpiresAt { get; private set; }
        public double Duration { get; private set; } = 600;

        public void Launch(double now, double duration = 600)
        {
            if (Launched)
            {
                throw new InvalidOperationException("One battery per raid in this prototype.");
            }

            Launched = true;
            Duration = duration;
            ExpiresAt = now + duration;
        }

        public double Remaining(double now)
        {
            return Launched ? Math.Max(0, ExpiresAt - now) : Duration;
        }
    }
}
