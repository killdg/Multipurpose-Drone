using System;
using UnityEngine;

namespace Killdg.MultipurposeDrone
{
    /// <summary>
    /// Cosmetic signal noise over the normal camera image. A tiny owned texture
    /// is refreshed at ten Hz only while visible; there is no camera capture,
    /// readback, network activity, full-screen image allocation or worker thread.
    /// </summary>
    internal sealed class DroneInterference : IDisposable
    {
        private readonly System.Random random = new();
        private readonly Color32[] pixels = new Color32[128 * 72];
        private Texture2D? noise;
        private float nextTexture;
        private float nextBurst;
        private int previousBars;
        private float burstUntil;
        private bool connected;

        internal void Draw(float distance)
        {
            int bars = DroneSignal.Bars(distance);
            float interval = DroneSignal.BurstInterval(distance);
            if (!connected || bars != previousBars)
            {
                connected = true;
                previousBars = bars;
                nextBurst = Time.unscaledTime + interval * (0.5f + (float)random.NextDouble());
            }

            if (Time.unscaledTime >= nextBurst)
            {
                burstUntil = Time.unscaledTime + 0.12f + (float)random.NextDouble() * 0.25f;
                nextBurst = Time.unscaledTime + interval * (0.5f + (float)random.NextDouble());
            }

            float strength = Time.unscaledTime < burstUntil ? DroneSignal.DistanceNoise(distance) : 0;

            DrawNoise(new Rect(0, 0, 1920, 1080), strength);
        }

        internal void DrawNoise(Rect area, float strength)
        {
            if (strength <= 0)
            {
                return;
            }

            if (noise == null)
            {
                noise = new Texture2D(128, 72, TextureFormat.RGBA32, false);
                noise.filterMode = FilterMode.Point;
                noise.wrapMode = TextureWrapMode.Repeat;
            }

            if (Time.unscaledTime >= nextTexture)
            {
                nextTexture = Time.unscaledTime + 0.1f;
                for (int index = 0; index < pixels.Length; index++)
                {
                    byte grey = (byte)random.Next(256);
                    pixels[index] = new Color32(grey, grey, grey, 255);
                }

                noise.SetPixels32(pixels);
                noise.Apply(false);
            }

            Color previous = GUI.color;
            GUI.color = new Color(1, 1, 1, strength);
            GUI.DrawTexture(area, noise);
            GUI.color = new Color(0.8f, 0.9f, 0.85f, strength * 0.5f);
            float scan = Time.unscaledTime * 235 % (area.height - 3);
            GUI.DrawTexture(new Rect(area.x, area.y + scan, area.width, 3), Texture2D.whiteTexture);
            GUI.color = previous;
        }

        internal void Disconnected()
        {
            connected = false;
            burstUntil = 0;
        }

        public void Dispose()
        {
            if (noise != null)
            {
                UnityEngine.Object.Destroy(noise);
                noise = null;
            }

            Disconnected();
        }
    }
}
