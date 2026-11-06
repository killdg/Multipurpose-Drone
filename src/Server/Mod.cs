using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Services.Locales;

namespace MultipurposeDrone.LanguagePack
{
    public record Metadata : IModMetadata
    {
        public string ModGuid { get; init; } = "com.killdg.multipurposedrone.languages";
        public string Name { get; init; } = "Multipurpose Drone Language Pack";
        public string Author { get; init; } = "killdg";
        public List<string>? Contributors { get; init; }
        public SemanticVersioning.Version Version { get; init; } = new("1.0.0");
        public SemanticVersioning.Range SptVersion { get; init; } = new("~4.1.6");
        public List<string>? Incompatibilities { get; init; }
        public Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; } = new()
        {
            ["com.killdg.multipurposedrone.server"] = new(">=0.2.24 <0.3.0")
        };
        public string? Url { get; init; }
        public bool HasPrepatcher { get; init; }
        public string License { get; init; } = "MIT";
    }

    [Injectable(TypePriority = OnLoadOrder.PostLoad + 20)]
    public sealed class Mod(ISptLogger<Mod> logger) : IOnLoad
    {
        internal static Catalog? Translations { get; private set; }

        public Task OnLoadAsync(CancellationToken cancellationToken)
        {
            var harmony = new Harmony("com.killdg.multipurposedrone.languages.server");
            try
            {
                string directory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
                Translations = new Catalog(Path.Combine(directory, "Locales"), text =>
                    JsonSerializer.Deserialize<Dictionary<string, string>>(text)
                    ?? throw new InvalidDataException("Expected a translation dictionary."));
                harmony.PatchAll();
            }
            catch (Exception exception)
            {
                harmony.UnpatchSelf();
                Translations = null;
                logger.Warning("Drone localization unavailable. Using English. " + exception.Message);
            }

            return Task.CompletedTask;
        }
    }

    [HarmonyPatch(typeof(LocaleService), nameof(LocaleService.GetLocaleDb))]
    internal static class DroneItemDescriptions
    {
        private static readonly string[] ItemIds =
        {
            "672f41440000000000000001",
            "672f41440000000000000002",
            "672f41440000000000000003",
            "672f41440000000000000004"
        };

        private static void Postfix(LocaleService __instance, string? language, Dictionary<string, string> __result)
        {
            string locale = language ?? __instance.GetDesiredGameLocale();
            // These are the four native locale keys for the drone mod's items.
            // Only their descriptions are overlaid in SPT's in-memory locale.
            lock (__result)
            {
                foreach (string id in ItemIds)
                {
                    string key = id + " Description";
                    string? translation = Mod.Translations?.Find(locale, key);
                    if (translation != null)
                    {
                        __result[key] = translation;
                    }
                }
            }
        }
    }
}
