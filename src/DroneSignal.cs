namespace Killdg.MultipurposeDrone
{
    public static class DroneSignal
    {
        public const float DropRiskDistance = 400f;
        public const float MaximumConnectionDistance = 450f;

        public static int Bars(float distance)
        {
            if (distance <= 200f) { return 4; }
            if (distance <= 300f) { return 3; }
            if (distance <= 350f) { return 2; }
            if (distance <= DropRiskDistance) { return 1; }
            return 0;
        }

        public static float BurstInterval(float distance)
        {
            return Bars(distance) switch
            {
                4 => 14f,
                3 => 8f,
                2 => 4f,
                1 => 2f,
                _ => 0.8f
            };
        }

        public static float DistanceNoise(float distance)
        {
            return (4 - Bars(distance)) * 0.032f + 0.055f;
        }

        // A single roll per second in the fringe; loss becomes more likely
        // toward the hard cutoff. The same threshold gates manual reconnection.
        public static float DropChance(float distance)
        {
            if (distance <= DropRiskDistance) { return 0f; }
            if (distance >= MaximumConnectionDistance) { return 1f; }
            return 0.08f + (distance - DropRiskDistance) / 50f * 0.32f;
        }
    }
}
