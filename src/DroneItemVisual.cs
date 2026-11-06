using System;
using System.Linq;
using System.Threading.Tasks;
using EFT;
using EFT.InventoryLogic;
using EFT.Interactive;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Killdg.MultipurposeDrone
{
    /// <summary>
    /// Cosmetic replacement on the native item pool object. Its original loot
    /// colliders and pooling identity remain untouched; only renderer visibility
    /// changes. Every owned object is removed before the pool object is reused.
    /// </summary>
    internal sealed class DroneItemVisual : MonoBehaviour
    {
        private Renderer[] originals = Array.Empty<Renderer>();
        private bool[] visibility = Array.Empty<bool>();
        private GameObject? visual;
        private bool cleaned;
        internal bool IsRemote { get; private set; }

        internal void Build(bool kit, bool remote)
        {
            IsRemote = remote;
            originals = GetComponentsInChildren<Renderer>(true);
            visibility = originals.Select(renderer => renderer.forceRenderingOff).ToArray();
            foreach (var renderer in originals)
            {
                renderer.forceRenderingOff = true;
            }
            int layer = originals.Length > 0 ? originals[0].gameObject.layer : gameObject.layer;
            visual = DroneModel.Create(transform, layer, kit, 1, remote);
        }

        private void OnDisable()
        {
            Cleanup();
            Destroy(this);
        }

        private void OnDestroy()
        {
            Cleanup();
        }

        private void Cleanup()
        {
            if (cleaned)
            {
                return;
            }
            cleaned = true;
            for (int index = 0; index < originals.Length; index++)
            {
                if (originals[index] != null)
                {
                    originals[index].forceRenderingOff = visibility[index];
                }
            }
            if (visual != null)
            {
                Destroy(visual);
            }

        }
    }

    [HarmonyPatch(typeof(LootItem), nameof(LootItem.SetupBoundingCollider))]
    internal static class DroneRemotePickupBounds
    {
        private static void Postfix(GameObject lootItemGameobject, BoxCollider resultCollider, ref Bounds __result)
        {
            if (lootItemGameobject.GetComponent<DroneItemVisual>()?.IsRemote != true)
            {
                return;
            }

            // The visible controller is built in the loot root's local space.
            // Include its antenna and joystick with a little aiming tolerance.
            // This is the game's own pickup trigger, separate from the colliders
            // used to settle the item on the ground.
            resultCollider.center = new Vector3(0f, 0.008f, 0.019f);
            resultCollider.size = new Vector3(0.09f, 0.075f, 0.19f);
            __result = resultCollider.bounds;
        }
    }

    [HarmonyPatch(typeof(ObjectsFactory), nameof(ObjectsFactory.CreateItemAsync))]
    internal static class DroneItemFactoryVisual
    {
        private static void Postfix(Item item, ref Task<GameObject> __result)
        {
            bool kit = item.TemplateId == DroneItems.Kit;
            bool remote = item.TemplateId == DroneItems.Remote;
            if (kit || remote || item.TemplateId == DroneItems.Drone)
            {
                __result = Apply(__result, kit, remote);
            }
        }

        private static async Task<GameObject> Apply(Task<GameObject> nativeCreation, bool kit, bool remote)
        {
            // Preserve the native task/cancellation/pooled root, and customize only
            // after native assembly completes on its Unity synchronization context.
            GameObject root = await nativeCreation;
            if (root != null && root.GetComponent<DroneItemVisual>() == null)
            {
                root.AddComponent<DroneItemVisual>().Build(kit, remote);
            }
            return root!;
        }
    }
}
