using System.Reflection;
using Path = System.IO.Path;
using Killdg.MultipurposeDrone;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Modding.Custom;
using SPTarkov.Server.Core.Utils;

namespace Killdg.MultipurposeDrone.Server
{
    public record ModMetadata : IModMetadata
    {
        public string ModGuid { get; init; } = "com.killdg.multipurposedrone.server";
        public string Name { get; init; } = "Multipurpose Drone";
        public string Author { get; init; } = "killdg";
        public List<string>? Contributors { get; init; }
        public SemanticVersioning.Version Version { get; init; } = new("0.2.26");
        public SemanticVersioning.Range SptVersion { get; init; } = new("~4.1.6");
        public List<string>? Incompatibilities { get; init; }
        public Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
        public string? Url { get; init; }
        public bool HasPrepatcher { get; init; }
        public string License { get; init; } = "MIT";
    }

    [Injectable(TypePriority = OnLoadOrder.Preload)]
    public sealed class DroneMod(
        CustomItemService customItems,
        TradersTable traders,
        JsonUtil json,
        ISptLogger<DroneMod> logger) : IOnLoad
    {
        public Task OnLoadAsync(CancellationToken cancellationToken)
        {
            // SPT freezes custom-item registration when SaveServer loads profiles.
            // This hook must run at PreLoad, before that boundary; PostLoad is
            // too late even though trader-only mods can safely use that phase.
            // Read only shipped definitions; no profile files are edited.
            string directory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
            var definitions = json.DeserializeFromFile<List<NewItemFromCloneDetails>>(
                Path.Combine(directory, "items.json"))!;
            foreach (var definition in definitions)
            {
                var result = customItems.CreateItemFromClone(definition);
                if (!result.Success)
                {
                    throw new InvalidOperationException("Drone item registration failed: "
                        + string.Join("; ", result.Errors));
                }
            }

            TraderAssort assort = traders[new MongoId("5a7c2eca46aef81a7ca2145d")].Assort;
            // The kit contains real child items. Normal purchase and move operations
            // preserve these IDs, so unpacking works locally in raid as well as stash.
            var offer = json.DeserializeFromFile<TraderAssort>(
                Path.Combine(directory, "assort.json"))!;
            assort.Items.AddRange(offer.Items);
            foreach (var entry in offer.BarterScheme)
            {
                assort.BarterScheme[entry.Key] = entry.Value;
            }
            foreach (var entry in offer.LoyalLevelItems)
            {
                assort.LoyalLevelItems[entry.Key] = entry.Value;
            }
            logger.Info("Multipurpose Drone: Mechanic LL1 kit, 65000 RUB, 50 per reset. No custom network routes.");
            return Task.CompletedTask;
        }
    }
}
