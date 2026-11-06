using System;

namespace Killdg.MultipurposeDrone
{
    public sealed class FlightBattery
    {
        public bool Launched { get; private set; }
        public double ExpiresAt { get; private set; }

        public void Launch(double now)
        {
            if (Launched)
            {
                throw new InvalidOperationException("One battery per raid in this prototype.");
            }

            Launched = true;
            ExpiresAt = now + 600;
        }

        public double Remaining(double now)
        {
            return Launched ? Math.Max(0, ExpiresAt - now) : 600;
        }
    }
}
