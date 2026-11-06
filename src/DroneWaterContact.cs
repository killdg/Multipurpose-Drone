using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using WaterSSR;

namespace Killdg.MultipurposeDrone
{
    internal sealed class DroneWaterContact
    {
        private readonly Collider[] overlaps = new Collider[8];
        private readonly List<MeshFilter> surfaces = new();
        private readonly Dictionary<Mesh, SurfaceMesh> geometry = new();
        private bool discovered;
        private int layerMask;

        internal bool Touches(Vector3 start, Vector3 end, float radius)
        {
            if (!discovered)
            {
                DiscoverSurfaces();
            }

            if (layerMask != 0 && Physics.OverlapCapsuleNonAlloc(start, end,
                radius, overlaps, layerMask, QueryTriggerInteraction.Collide) > 0)
            {
                return true;
            }

            // Some water is rendered without a collider. Test the actual water
            // mesh footprint so its rectangular bounds don't sink drones over land.
            foreach (MeshFilter surface in surfaces)
            {
                if (surface == null || !surface.gameObject.activeInHierarchy
                    || !surface.TryGetComponent(out MeshRenderer renderer))
                {
                    continue;
                }

                Bounds bounds = renderer.bounds;
                bounds.Expand(radius * 2f);
                if (!bounds.Contains(end) && !bounds.Contains(start))
                {
                    continue;
                }

                Mesh mesh = surface.sharedMesh;
                if (mesh == null || !mesh.isReadable)
                {
                    continue;
                }

                if (!geometry.TryGetValue(mesh, out SurfaceMesh data))
                {
                    data = new SurfaceMesh(mesh.vertices, mesh.triangles);
                    geometry.Add(mesh, data);
                }

                Vector3 bottom = end - Vector3.up * radius;
                Vector3 top = start + Vector3.up * radius;
                if (data.Intersects(surface.transform.InverseTransformPoint(top),
                    surface.transform.InverseTransformPoint(bottom)))
                {
                    return true;
                }
            }

            return false;
        }

        private void DiscoverSurfaces()
        {
            discovered = true;
            layerMask = LayerMask.GetMask("Water");
            foreach (WaterForSSR water in Object.FindObjectsOfType<WaterForSSR>())
            {
                AddPlanes(water.WaterPlanes);
            }

            foreach (WaterForSSRv2 water in Object.FindObjectsOfType<WaterForSSRv2>())
            {
                AddPlanes(water.WaterPlanes);
            }

            // The third native renderer stores its configured surface meshes in
            // a private serialized field. Missing fields simply skip this fallback.
            var field = AccessTools.Field(typeof(WaterForSSRv3), "_targets");
            foreach (WaterForSSRv3 water in Object.FindObjectsOfType<WaterForSSRv3>())
            {
                if (field?.GetValue(water) is WaterObject[] targets)
                {
                    foreach (WaterObject target in targets)
                    {
                        if (target.Filter != null)
                        {
                            surfaces.Add(target.Filter);
                        }
                    }
                }
            }
        }

        private void AddPlanes(MeshFilter[]? planes)
        {
            if (planes != null)
            {
                surfaces.AddRange(planes);
            }
        }

        internal void Reset()
        {
            System.Array.Clear(overlaps, 0, overlaps.Length);
            surfaces.Clear();
            geometry.Clear();
            discovered = false;
        }

        private readonly struct SurfaceMesh
        {
            private readonly Vector3[] vertices;
            private readonly int[] triangles;

            internal SurfaceMesh(Vector3[] vertices, int[] triangles)
            {
                this.vertices = vertices;
                this.triangles = triangles;
            }

            internal bool Intersects(Vector3 start, Vector3 end)
            {
                Vector3 delta = end - start;
                for (int index = 0; index < triangles.Length; index += 3)
                {
                    Vector3 origin = vertices[triangles[index]];
                    Vector3 edgeA = vertices[triangles[index + 1]] - origin;
                    Vector3 edgeB = vertices[triangles[index + 2]] - origin;
                    Vector3 cross = Vector3.Cross(delta, edgeB);
                    float determinant = Vector3.Dot(edgeA, cross);
                    if (Mathf.Abs(determinant) < 0.000001f)
                    {
                        continue;
                    }

                    Vector3 offset = start - origin;
                    float u = Vector3.Dot(offset, cross) / determinant;
                    Vector3 perpendicular = Vector3.Cross(offset, edgeA);
                    float v = Vector3.Dot(delta, perpendicular) / determinant;
                    float fraction = Vector3.Dot(edgeB, perpendicular) / determinant;
                    if (u >= 0 && v >= 0 && u + v <= 1 && fraction >= 0 && fraction <= 1)
                    {
                        return true;
                    }
                }

                return false;
            }
        }
    }
}
