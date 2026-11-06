namespace Killdg.MultipurposeDrone
{
    internal static class DroneTestPolicy
    {
        internal static bool NeedsFreshAircraft(bool testing, bool destroyed,
            bool launched, bool aircraftExists, double batteryRemaining)
        {
            return testing && (destroyed || launched && (!aircraftExists || batteryRemaining <= 0));
        }
    }
}
