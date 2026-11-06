using System.Collections.Generic;
using UnityEngine;

namespace Killdg.MultipurposeDrone
{
    internal sealed class DroneModel : MonoBehaviour
    {
        private readonly List<Material> materials = new();
        private readonly List<Texture2D> textures = new();
        private readonly List<Mesh> meshes = new();
        private int renderLayer;

        internal static GameObject Create(Transform parent, int layer, bool kit, float scale, bool remote = false)
        {
            var root = new GameObject("MS_CustomItemModel");
            root.transform.SetParent(parent, false);
            root.transform.localScale = Vector3.one * scale;
            var builder = root.AddComponent<DroneModel>();
            builder.renderLayer = layer;
            if (remote)
            {
                builder.BuildRemote();
            }
            else if (kit)
            {
                builder.BuildKit();
            }
            else
            {
                builder.BuildAircraft();
            }
            return root;
        }

        private Material Surface(Color color, bool label = false, bool lit = false)
        {
            // An unlit texture keeps these tiny parts readable under the game's
            // different camera/lighting paths. Textures are generated locally.
            Shader shader = lit
                ? Shader.Find("Standard") ?? Shader.Find("Unlit/Texture")
                : Shader.Find("Unlit/Texture") ?? Shader.Find("Standard");
            var material = new Material(shader);
            var texture = new Texture2D(64, 32, TextureFormat.RGBA32, false);
            var pixels = new Color[64 * 32];
            for (int y = 0; y < 32; y++)
            {
                for (int x = 0; x < 64; x++)
                {
                    int grain = (x * 13 + y * 7 + x * y * 3) % 17;
                    float shade = 0.80f + grain * 0.018f;
                    if (x < 2 || y < 2 || x > 61 || y > 29)
                    {
                        shade += 0.25f;
                    }
                    Color tint = lit && material.HasProperty("_Color") ? Color.white : color;
                    pixels[y * 64 + x] = new Color(tint.r * shade, tint.g * shade, tint.b * shade, 1);
                }
            }
            if (label)
            {
                DrawBoxLabel(pixels);
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            material.mainTexture = texture;
            if (material.HasProperty("_Color"))
            {
                material.color = lit ? color : Color.white;
            }
            if (lit && material.HasProperty("_Glossiness"))
            {
                material.SetFloat("_Glossiness", 0.15f);
                material.SetFloat("_Metallic", 0f);
            }
            materials.Add(material);
            textures.Add(texture);
            return material;
        }

        private static void DrawBoxLabel(Color[] pixels)
        {
            // Five compact bitmap glyphs avoid OS fonts or external dependencies.
            string[] rows =
            {
                "1110 1110 0110 1001 1111",
                "1001 1001 1001 1101 1000",
                "1001 1110 1001 1011 1110",
                "1001 1010 1001 1001 1000",
                "1110 1001 0110 1001 1111"
            };
            for (int y = 0; y < rows.Length; y++)
            {
                for (int x = 0; x < rows[y].Length; x++)
                {
                    if (rows[y][x] == '1')
                    {
                        pixels[(21 - y * 2) * 64 + 8 + x * 2] = new Color(0.81f, 0.77f, 0.64f);
                        pixels[(20 - y * 2) * 64 + 8 + x * 2] = new Color(0.81f, 0.77f, 0.64f);
                        pixels[(21 - y * 2) * 64 + 9 + x * 2] = new Color(0.81f, 0.77f, 0.64f);
                        pixels[(20 - y * 2) * 64 + 9 + x * 2] = new Color(0.81f, 0.77f, 0.64f);
                    }
                }
            }
        }

        private Transform Part(PrimitiveType type, Vector3 position, Vector3 size, Material surface)
        {
            var part = GameObject.CreatePrimitive(type);
            part.layer = renderLayer;
            part.transform.SetParent(transform, false);
            part.transform.localPosition = position;
            part.transform.localScale = size;
            var collider = part.GetComponent<Collider>();
            collider.enabled = false;
            Destroy(collider);
            part.GetComponent<Renderer>().sharedMaterial = surface;
            return part.transform;
        }

        private void BuildAircraft()
        {
            var shell = Surface(new Color(0.21f, 0.24f, 0.20f), lit: true);
            var rubber = Surface(new Color(0.09f, 0.10f, 0.10f), lit: true);
            var metal = Surface(new Color(0.37f, 0.38f, 0.33f), lit: true);
            var light = Surface(new Color(0.18f, 0.72f, 0.59f), lit: true);
            Part(PrimitiveType.Cube, Vector3.zero, new Vector3(0.068f, 0.024f, 0.14f), shell);
            Part(PrimitiveType.Cube, new Vector3(0, 0.014f, 0), new Vector3(0.059f, 0.009f, 0.11f), metal);
            Part(PrimitiveType.Cube, new Vector3(0, -0.003f, 0.070f), new Vector3(0.030f, 0.019f, 0.021f), rubber);
            Part(PrimitiveType.Sphere, new Vector3(0, -0.003f, 0.083f), new Vector3(0.013f, 0.013f, 0.008f), metal);
            Part(PrimitiveType.Cube, new Vector3(0.024f, 0.020f, 0.043f), new Vector3(0.009f, 0.003f, 0.009f), light);
            for (int vent = 0; vent < 5; vent++)
            {
                Part(PrimitiveType.Cube, new Vector3(0, 0.019f, -0.041f + vent * 0.006f),
                    new Vector3(0.033f, 0.002f, 0.002f), rubber);
            }

            foreach (float x in new[] { -0.061f, 0.061f })
            {
                foreach (float z in new[] { -0.043f, 0.043f })
                {
                    Part(PrimitiveType.Cube, new Vector3(x * 0.6f, 0, z), new Vector3(0.052f, 0.014f, 0.023f), shell);
                    Part(PrimitiveType.Cylinder, new Vector3(x, 0, z), new Vector3(0.023f, 0.008f, 0.023f), metal);
                    Part(PrimitiveType.Cube, new Vector3(x, 0.006f, z), new Vector3(0.058f, 0.003f, 0.010f), rubber)
                        .localRotation = Quaternion.Euler(0, x * z > 0 ? 35 : -35, 0);
                    BuildRotorGuard(x, z, shell);
                }
            }
            Part(PrimitiveType.Sphere, new Vector3(0, -0.025f, 0), new Vector3(0.029f, 0.043f, 0.029f), shell);
            Part(PrimitiveType.Cylinder, new Vector3(0, -0.017f, 0), new Vector3(0.032f, 0.004f, 0.032f), rubber);
        }

        private void BuildRotorGuard(float x, float z, Material surface)
        {
            // The chamfered guard reads as a molded cage without adding any
            // physics shapes. The flight body owns the only ballistic hitbox.
            const float halfWidth = 0.038f;
            const float corner = 0.013f;
            var points = new[]
            {
                new Vector2(-halfWidth, -halfWidth + corner),
                new Vector2(-halfWidth + corner, -halfWidth),
                new Vector2(halfWidth - corner, -halfWidth),
                new Vector2(halfWidth, -halfWidth + corner),
                new Vector2(halfWidth, halfWidth - corner),
                new Vector2(halfWidth - corner, halfWidth),
                new Vector2(-halfWidth + corner, halfWidth),
                new Vector2(-halfWidth, halfWidth - corner)
            };
            for (int index = 0; index < points.Length; index++)
            {
                Vector2 start = points[index];
                Vector2 end = points[(index + 1) % points.Length];
                Vector2 midpoint = (start + end) * 0.5f;
                Vector2 edge = end - start;
                var rail = Part(PrimitiveType.Cube, new Vector3(x + midpoint.x, 0.002f, z + midpoint.y),
                    new Vector3(edge.magnitude + 0.003f, 0.015f, 0.005f), surface);
                rail.localRotation = Quaternion.Euler(0, -Mathf.Atan2(edge.y, edge.x) * Mathf.Rad2Deg, 0);
            }
        }

        private void BuildKit()
        {
            var cardboard = Surface(new Color(0.19f, 0.18f, 0.15f));
            var rim = Surface(new Color(0.37f, 0.33f, 0.26f));
            var label = Surface(new Color(0.20f, 0.19f, 0.17f), true);
            Part(PrimitiveType.Cube, new Vector3(0, -0.070f, 0), new Vector3(0.32f, 0.016f, 0.23f), cardboard);
            foreach (float side in new[] { -1f, 1f })
            {
                Part(PrimitiveType.Cube, new Vector3(side * 0.155f, 0, 0), new Vector3(0.010f, 0.15f, 0.23f), cardboard);
                Part(PrimitiveType.Cube, new Vector3(0, 0, side * 0.110f), new Vector3(0.32f, 0.15f, 0.010f), cardboard);
                Part(PrimitiveType.Cube, new Vector3(0, 0.086f, side * 0.142f), new Vector3(0.32f, 0.010f, 0.076f), rim)
                    .localRotation = Quaternion.Euler(side * 22, 0, 0);
            }
            Part(PrimitiveType.Quad, new Vector3(0, 0, -0.116f), new Vector3(0.30f, 0.14f, 1), label);
        }

        private void BuildRemote()
        {
            var shell = Surface(new Color(0.18f, 0.25f, 0.24f), lit: true);
            var rubber = Surface(new Color(0.08f, 0.085f, 0.085f), lit: true);
            var gripRib = Surface(new Color(0.12f, 0.125f, 0.12f), lit: true);
            var metal = Surface(new Color(0.36f, 0.34f, 0.28f), lit: true);
            var orange = Surface(new Color(0.66f, 0.25f, 0.065f), lit: true);
            var indicator = Surface(new Color(0.90f, 0.46f, 0.10f), lit: true);

            // Wider control head, tapered grip and clipped corners follow the
            // inventory art. The original loot body still owns physics.
            var casing = new[]
            {
                new Vector2(-0.014f, -0.062f), new Vector2(0.013f, -0.062f),
                new Vector2(0.022f, -0.053f), new Vector2(0.031f, 0.016f),
                new Vector2(0.036f, 0.046f), new Vector2(0.028f, 0.060f),
                new Vector2(-0.014f, 0.063f), new Vector2(-0.030f, 0.051f),
                new Vector2(-0.036f, 0.030f), new Vector2(-0.027f, 0.010f),
                new Vector2(-0.021f, -0.053f)
            };
            RemoteShell(casing, 0f, 0.018f, shell);
            RemoteShell(new[]
            {
                new Vector2(-0.018f, 0.002f), new Vector2(0.019f, 0.002f),
                new Vector2(0.027f, 0.015f), new Vector2(0.028f, 0.042f),
                new Vector2(0.021f, 0.052f), new Vector2(-0.012f, 0.054f),
                new Vector2(-0.024f, 0.044f), new Vector2(-0.027f, 0.022f)
            }, 0.015f, 0.005f, rubber);
            Part(PrimitiveType.Cube, new Vector3(0, 0.015f, -0.035f),
                new Vector3(0.029f, 0.008f, 0.041f), rubber);
            for (int rib = 0; rib < 4; rib++)
            {
                Part(PrimitiveType.Cube, new Vector3(0, 0.020f, -0.049f + rib * 0.008f),
                    new Vector3(0.025f, 0.002f, 0.002f), gripRib);
            }

            Part(PrimitiveType.Cylinder, new Vector3(0, 0.021f, 0.033f),
                new Vector3(0.032f, 0.003f, 0.032f), rubber);
            Part(PrimitiveType.Sphere, new Vector3(0, 0.026f, 0.033f),
                new Vector3(0.024f, 0.013f, 0.024f), gripRib);
            Part(PrimitiveType.Cylinder, new Vector3(0, 0.036f, 0.033f),
                new Vector3(0.020f, 0.004f, 0.020f), rubber);
            Part(PrimitiveType.Sphere, new Vector3(-0.010f, 0.021f, 0.010f),
                new Vector3(0.015f, 0.006f, 0.009f), orange);
            Part(PrimitiveType.Sphere, new Vector3(0.011f, 0.021f, 0.010f),
                new Vector3(0.009f, 0.006f, 0.009f), orange);
            Part(PrimitiveType.Sphere, new Vector3(0, 0.020f, 0.018f),
                new Vector3(0.005f, 0.002f, 0.002f), indicator);

            foreach (float x in new[] { -0.017f, 0.017f })
            {
                foreach (float z in new[] { 0.020f, 0.045f })
                {
                    Part(PrimitiveType.Cylinder, new Vector3(x, 0.020f, z),
                        new Vector3(0.004f, 0.001f, 0.004f), metal);
                }
            }

            Part(PrimitiveType.Cylinder, new Vector3(0.014f, 0, 0.063f),
                new Vector3(0.014f, 0.003f, 0.014f), metal).localRotation = Quaternion.Euler(90, 0, 0);
            Part(PrimitiveType.Cylinder, new Vector3(0.014f, 0, 0.080f),
                new Vector3(0.009f, 0.016f, 0.009f), rubber).localRotation = Quaternion.Euler(90, 0, 0);
            Part(PrimitiveType.Sphere, new Vector3(0.014f, 0, 0.096f),
                new Vector3(0.009f, 0.009f, 0.009f), rubber);
        }

        private void RemoteShell(Vector2[] outline, float height, float halfDepth, Material surface)
        {
            var vertices = new List<Vector3>();
            var uv = new List<Vector2>();
            var triangles = new List<int>();
            int count = outline.Length;
            for (int ring = 0; ring < 4; ring++)
            {
                bool bevel = ring == 0 || ring == 3;
                float scale = bevel ? 0.87f : 1f;
                float y = height + halfDepth * (-1f + ring * 2f / 3f);
                foreach (Vector2 point in outline)
                {
                    vertices.Add(new Vector3(point.x * scale, y, point.y * scale));
                    uv.Add(new Vector2(0.5f + point.x * 12f, 0.5f + point.y * 7f));
                }
            }

            for (int ring = 0; ring < 3; ring++)
            {
                for (int index = 0; index < count; index++)
                {
                    int first = ring * count + index;
                    int next = ring * count + (index + 1) % count;
                    triangles.AddRange(new[] { first, first + count, next, next, first + count, next + count });
                }
            }

            int top = 3 * count;
            for (int index = 1; index < count - 1; index++)
            {
                triangles.AddRange(new[] { 0, index, index + 1, top, top + index + 1, top + index });
            }

            var mesh = new Mesh { name = "Multipurpose Drone Controller Casing" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            meshes.Add(mesh);

            var part = new GameObject("Controller Casing");
            part.layer = renderLayer;
            part.transform.SetParent(transform, false);
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            part.AddComponent<MeshRenderer>().sharedMaterial = surface;
        }

        private void OnDestroy()
        {
            foreach (var mesh in meshes)
            {
                Destroy(mesh);
            }
            foreach (var material in materials)
            {
                Destroy(material);
            }
            foreach (var texture in textures)
            {
                Destroy(texture);
            }
        }
    }
}
