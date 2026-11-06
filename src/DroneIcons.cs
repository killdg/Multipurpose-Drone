using System.Collections.Generic;
using System.IO;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;

namespace Killdg.MultipurposeDrone
{
    [HarmonyPatch(typeof(ItemIconCreator), nameof(ItemIconCreator.GetItemIcon))]
    internal static class DroneIcons
    {
        private static readonly Dictionary<string, Texture2D> Sources = new();
        private static readonly Dictionary<string, Sprite> Sprites = new();

        private static bool Prefix(Item item, in IntVec2 size, ref ItemIcon __result)
        {
            string? file = item.TemplateId.ToString() switch
            {
                DroneItems.Kit => "kit.png",
                DroneItems.Drone => "drone.png",
                DroneItems.Glasses => "glasses.png",
                DroneItems.Remote => "remote.png",
                _ => null
            };
            if (file == null)
            {
                return true;
            }
            if (!Sources.TryGetValue(file, out var source))
            {
                string path = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location)!, "assets", file);
                if (!File.Exists(path))
                {
                    return true;
                }
                source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(source, File.ReadAllBytes(path)))
                {
                    Object.Destroy(source);
                    return true;
                }
                Sources[file] = source;
            }
            int width = Mathf.Clamp(size.X, 1, 1024);
            int height = Mathf.Clamp(size.Y, 1, 1024);
            string key = file + ":" + width + ":" + height;
            if (!Sprites.TryGetValue(key, out var sprite))
            {
                // Native inventory atlases copy exactly the requested dimensions.
                // Returning the 1024px source directly overflows their small cells
                // and makes the trading preview adopt the wrong native size.
                var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                var pixels = new Color[width * height];
                float scale = Mathf.Min((float)width / source.width, (float)height / source.height);
                float imageWidth = source.width * scale;
                float imageHeight = source.height * scale;
                float left = (width - imageWidth) / 2;
                float bottom = (height - imageHeight) / 2;
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        float u = (x + 0.5f - left) / imageWidth;
                        float v = (y + 0.5f - bottom) / imageHeight;
                        pixels[y * width + x] = u >= 0 && u <= 1 && v >= 0 && v <= 1
                            ? source.GetPixelBilinear(u, v) : Color.clear;
                    }
                }
                texture.SetPixels(pixels);
                texture.Apply(false, false);
                sprite = Sprite.Create(texture, new Rect(0, 0, width, height), Vector2.one * 0.5f);
                Sprites[key] = sprite;
            }
            __result = new ItemIcon(IconsHash.GetItemHash(item)) { Sprite = sprite };
            return false;
        }

        internal static void Dispose()
        {
            foreach (var sprite in Sprites.Values)
            {
                Object.Destroy(sprite.texture);
                Object.Destroy(sprite);
            }
            foreach (var source in Sources.Values)
            {
                Object.Destroy(source);
            }
            Sprites.Clear();
            Sources.Clear();
        }
    }
}
