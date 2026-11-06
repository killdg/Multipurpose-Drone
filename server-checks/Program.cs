using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Json;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

var json = new JsonUtil(new[] { new SptJsonConverterRegistrator() });
var definitions = json.DeserializeFromFile<List<NewItemFromCloneDetails>>(
    System.IO.Path.Combine(args[0], "items.json"))!;
if (definitions.Count != 4 || definitions.Select(item => item.NewId).Distinct().Count() != 4)
{
    throw new Exception("Expected four unique custom item definitions.");
}
var assort = json.DeserializeFromFile<TraderAssort>(System.IO.Path.Combine(args[0], "assort.json"))!;
if (assort.Items.Count != 4 || assort.LoyalLevelItems.Values.Single() != 1
    || assort.BarterScheme.Values.Single().Single().Single().Count != 65000)
{
    throw new Exception("Mechanic kit contents, loyalty level or price are incorrect.");
}
if (assort.Items[0].Upd.BuyRestrictionMax != 50)
{
    throw new Exception("Purchase restriction must be 50 per reset.");
}
Console.WriteLine("Native server JSON deserialization, four item definitions, kit contents, LL1, price and stock checks passed.");

// Guard the startup-order regression responsible for the 0.2.0 crash.
string registrationSource = File.ReadAllText(System.IO.Path.Combine(args[0], "DroneMod.cs"));
if (!registrationSource.Contains("TypePriority = OnLoadOrder.Preload)")
    || registrationSource.Contains("OnLoadOrder.PostLoad"))
{
    throw new Exception("Custom items must register before profile loading.");
}
Console.WriteLine("Custom-item startup phase regression check passed.");
