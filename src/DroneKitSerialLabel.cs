using System.Globalization;
using EFT.UI.DragAndDrop;
using HarmonyLib;

namespace Killdg.MultipurposeDrone
{
    [HarmonyPatch(typeof(GridItemView), nameof(GridItemView.SetCountValue))]
    internal static class DroneKitSerialLabel
    {
        private static void Postfix(GridItemView __instance)
        {
            var item = __instance.Item;
            if (item == null)
            {
                return;
            }

            int serial = DroneKitPairs.SerialFor(item);
            if (serial > 0)
            {
                // Use the existing lower-right value panel so the serial
                // follows native slot layout and never changes stack size.
                __instance.SetItemValue(GridItemView.EItemValueFormat.OneValue, true, "#dadabc",
                    serial.ToString("D4", CultureInfo.InvariantCulture));
            }
        }
    }
}
