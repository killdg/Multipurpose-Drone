using System;
using System.IO;
using UnityEngine;

namespace Killdg.MultipurposeDrone
{
    internal sealed class DroneAudio : IDisposable
    {
        private readonly AudioSource source;
        private readonly AudioClip flight;
        private readonly AudioClip landing;
        private bool landed;

        internal DroneAudio(GameObject drone, float volume)
        {
            source = drone.AddComponent<AudioSource>();
            source.spatialBlend = 1;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = 2;
            source.maxDistance = 65;
            source.volume = volume;
            flight = Read("flight.wav");
            landing = Read("landing.wav");
            // Run the sustained rotor segment from deployment until shutdown,
            // including while disconnected. No repeated attack/release envelope.
            source.priority = 64;
            source.dopplerLevel = 0;
            source.clip = flight;
            source.loop = true;
            source.Play();
        }

        internal void Update(bool depleted, float volume)
        {
            source.volume = volume;
            if (depleted && !landed)
            {
                landed = true;
                source.loop = false;
                source.clip = landing;
                source.Play();
            }

        }

        private static AudioClip Read(string name)
        {
            string path = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location)!, "assets", "audio", name);
            using var reader = new BinaryReader(File.OpenRead(path));
            if (new string(reader.ReadChars(4)) != "RIFF")
            {
                throw new InvalidDataException("Expected PCM WAV: " + name);
            }
            reader.ReadInt32();
            reader.ReadChars(4);
            int rate = 24000;
            while (reader.BaseStream.Position + 8 <= reader.BaseStream.Length)
            {
                string chunk = new(reader.ReadChars(4));
                int length = reader.ReadInt32();
                long next = reader.BaseStream.Position + length + (length & 1);
                if (chunk == "fmt ")
                {
                    if (reader.ReadInt16() != 1 || reader.ReadInt16() != 1)
                    {
                        throw new InvalidDataException("Expected mono PCM WAV.");
                    }
                    rate = reader.ReadInt32();
                    reader.ReadInt32();
                    reader.ReadInt16();
                    if (reader.ReadInt16() != 16)
                    {
                        throw new InvalidDataException("Expected 16-bit WAV.");
                    }
                }
                if (chunk == "data")
                {
                    var samples = new float[length / 2];
                    for (int index = 0; index < samples.Length; index++)
                    {
                        samples[index] = reader.ReadInt16() / 32768f;
                    }
                    var clip = AudioClip.Create("MS_Drone_" + name, samples.Length, 1, rate, false);
                    clip.SetData(samples, 0);
                    return clip;
                }
                reader.BaseStream.Position = next;
            }
            throw new InvalidDataException("Missing WAV samples: " + name);
        }

        public void Dispose()
        {
            if (source != null)
            {
                source.Stop();
            }
            UnityEngine.Object.Destroy(flight);
            UnityEngine.Object.Destroy(landing);
        }
    }
}
