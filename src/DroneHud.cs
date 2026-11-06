using System;
using UnityEngine;

namespace Killdg.MultipurposeDrone
{
    /// <summary>
    /// Transparent telemetry over the native full-screen drone viewpoint.
    /// The optional inset displays a crop of the native camera feed.
    /// No world assets, native notification queues or external textures are used.
    /// </summary>
    internal sealed class DroneHud : IDisposable
    {
        private GUIStyle? label;
        private GUIStyle? rightLabel;
        private GUIStyle? smallLabel;
        private float zoomOpenedAt;
        private bool zoomWasOpen;
        private readonly DroneInterference interference = new();
        private static readonly Color Ink = new Color(0.8f, 0.96f, 0.9f, 0.9f);

        internal void Draw(bool connected, bool deployed, double remaining,
            float heading, float pitch, float speed, float height, float range, string status, float health, bool boost, int zoomMagnification, Texture? zoom, bool targetSighted)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            Matrix4x4 previousMatrix = GUI.matrix;
            Color previousColor = GUI.color;
            int previousDepth = GUI.depth;
            try
            {
                GUI.matrix = Matrix4x4.Scale(new Vector3(Screen.width / 1920f, Screen.height / 1080f, 1));
                GUI.depth = -100;
                EnsureStyles();
                bool zoomOpen = zoomMagnification > 0;
                if (connected)
                {
                    interference.Draw(range);
                    DrawTelemetry(heading, pitch, speed, height, range, remaining, health, zoomOpen);
                    if (zoomOpen)
                    {
                        if (!zoomWasOpen)
                        {
                            zoomOpenedAt = Time.unscaledTime;
                        }
                        DrawZoom(zoom, zoomMagnification);
                    }
                }
                else
                {
                    interference.Disconnected();
                }

                zoomWasOpen = connected && zoomOpen;
                DrawBattery(deployed, connected, remaining, status, health, boost, targetSighted);
            }
            finally
            {
                GUI.matrix = previousMatrix;
                GUI.color = previousColor;
                GUI.depth = previousDepth;
            }
        }

        private void EnsureStyles()
        {
            if (label != null)
            {
                return;
            }

            label = new GUIStyle(GUI.skin.label);
            label.fontSize = 19;
            label.normal.textColor = Ink;
            rightLabel = new GUIStyle(label);
            rightLabel.alignment = TextAnchor.UpperRight;
            smallLabel = new GUIStyle(label);
            smallLabel.fontSize = 14;
            smallLabel.wordWrap = true;
        }

        private void DrawTelemetry(float heading, float pitch, float speed,
            float height, float range, double remaining, float health, bool zoomVisible)
        {
            float left = 80;
            float right = 1920 - left;
            float top = 50;
            float bottom = 900;
            Fill(new Rect(left, top, right - left, 32), new Color(0.015f, 0.025f, 0.025f, 0.18f));
            for (int index = 0; index < 4; index++)
            {
                float barHeight = 5 + index * 5;
                Color color = index < DroneSignal.Bars(range)
                    ? Ink : new Color(Ink.r, Ink.g, Ink.b, 0.22f);
                Fill(new Rect(left + 13 + index * 11, top + 27 - barHeight, 7, barHeight), color);
            }
            Text(new Rect(right - 230, top + 2, 220, 30),
                "BAT " + Math.Ceiling(remaining / 6).ToString("0") + "%", true);
            float normalizedHeading = (heading % 360 + 360) % 360;
            Text(new Rect(830, top + 2, 260, 30), "HDG " + normalizedHeading.ToString("000") + "°");

            Corner(left, top + 55, 1, 1);
            Corner(right, top + 55, -1, 1);
            Corner(left, bottom, 1, -1);
            Corner(right, bottom, -1, -1);
            float reticleSize = zoomVisible ? 8 : 30;
            Fill(new Rect(960 - reticleSize / 2, 539, reticleSize, 2), Ink);
            Fill(new Rect(959, 540 - reticleSize / 2, 2, reticleSize), Ink);
            Text(new Rect(910, zoomVisible ? 602 : 562, 140, 28), range.ToString("0.0") + " m");
            Text(new Rect(left + 14, 492, 190, 28), "SPEED");
            Text(new Rect(left + 14, 520, 190, 28), (speed * 3.6f).ToString("0.0") + " km/h");
            Text(new Rect(right - 205, 492, 190, 28), "HEIGHT AGL", true);
            Text(new Rect(right - 205, 520, 190, 28),
                height >= 0 ? height.ToString("0.0") + " m" : "NO GROUND", true);
            Text(new Rect(left + 14, bottom - 35, 450, 28),
                "PITCH " + (-pitch).ToString("+0;-0;0") + "°   /   HP " + health.ToString("0") + "/100");
            Text(new Rect(right - 450, bottom - 35, 435, 28), "F10 DISCONNECT", true);
        }

