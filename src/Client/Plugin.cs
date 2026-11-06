using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using EFT;
using HarmonyLib;
using Newtonsoft.Json;

namespace MultipurposeDrone.LanguagePack
{
    [BepInPlugin("com.killdg.multipurposedrone.languages.client", "Multipurpose Drone Language Pack", "1.0.0")]
    [BepInDependency("com.killdg.multipurposedrone", "0.2.24")]
    public sealed class Plugin : BaseUnityPlugin
    {
        private static Catalog? catalog;
        private static string? locale;
        private Harmony? harmony;
        private Action? unsubscribe;

        private void Start()
        {
            try
            {
                string directory = Path.GetDirectoryName(GetType().Assembly.Location)!;
                catalog = new Catalog(Path.Combine(directory, "Locales"), text =>
                    JsonConvert.DeserializeObject<Dictionary<string, string>>(text)
                    ?? throw new InvalidDataException("Expected a translation dictionary."));
                RefreshLanguage();
                unsubscribe = LocalizationManager.Instance.AddLocaleUpdateListener(RefreshLanguage);
                Type hud = AccessTools.TypeByName("Killdg.MultipurposeDrone.DroneHud")
                    ?? throw new MissingMemberException("Multipurpose Drone HUD");
                harmony = new Harmony("com.killdg.multipurposedrone.languages.client");
                foreach (string method in new[] { "DrawBattery", "DrawTelemetry" })
                {
                    MethodInfo target = AccessTools.Method(hud, method)
                        ?? throw new MissingMethodException(hud.FullName, method);
                    harmony.Patch(target,
                        prefix: method == "DrawBattery"
                            ? new HarmonyMethod(typeof(Plugin), nameof(TranslateStatus))
                            : null,
                        transpiler: new HarmonyMethod(typeof(Plugin), nameof(TranslateLabels)));
                }
            }
            catch (Exception exception)
            {
                OnDestroy();
                Logger.LogWarning("Drone localization unavailable. Using English. " + exception.Message);
            }
        }

        private void OnDestroy()
        {
            harmony?.UnpatchSelf();
            unsubscribe?.Invoke();
            harmony = null;
            unsubscribe = null;
            catalog = null;
        }

        private static void RefreshLanguage()
        {
            locale = LocalizationManager.Instance.Culture;
        }

        private static string Translate(string english)
        {
            return catalog?.Text(locale, english) ?? english;
        }

        private static void TranslateStatus(ref string status)
        {
            status = Translate(status);
        }

        private static IEnumerable<CodeInstruction> TranslateLabels(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo translate = AccessTools.Method(typeof(Plugin), nameof(Translate));

            // These two methods only draw the HUD. Translate string constants
            // where they are read, without changing shared GUI methods or flight state.
            foreach (CodeInstruction instruction in instructions)
            {
                yield return instruction;
                if (instruction.opcode == OpCodes.Ldstr && instruction.operand is string label
                    && label.Length > 1 && char.IsLetter(label[0]))
                {
                    yield return new CodeInstruction(OpCodes.Call, translate);
                }
            }
        }
    }
}
