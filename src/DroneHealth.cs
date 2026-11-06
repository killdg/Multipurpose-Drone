using System;

namespace Killdg.MultipurposeDrone
{
    public sealed class DroneHealth
    {
        public float Remaining { get; private set; } = 100;
        public bool Destroyed => Remaining <= 0;

        public bool ApplyDamage(float damage)
        {
            if (Destroyed || float.IsNaN(damage) || float.IsInfinity(damage) || damage <= 0)
            {
                return false;
            }

            Remaining = Math.Max(0, Remaining - damage);
            return Destroyed;
        }
    }
}
