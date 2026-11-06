using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.DragAndDrop;
using EFT;
using EFT.Communications;
using HarmonyLib;

namespace Killdg.MultipurposeDrone
{
    internal static class DroneKit
    {
        private static readonly HashSet<string> Busy = new();

        private static GridItemAddress? FindDestination(Item item, List<CompoundItem> containers)
        {
            foreach (var container in containers)
            {
                foreach (var grid in container.Grids)
                {
                    var address = grid.FindLocationForItem(item);
                    if (address != null)
                    {
                        return address;
                    }
                }
            }
            return null;
        }

        internal static async Task Unpack(BaseItemContextInteractions context)
        {
            if (context.Item is not CompoundItem kit || !Busy.Add(kit.Id))
            {
                return;
            }

            var controller = context._contextInteractionsSwitcher._itemController;
            bool inRaid = controller is Player.PlayerInventoryController;
            var targets = new List<CompoundItem>();
            if (controller is InventoryController inventory)
            {
                if (!inRaid && inventory.Inventory.Stash is CompoundItem stash)
                {
                    // Stash unpacking uses only its visible main grid. Never
                    // silently scatter kit contents through equipped containers.
                    targets.Add(stash);
                }
                else
                {
                    foreach (var slot in new[]
                    {
                        EquipmentSlot.Backpack,
                        EquipmentSlot.TacticalVest,
                        EquipmentSlot.Pockets
                    })
                    {
                        if (inventory.Inventory.Equipment.GetSlot(slot).ContainedItem is CompoundItem container)
                        {
                            targets.Add(container);
                        }
                    }
                }
            }
            targets = targets.Where(container => container != kit && !container.IsChildOf(kit)).ToList();
            var contents = kit.Grids.SelectMany(grid => grid.Items).ToArray();
            var undo = new List<Action>();
            try
            {
                // Preflight reserves real grid cells synchronously, then rolls the
                // local plan back before submitting native transactions. No await,
                // network request or rendering occurs while the plan is applied.
                bool fits = true;
                try
                {
                    foreach (var item in contents)
                    {
                        var destination = FindDestination(item, targets);
                        if (destination == null)
                        {
                            fits = false;
                            break;
                        }
                        var plan = ItemManipulator.Move(item, destination, controller, false);
                        if (plan.Failed)
                        {
                            fits = false;
                            break;
                        }
                        var operation = plan.Value;
                        undo.Add(() => operation.RollBack());
                    }
                }
                finally
                {
                    for (int index = undo.Count - 1; index >= 0; index--)
                    {
                        undo[index]();
                    }
                }
                if (!fits)
                {
                    NotificationManager.DisplayWarningNotification("Not enough space to unpack the drone kit.");
                    return;
                }

                DroneKitPairs.Register(contents);
                foreach (var item in contents)
                {
                    var destination = FindDestination(item, targets);
                    if (destination == null)
                    {
                        NotificationManager.DisplayWarningNotification("Inventory changed; remaining contents stay in the kit.");
                        return;
                    }
                    var operation = ItemManipulator.Move(item, destination, controller, true);
                    if (operation.Failed || (await controller.TryRunNetworkTransaction(operation)).Failed)
                    {
                        // An interrupted transaction retains every untransferred
                        // child in its kit. Never recreate already moved contents.
                        NotificationManager.DisplayWarningNotification("Unpacking interrupted; remaining items are still in the kit.");
                        return;
                    }
                }
                await controller.TryRunNetworkTransaction(ItemManipulator.Remove(kit, controller, true));
                NotificationManager.DisplayMessageNotification(inRaid
                    ? "Drone kit unpacked into backpack / rig / pockets."
                    : "Drone kit unpacked into the main stash.");
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogError("MS drone kit unpack failed: " + exception);
            }
            finally
            {
                Busy.Remove(kit.Id);
            }
        }
    }

    [HarmonyPatch(typeof(ItemContextInteractionsSwitcher), nameof(ItemContextInteractionsSwitcher.IsActive))]
    internal static class DroneKitMenu
    {
        private static void Postfix(ItemContextInteractionsSwitcher __instance,
            EItemInfoButton button, ref bool __result)
        {
            if (button == EItemInfoButton.Unpack && __instance._item.TemplateId == DroneItems.Kit)
            {
                __result = __instance.IsOwnedByPlayer && __instance.Examined;
            }
        }
    }

    [HarmonyPatch(typeof(BaseItemContextInteractions), nameof(BaseItemContextInteractions.UnpackItem))]
    internal static class DroneKitAction
    {
        private static bool Prefix(BaseItemContextInteractions __instance, ref Task __result)
        {
            if (__instance.Item.TemplateId != DroneItems.Kit)
            {
                return true;
            }
            __result = DroneKit.Unpack(__instance);
            return false;
        }
    }
    [HarmonyPatch(typeof(HandoverExtensions), nameof(HandoverExtensions.IsExchangeable))]
    internal static class DroneKitPurchase
    {
        private static void Postfix(Item item, ref bool __result)
        {
            // Native pricing rejects all non-empty containers. Only this sealed
            // trader offer is exempt; player-owned containers cannot bypass sale
            // restrictions. Contents remain real inventory children throughout.
            if (item.TemplateId == DroneItems.Kit && item.Owner?.OwnerType == EOwnerType.Trader)
            {
                __result = true;
            }
        }
    }
    [HarmonyPatch(typeof(TradingItemView), nameof(TradingItemView.SetContainerCanvasGroup))]
    internal static class DroneKitTraderAppearance
    {
        private static bool Prefix(CompoundItem item)
        {
            // Native trading dims filled containers after normal price/stock
            // presentation has already run. This kit is intentionally prepacked.
            // Skip only that final filled-container dimming for the trader's kit;
            // retain native price, availability, filters and out-of-stock state.
            return item.TemplateId != DroneItems.Kit || item.Owner?.OwnerType != EOwnerType.Trader;
        }
    }
}
