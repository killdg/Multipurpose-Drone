using System;
using System.Linq;
using EFT.InputSystem;
using HarmonyLib;
using UnityEngine;

namespace Killdg.MultipurposeDrone
{
    [HarmonyPatch(typeof(InputManager), nameof(InputManager.UpdateBindings))]
    internal static class DroneSprintBinding
    {
        private static KeyCode[][] bindings = Array.Empty<KeyCode[]>();

        internal static bool HasKeyboardBinding => bindings.Length != 0;

        private static void Postfix(KeyGroup[] keyGroups)
        {
            var sprint = keyGroups.FirstOrDefault(group => group.keyName == EGameKey.Sprint);
            bindings = sprint?.variants
                .Where(source => !source.isAxis && source.keyCode.Count > 0)
                .Select(source => source.keyCode.ToArray()).ToArray() ?? Array.Empty<KeyCode[]>();
        }

        internal static bool Pressed()
        {
            // Read the configured chord once per frame so the game sprint
            // press mode cannot turn a release into a second drone toggle.
            return bindings.Any(keys => keys.All(Input.GetKey) && keys.Any(Input.GetKeyDown));
        }
    }
}
