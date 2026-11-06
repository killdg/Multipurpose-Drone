using System.Collections.Generic;
using HarmonyLib;
using Koenigz.PerfectCulling;
using Koenigz.PerfectCulling.EFT;
using UnityEngine;
using UnityEngine.AI;

namespace Killdg.MultipurposeDrone
{
    /// <summary>
    /// Native baked scene sampling has a primary observer, unlike Unity's
    /// per-camera frustum. Follow the drone with that observer while retaining
    /// nearby drone geometry. No Player is relocated.
    /// The native workers remain responsible for disk sampling and visibility.
    /// </summary>
    internal static class DroneVisibility
    {
        internal static bool Active;
        internal static Vector3 DronePosition;
        internal static Vector3 BodyPosition;
        private static Vector3 samplingPosition;
        private static float nextSamplingPosition;
        private static bool hasSamplingPosition;

        internal static Vector3 SamplingPosition
        {
            get
            {
                if (!hasSamplingPosition || Time.time >= nextSamplingPosition)
                {
                    nextSamplingPosition = Time.time + 0.2f;
                    // Airborne coordinates can be outside every baked visibility
                    // cell. Find walkable ground at the aircraft's horizontal
                    // position without moving the camera or any player actor.
                    Vector3 probe = new Vector3(DronePosition.x, BodyPosition.y + 1, DronePosition.z);
                    samplingPosition = NavMesh.SamplePosition(probe, out NavMeshHit ground, 20, NavMesh.AllAreas)
                        ? ground.position + Vector3.up * 1.6f : probe;
                    hasSamplingPosition = true;
                }
                return samplingPosition;
            }
        }

        internal static void PrepareSampling()
        {
            if (!Active)
            {
                return;
            }
            Plugin.PrepareRemoteVisibility();
            var camera = PerfectCullingCrossSceneSampler.Instance?.CullingCamera;
            if (camera != null)
            {
                camera.ObservePosition = SamplingPosition;
            }
        }
        private static readonly Dictionary<PerfectCullingBakeGroup, Bounds> bounds = new();
        private static readonly HashSet<PerfectCullingBakeGroup> retained = new();

        internal static bool Retain(PerfectCullingBakeGroup group)
        {
            if (!Active)
            {
                return false;
            }

            if (!bounds.TryGetValue(group, out Bounds box))
            {
                var result = group.GetGroupBounds();
                if (!result.Item1)
                {
                    return false;
                }

                box = result.Item2;
                bounds[group] = box;
            }

            bool keep = box.SqrDistance(DronePosition) <= 200 * 200
                || box.SqrDistance(BodyPosition) <= 30 * 30;
            if (keep)
            {
                retained.Add(group);
            }

            return keep;
        }

        internal static void Reset()
        {
            if (!Active && retained.Count == 0 && bounds.Count == 0)
            {
                return;
            }

            Active = false;
            hasSamplingPosition = false;
            nextSamplingPosition = 0;
            // Release only groups whose disable request this plugin overrode.
            // Native sampling will recompute their ordinary visibility next.
            foreach (PerfectCullingBakeGroup group in retained)
            {
                bool sceneUnloaded = false;
                foreach (Renderer renderer in group.renderers ?? System.Array.Empty<Renderer>())
                {
                    if (renderer == null)
                    {
                        sceneUnloaded = true;
                        break;
                    }
                }

                if (!sceneUnloaded)
                {
                    try
                    {
                        group.IsEnabled = false;
                    }
                    catch (MissingReferenceException)
                    {
                        // The native scene can already have destroyed a runtime
                        // proxy during raid teardown. There is nothing to restore.
                    }
                }
            }

            retained.Clear();
            bounds.Clear();
            PerfectCullingCrossSceneSampler.Instance?.CullingCamera?.SetDirty();
        }
    }

    [HarmonyPatch(typeof(PerfectCullingCamera), nameof(PerfectCullingCamera.Update))]
    internal static class DroneSceneObserver
    {
        private static void Postfix(PerfectCullingCamera __instance)
        {
            if (DroneVisibility.Active
                && PerfectCullingCrossSceneSampler.Instance?.CullingCamera == __instance)
            {
                __instance.ObservePosition = DroneVisibility.SamplingPosition;
            }
        }
    }

    [HarmonyPatch(typeof(PerfectCullingBakeGroup), nameof(PerfectCullingBakeGroup.Toggle))]
    internal static class DroneBakedVisibility
    {
        private static void Prefix(PerfectCullingBakeGroup __instance, ref bool rendererEnabled)
        {
            if (!rendererEnabled && DroneVisibility.Retain(__instance))
            {
                rendererEnabled = true;
                __instance.isGroupEnabled = true;
            }
        }
    }

    [HarmonyPatch(typeof(CullingObject), nameof(CullingObject.SetVisibility))]
    internal static class DroneDynamicVisibility
    {
        private static void Prefix(CullingObject __instance, ref bool isVisible)
        {
            // Native distance culling otherwise evaluates only the body's FPS
            // camera. This bounded exception affects nearby drone content only.
            if (!isVisible && DroneVisibility.Active
                && ((__instance.transform.position - DroneVisibility.DronePosition).sqrMagnitude < 200 * 200
                    || (__instance.transform.position - DroneVisibility.BodyPosition).sqrMagnitude < 30 * 30))
            {
                isVisible = true;
            }
        }
    }
    [HarmonyPatch(typeof(CullingManager), nameof(CullingManager.Update))]
    internal static class DroneEarlyDynamicCamera
    {
        private static void Prefix()
        {
            if (DroneVisibility.Active)
            {
                Plugin.PrepareRemoteVisibility();
            }
        }
    }

    [HarmonyPatch(typeof(PerfectCullingCamera), nameof(PerfectCullingCamera.CamPreCullOptimized))]
    internal static class DroneEarlyBakedCamera
    {
        private static void Prefix()
        {
            DroneVisibility.PrepareSampling();
        }
    }

    [HarmonyPatch(typeof(PerfectCullingCrossSceneSampler), "method_4")]
    internal static class DroneAdaptiveSceneCamera
    {
        private static void Prefix()
        {
            // In this native build method_4 updates adaptive content cells and
            // schedules their overlap/visibility jobs. Set both the render pose
            // and the ground sampling observer before it captures either value.
            DroneVisibility.PrepareSampling();
        }
    }
}
