using System;
using System.IO;
using System.Linq;
using Killdg.MultipurposeDrone;
using Mono.Cecil;

internal static class Program
{
    private static int checks;

    private static void Require(bool condition, string label)
    {
        if (!condition)
        {
            throw new InvalidOperationException(label);
        }

        checks++;
    }

    private static void Main(string[] arguments)
    {
        FlightBattery battery = new FlightBattery();
        Require(DroneSignal.Bars(200) == 4 && DroneSignal.Bars(201) == 3, "Full signal boundary");
        Require(DroneSignal.Bars(300) == 3 && DroneSignal.Bars(301) == 2, "Medium signal boundary");
        Require(DroneSignal.Bars(350) == 2 && DroneSignal.Bars(351) == 1, "Weak signal boundary");
        Require(DroneSignal.Bars(400) == 1 && DroneSignal.Bars(401) == 0, "Fringe signal boundary");
        Require(DroneSignal.BurstInterval(399) < DroneSignal.BurstInterval(100), "Weak signal bursts recur faster");
        Require(DroneSignal.DropChance(400) == 0 && DroneSignal.DropChance(401) > 0
            && DroneSignal.DropChance(450) == 1, "Radio drop boundaries");
        Require(battery.Remaining(100) == 600, "Unused battery");
        battery.Launch(100);
        Require(battery.Remaining(100) == 600, "Full battery at launch");
        Require(battery.Remaining(400) == 300, "Battery drains without a camera connection");
        Require(battery.Remaining(700) == 0, "Ten-minute expiration");
        Require(battery.Remaining(1000) == 0, "Expiration clamps to zero");
        bool duplicateRejected = false;
        try
        {
            battery.Launch(800);
        }
        catch (InvalidOperationException)
        {
            duplicateRejected = true;
        }

        Require(duplicateRejected, "Disconnect cannot create another battery");

        Require(DroneTestPolicy.NeedsFreshAircraft(true, true, true, false, 500),
            "F9 replaces an impact-destroyed or shot-down test drone");
        Require(DroneTestPolicy.NeedsFreshAircraft(true, false, true, true, 0),
            "F9 replaces an expired test aircraft");
        Require(!DroneTestPolicy.NeedsFreshAircraft(false, true, true, false, 500),
            "Bound remote cannot grant a free replacement");
        Require(!DroneTestPolicy.NeedsFreshAircraft(true, false, true, true, 500),
            "F9 toggles an intact aircraft without resetting its battery");
        Require(!DroneTestPolicy.NeedsFreshAircraft(true, false, false, false, 600),
            "Initial test launch needs no prior reset");

        DroneHealth health = new DroneHealth();
        Require(health.Remaining == 100 && !health.Destroyed, "Aircraft starts at 100 HP");
        Require(!health.ApplyDamage(-10) && health.Remaining == 100, "Negative damage cannot heal");
        Require(!health.ApplyDamage(float.NaN) && health.Remaining == 100, "Invalid damage ignored");
        Require(!health.ApplyDamage(34) && health.Remaining == 66, "Native bullet damage accumulates");
        Require(health.ApplyDamage(70) && health.Remaining == 0, "Lethal damage clamps to zero");
        Require(!health.ApplyDamage(30) && health.Remaining == 0, "Destruction fires only once");


        // Inspect local native metadata without executing game code. These
        // checks detect removed/overloaded Harmony targets before packaging.
        using ModuleDefinition module = ModuleDefinition.ReadModule(
            Path.Combine(arguments[0], "EscapeFromTarkov_Data", "Managed", "Assembly-CSharp.dll"));
        foreach (string specification in new[]
        {
            "EFT.EftGamePlayerOwner|TranslateCommand",
            "EFT.PlayerInputTranslator|TranslateCommand",
            "EFT.MoveInputTranslator|TranslateAxes",
            "EFT.MoveInputTranslator|TranslateCommand",
            "EFT.BotOwner|UpdateManual",
            "Koenigz.PerfectCulling.PerfectCullingCamera|Update",
            "Koenigz.PerfectCulling.PerfectCullingBakeGroup|Toggle",
            "CullingObject|SetVisibility",
            "EFT.UI.ItemContextInteractionsSwitcher|IsActive",
            "EFT.UI.BaseItemContextInteractions|UnpackItem",
            "ItemIconCreator|GetItemIcon",
            "EFT.UI.DragAndDrop.TradingItemView|SetContainerCanvasGroup",
            "EFT.ObjectsFactory|CreateItemAsync",
            "CullingManager|Update",
            "Koenigz.PerfectCulling.PerfectCullingCamera|CamPreCullOptimized",
            "Koenigz.PerfectCulling.EFT.PerfectCullingCrossSceneSampler|method_4"
        })
        {
            string[] parts = specification.Split('|');
            TypeDefinition type = module.GetType(parts[0]);
            Require(type != null, parts[0]);
            Require(type.Methods.Count(method => method.Name == parts[1]) == 1, specification);
        }

        TypeDefinition axesOwner = module.GetType("EFT.MoveInputTranslator");
        MethodDefinition axes = axesOwner.Methods.Single(method => method.Name == "TranslateAxes");
        Require(axes.Parameters[0].ParameterType.FullName == "System.Single[]&", "Native axes signature");
        Require(axesOwner.Fields.Any(field => field.Name == "_controller"), "Native movement owner");
        Require(module.GetType("EFT.PlayerInputTranslator").Fields.Any(field => field.Name == "_player"),
            "Native hands owner");
        TypeDefinition ballistic = module.GetType("EFT.Ballistics.BallisticCollider");
        Require(ballistic.Events.Any(entry => entry.Name == "OnHitAction"), "Native damage callback");
        MethodDefinition visibility = module.GetType("CullingObject").Methods.Single(
            method => method.Name == "SetVisibility");
        Require(visibility.Parameters[0].Name == "isVisible", "Harmony visibility argument binding");

        if (arguments.Length > 1)
        {
            // Regression checks inspect the finished plugin IL. The black-feed
            // failure came from manually rendering the native effects into an
            // incompatible secondary target; that entire path must stay absent.
            using ModuleDefinition plugin = ModuleDefinition.ReadModule(arguments[1]);
            var calls = plugin.Types.SelectMany(type => type.Methods)
                .Where(method => method.HasBody)
                .SelectMany(method => method.Body.Instructions)
                .Select(instruction => instruction.Operand).OfType<MethodReference>().ToArray();
            Require(!calls.Any(method => method.DeclaringType.FullName == "UnityEngine.Camera"
                && method.Name == "Render"), "No manual FPS camera render");
            Require(!calls.Any(method => method.DeclaringType.FullName == "EFT.Player"
                && method.Name.Contains("Teleport")), "Operator is never teleported");
            Require(calls.Any(method => method.DeclaringType.FullName == "EFT.Player"
                && method.Name == "ProceedPortableRangeFinder"), "Native controller equip transaction");
        }

        MethodDefinition explosion = module.GetType("EFT.Grenade").Methods.Single(
            method => method.Name == "Explosion" && method.IsStatic);
        Require(explosion.Parameters.Any(parameter => parameter.Name == "playerProfileIDWhoThrew"),
            "Native explosion supports operator attribution");
        MethodDefinition damage = module.GetType("EFT.Grenade").Methods.Single(
            method => method.Name == "GetDamageInfo");
        Require(damage.Body.Instructions.Any(instruction => instruction.Operand is MethodReference method
            && method.DeclaringType.FullName == "EFT.Ballistics.Shot" && method.Name == "Create"),
            "Grenade damage uses native attributed shot construction");

        Console.WriteLine(checks + " battery/health/native API checks passed; live gameplay remains unverified.");
    }
}
