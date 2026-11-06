using Comfort.Common;
using UnityEngine;
using UnityEngine.Audio;

namespace Killdg.MultipurposeDrone
{
    internal sealed class DroneWorldAudio
    {
        // Same exposed gameplay bus used by Stormtide. This leaves music,
        // interface cues, and the drone's own AudioSource alone.
        private const string WorldVolume = "WorldVolume";
        private const float AttenuationDb = 3f;
        private AudioMixer? mixer;
        private float original;
        private float applied;
        private bool owned;

        internal void SetActive(bool active)
        {
            AudioMixer? current = active && Singleton<BetterAudio>.Instantiated
                ? Singleton<BetterAudio>.Instance.Master : null;
            if (mixer != current)
            {
                Restore();
                mixer = current;
            }

            if (current == null || !current.GetFloat(WorldVolume, out float value))
            {
                return;
            }

            if (owned && Mathf.Abs(value - applied) < 0.01f)
            {
                return;
            }

            original = value;
            applied = value - AttenuationDb;
            owned = current.SetFloat(WorldVolume, applied);
        }

        private void Restore()
        {
            // Do not overwrite a later change made by the game or another mod.
            if (owned && mixer != null && mixer.GetFloat(WorldVolume, out float value)
                && Mathf.Abs(value - applied) < 0.01f)
            {
                mixer.SetFloat(WorldVolume, original);
            }

            owned = false;
        }
    }
}