        private void DrawZoom(Texture? zoom, int magnification)
        {
            Rect area = new Rect(1520, 105, 320, 180);
            Fill(new Rect(area.x - 3, area.y - 3, area.width + 6, area.height + 6), Ink);
            Fill(area, new Color(0.08f, 0.09f, 0.08f));
            if (zoom != null)
            {
                // The presented frame on Direct3D has a top-origin Y axis.
                // Its scene RGB is valid even where the backbuffer alpha is
                // clear. Draw it opaque so the backdrop cannot obscure it.
                float insetAspect = (area.width * Screen.width / 1920f)
                    / (area.height * Screen.height / 1080f);
                float cropSize = 1f / magnification;
                float cropWidth = cropSize * insetAspect / (zoom.width / (float)zoom.height);
                float cropHeight = SystemInfo.graphicsUVStartsAtTop ? -cropSize : cropSize;
                GUI.color = Color.white;
                GUI.DrawTextureWithTexCoords(area, zoom,
                    new Rect(0.5f - cropWidth / 2f, 0.5f - cropHeight / 2f, cropWidth, cropHeight),
                    alphaBlend: false);
            }

            float elapsed = Time.unscaledTime - zoomOpenedAt;
            float staticOpacity = zoom == null ? 1f : 1f - Mathf.SmoothStep(0f, 1f, elapsed / 0.45f);
            interference.DrawNoise(area, staticOpacity);
            Text(new Rect(area.x + 8, area.y + 5, 90, 28), magnification + "x");
        }

                private void DrawBattery(bool deployed, bool connected, double remaining, string status, float health, bool boost, bool targetSighted)
        {
            Rect box = new Rect(1470, 915, 425, 138);
            Fill(box, new Color(0.055f, 0.055f, 0.045f, 0.88f));
            Fill(new Rect(box.x, box.y, 4, box.height), new Color(0.65f, 0.60f, 0.43f));
            Text(new Rect(box.x + 17, box.y + 9, 265, 28),
                connected ? (boost ? "DRONE  /  FAST" : "DRONE  /  NORMAL") : "DRONE  /  BODY VIEW");
            Text(new Rect(box.x + 290, box.y + 9, 120, 28),
                deployed ? TimeSpan.FromSeconds(remaining).ToString(@"mm\:ss") : "10:00", true);
            int live = deployed ? (int)Math.Ceiling(remaining / 600 * 24) : 24;
            for (int index = 0; index < 24; index++)
            {
                Color color = index < live ? new Color(0.69f, 0.67f, 0.53f) : new Color(0.18f, 0.18f, 0.15f);
                Fill(new Rect(box.x + 19 + index * 16, box.y + 43, 7, 21), color);
            }

            GUI.color = Color.white;
            Text(new Rect(box.x + 17, box.y + 73, 120, 28), health.ToString("0") + "/100");
            Text(new Rect(box.x + 220, box.y + 73, 190, 28),
                connected ? "Connected" : deployed ? "Disconnected" : "Standby", true);
            GUI.Label(new Rect(box.x + 17, box.y + 105, 390, 28),
                connected ? (targetSighted ? "Target sighted" : "No targets in sight") : status,
                smallLabel!);
        }

        private static void Corner(float x, float y, int horizontal, int vertical)
        {
            Fill(new Rect(horizontal > 0 ? x : x - 85, y, 85, 2), Ink);
            Fill(new Rect(x, vertical > 0 ? y : y - 60, 2, 60), Ink);
        }

        private void Text(Rect area, string text, bool right = false)
        {
            GUIStyle style = right ? rightLabel! : label!;
            GUI.color = Color.black;
            GUI.Label(new Rect(area.x + 1, area.y + 1, area.width, area.height), text, style);
            GUI.color = Color.white;
            GUI.Label(area, text, style);
        }

        private static void Fill(Rect area, Color color)
        {
            GUI.color = color;
            GUI.DrawTexture(area, Texture2D.whiteTexture);
        }

        internal void Disconnected()
        {
            interference.Disconnected();
        }

        public void Dispose()
        {
            interference.Dispose();
            // GUIStyle is managed; no texture/material/native UI object is owned.
            label = null;
            rightLabel = null;
            smallLabel = null;
        }
    }
}
