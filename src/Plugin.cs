using System.Collections.Generic;

using System;

using System.Linq;

using BepInEx;

using BepInEx.Configuration;

using Comfort.Common;

using EFT;

using EFT.CameraControl;

using EFT.InputSystem;

using EFT.InventoryLogic;

using EFT.Ballistics;

using EFT.Interactive;

using Systems.Effects;

using HarmonyLib;

using UnityEngine;



namespace Killdg.MultipurposeDrone

{

    public enum DisplayMode

    {

        Sunglasses,

        HelmetVisor

    }



    [BepInPlugin("com.killdg.multipurposedrone", "Multipurpose Drone", "0.2.26")]

    public sealed class Plugin : BaseUnityPlugin

    {

        internal static Plugin? Instance;

        private ConfigEntry<bool> prototypeEnabled = null!;

        private ConfigEntry<bool> testDeployment = null!;

        private ConfigEntry<DisplayMode> viewing = null!;

        private ConfigEntry<float> speed = null!;

        private ConfigEntry<float> maximumRange = null!;

        private ConfigEntry<bool> limitRadioRange = null!;

        private ConfigEntry<KeyboardShortcut> activation = null!;

        private ConfigEntry<KeyboardShortcut> disconnect = null!;

        private Harmony? harmony;

        private GameWorld? world;

        private Player? player;

        private GameObject? drone;

        private Transform? droneView;

        private DroneCameraView? cameraView;

        private bool preparingController;
        private bool restoringHands;

        private DroneKitPair? flightPair;

        private Item? previousHandsItem;

        private PortableRangeFinder? flightController;

        private int controllerRequest;

        private DroneHud? hud;

        private Renderer[] droneRenderers = Array.Empty<Renderer>();

        private int physicalMask;

        private float flightSpeed;

        private Vector3 velocity;

        private Vector3 holdPosition;

        private bool hadMovement;

        private bool boost;

        private bool testAircraft;

        private ConfigEntry<bool> fisheye = null!;

        private ConfigEntry<int> droneSoundVolume = null!;

        private DroneAudio? droneAudio;

        private DroneTracking tracking = new();

        private Player? diveTarget;

        private int zoomMagnification;
        private bool ZoomOpen => zoomMagnification != 0;
        private Func<bool>? configurationWindowOpen;
        private bool IsConfigurationOpen => configurationWindowOpen?.Invoke() == true;
        private enum VisionMode { Normal, NightVision, Thermal }
        private VisionMode visionMode;
        private ConfigEntry<KeyboardShortcut> nightVisionShortcut = null!;
        private ConfigEntry<KeyboardShortcut> markTargetShortcut = null!;
        private ConfigEntry<bool> autoTrackTargets = null!;

        private Coroutine? zoomCoroutine;

        private float nextContactDamage;

        private ThrowWeap? reactionPayload;

        private float reactionAt;

        private bool reactionKill;

        private float groundHeight = -1;

        private float nextGroundSample;

        private float nextCollisionLog;

        private readonly Collider[] overlaps = new Collider[64];

        private FlightBattery battery = new();

        private DroneHealth health = new();

        private BallisticCollider? hitTarget;

        private DroneCombat? combat;

        private bool destructionPending;

        private bool connected;
        private float nextRadioRoll;
        private bool radioLost;
        private readonly DroneWorldAudio worldAudio = new();
        private readonly DroneWaterContact waterContact = new();

        private bool raidActive;

        private bool waitForRaisedVisor;

        private bool previousVisor;

        private float yaw;

        private float pitch;

        private Vector2 movement;

        private float inputAt;

        private readonly RaycastHit[] hits = new RaycastHit[64];

        private string status = "Put a rangefinder in a special slot and bind it to 4–0.";

        private const float DroneRadius = 0.32f;
        private const float LaunchClearance = DroneRadius + 0.05f;
        private const float FlightClearance = 1.25f;
        private bool takingOff;



        internal static bool Piloting(Player? candidate)

        {

            return Instance is { connected: true, IsConfigurationOpen: false } pilot
                && candidate != null && candidate == pilot.player;

        }



        internal static void PrepareRemoteVisibility()

        {

            if (Instance is { connected: true, IsConfigurationOpen: false } pilot && pilot.player?.IsInventoryOpened == false)

            {

                Instance.cameraView?.RefreshBeforeVisibility();

            }

        }



        private void Start()
        {
            // Configuration Manager is optional. Cache its public visibility
            // getter once so both its cursor and its input remain available.
            if (BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue(
                "com.bepis.bepinex.configurationmanager", out var manager)
                && manager.Instance != null
                && manager.Instance.GetType().GetProperty("DisplayingWindow")?.GetMethod is { } getter)
            {
                configurationWindowOpen = Delegate.CreateDelegate(
                    typeof(Func<bool>), manager.Instance, getter, false) as Func<bool>;
            }
        }

        private void Awake()

        {

            Instance = this;

            Player.OnPlayerDeadStatic += OnDroneVictim;

            DroneKitPairs.RegistryPath = System.IO.Path.ChangeExtension(Config.ConfigFilePath, "kit-pairs.txt");

            prototypeEnabled = Config.Bind("General", "Enable prototype", true, "Solo drone test; one ten-minute battery per raid.");

            droneSoundVolume = Config.Bind("Audio", "Drone sound effects volume", 25,
                new ConfigDescription(
                    "Volume of the drone's rotor and landing sounds. Set to 0 to mute. Applies during flight.",
                    new AcceptableValueRange<int>(0, 100)));

            testDeployment = Config.Bind("Testing", "Allow F9 test deployment", true,

                "Beta testing: F9 launches or reconnects a test aircraft without using any kit items.");

            fisheye = Config.Bind("Camera", "Fisheye", false,

                "Optional barrel distortion on the drone feed. Off uses the native lens and colors.");

            viewing = Config.Bind("General", "Viewing equipment", DisplayMode.Sunglasses,

                "Sunglasses: equip MS Glasses; use MS-Remote to toggle the feed. "

                + "HelmetVisor: lower a toggleable helmet visor to connect and raise it to disconnect.");

            speed = Config.Bind("Flight", "Flight speed metres per second", 6f,

                new ConfigDescription("WASD flight; Q down, E up.",

                    new AcceptableValueRange<float>(1, 12)));

            maximumRange = Config.Bind("Flight", "Maximum range metres", 300f,

                new ConfigDescription("Distance from the body. Distant native scene rendering still needs testing.",

                    new AcceptableValueRange<float>(50, 500)));

            limitRadioRange = Config.Bind("Flight", "Limit radio range", false,

                "Optional distance limit from the body. Off permits movement throughout the loaded map; "

                + "native map boundaries and baked visibility still apply.");

            activation = Config.Bind("Shortcuts", "Fallback controller key", new KeyboardShortcut(KeyCode.F9),

                "Launch/toggle if a rangefinder is in a special slot. Its bound 4–0 key is preferred.");

            nightVisionShortcut = Config.Bind("Shortcuts", "Drone night vision", new KeyboardShortcut(KeyCode.F6),
                "Toggle the drone's native night-vision feed while piloting.");

            markTargetShortcut = Config.Bind("Shortcuts", "Mark drone target", new KeyboardShortcut(KeyCode.M),
                "Mark the tracked target under the drone reticle for your MS Glasses.");

            autoTrackTargets = Config.Bind("Tracking", "Auto target tracking", false,
                "Show all targets visible to the hovering drone in your PMC view while wearing MS Glasses.");

            disconnect = Config.Bind("Shortcuts", "Emergency disconnect", new KeyboardShortcut(KeyCode.F10),

                "Return to the body immediately. Drone hovers; battery continues draining.");

            harmony = new Harmony("com.killdg.multipurposedrone");

            hud = new DroneHud();

            cameraView = new DroneCameraView();

            // Query physical map geometry only. DefaultRaycastLayers also includes

            // engine helper volumes that can behave like invisible flight walls.

            physicalMask = LayerMask.GetMask("Terrain", "HighPolyCollider", "LowPolyCollider",

                "DoorLowPolyCollider", "TransparentCollider");

            if (physicalMask == 0)

            {

                Logger.LogError("Native physical layers are missing; drone deployment is disabled on this build.");

            }

            harmony.PatchAll();

            Logger.LogInfo("Standalone MS drone beta. Uses native SPT item/trade operations; no custom network routes or other gameplay mod dependencies.");

        }



        private System.Collections.IEnumerator CaptureInset()

        {

            var frameEnd = new WaitForEndOfFrame();

            while (connected && ZoomOpen)

            {

                yield return frameEnd;

                if (connected && ZoomOpen && !IsConfigurationOpen && player?.IsInventoryOpened == false)

                {

                    cameraView?.CaptureZoomFrame();

                }

            }

            zoomCoroutine = null;

        }



        private void Update()

        {

            GameWorld? current = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;

            if (current is HideoutGameWorld)

            {

                current = null;

            }



            if (world != current)

            {

                ResetFlight();

                world = current;

                reactionPayload = null;

            }



            player = current?.MainPlayer;

            AbstractGame? game = Singleton<AbstractGame>.Instantiated ? Singleton<AbstractGame>.Instance : null;

            raidActive = game?.Status == GameStatus.Started;

            if (!prototypeEnabled.Value || player == null || player.IsAI || player.HealthController?.IsAlive != true

                || game?.Status != GameStatus.Started)

            {

                if (drone != null || connected)

                {

                    ResetFlight();

                }



                return;

            }



            if (reactionPayload != null && Time.time >= reactionAt)

            {

                reactionPayload = null;

                player.Say(reactionKill ? EPhraseTrigger.GoodWork : EPhraseTrigger.BadWork, demand: true);

            }



            if (disconnect.Value.IsDown() || connected && Input.GetKeyDown(KeyCode.Escape))

            {

                Disconnect("Disconnected; drone hovering.");

                waitForRaisedVisor = true;

            }



            bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            bool replaceTestAircraft = testDeployment.Value && shiftHeld
                && Input.GetKeyDown(activation.Value.MainKey);
            if (!IsConfigurationOpen && !player.IsInventoryOpened
                && (replaceTestAircraft || activation.Value.IsDown()))

            {

                Activate(testDeployment.Value, replaceTestAircraft: replaceTestAircraft);

                if (drone == null)

                {

                    EFT.Communications.NotificationManager.DisplayWarningNotification(status);

                }

            }



            if (drone == null)

            {

                return;

            }



            if (destructionPending)

            {

                EndLostFlight("Drone destroyed. Signal lost.");

                return;

            }



            droneAudio?.Update(battery.Remaining(Time.time) <= 0, droneSoundVolume.Value / 100f);

            if (battery.Remaining(Time.time) <= 0)

            {

                EndLostFlight("Battery depleted. Signal lost.");

                return;

            }



            if (!testAircraft && !HasController())

            {

                Disconnect("Controller removed from the special slot; drone hovering.");

                return;

            }



            bool visor = VisorDown();

            if (!testAircraft && viewing.Value == DisplayMode.HelmetVisor)

            {

                if (!visor)

                {

                    waitForRaisedVisor = false;

                    if (connected)

                    {

                        Disconnect("Visor raised; drone hovering.");

                    }

                }

                else if (!previousVisor && !waitForRaisedVisor)

                {

                    Connect();

                }

            }

            else if (connected && !testAircraft && !HasEyewear())

            {

                Disconnect("VR glasses removed; drone hovering.");

            }



            previousVisor = visor;

            float radioDistance = Vector3.Distance(drone.transform.position, player.Position);
            if (connected && (radioDistance >= DroneSignal.MaximumConnectionDistance
                || (Time.unscaledTime >= nextRadioRoll && RollRadioLoss(radioDistance))))
            {
                radioLost = true;
                Disconnect("Signal lost. Move closer and reconnect.");
            }

            worldAudio.SetActive(connected && !IsConfigurationOpen && !player.IsInventoryOpened);
            DroneVisibility.Active = connected && !IsConfigurationOpen && !player.IsInventoryOpened;

            if (DroneVisibility.Active)

            {

                DroneVisibility.DronePosition = drone.transform.position;

                DroneVisibility.BodyPosition = player.Position;

            }



            if (connected && !IsConfigurationOpen && !player.IsInventoryOpened)

            {

                Fly();

            }



            if (drone != null && Time.time >= nextGroundSample)

            {

                nextGroundSample = Time.time + 0.2f;

                groundHeight = Physics.Raycast(drone.transform.position, Vector3.down,

                    out RaycastHit floor, 1000, physicalMask, QueryTriggerInteraction.Ignore)

                    ? floor.distance : -1;

            }

        }



        private bool HasEyewear()

        {

            Item? eyewear = player?.Equipment.GetSlot(EquipmentSlot.Eyewear).ContainedItem;

            return eyewear != null && (flightPair != null

                ? eyewear.Id.ToString() == flightPair.Glasses

                : eyewear.TemplateId == DroneItems.Glasses || testDeployment.Value);

        }



        private bool VisorDown()

        {

            FaceShieldComponent? visor = player?.FaceShieldObserver?.Component;

            return visor?.Togglable != null && visor.Togglable.On;

        }



        private bool IsController(Item? item)

        {

            // Native special slots belong to the pockets container. Inspect

            // only its three slots, not the whole inventory or scene loot.

            return item is PortableRangeFinder && (item.TemplateId == DroneItems.Remote || testDeployment.Value) && player != null

                && player.Equipment.GetSlot(EquipmentSlot.Pockets).ContainedItem is CompoundItem pockets

                && pockets.Slots.Any(slot => slot.ID.StartsWith("SpecialSlot", StringComparison.Ordinal)

                    && slot.ContainedItem == item);

        }



        private bool HasController()

        {

            if (flightPair != null)

            {

                return IsController(flightController);

            }



            return player?.Equipment.GetSlot(EquipmentSlot.Pockets).ContainedItem is CompoundItem pockets

                && pockets.Slots.Any(slot => slot.ID.StartsWith("SpecialSlot", StringComparison.Ordinal)

                    && slot.ContainedItem is PortableRangeFinder && (slot.ContainedItem.TemplateId == DroneItems.Remote || testDeployment.Value));

        }



        internal static bool HandleController(Player candidate, ECommand command)

        {

            Plugin? pilot = Instance;

            if (pilot == null || !pilot.prototypeEnabled.Value || !pilot.raidActive

                || candidate == null || candidate != pilot.player || pilot.IsConfigurationOpen || candidate.IsInventoryOpened)

            {

                return false;

            }



            EBoundItem? bound = command switch

            {

                ECommand.SelectFastSlot4 => EBoundItem.Item4,

                ECommand.SelectFastSlot5 => EBoundItem.Item5,

                ECommand.SelectFastSlot6 => EBoundItem.Item6,

                ECommand.SelectFastSlot7 => EBoundItem.Item7,

                ECommand.SelectFastSlot8 => EBoundItem.Item8,

                ECommand.SelectFastSlot9 => EBoundItem.Item9,

                ECommand.SelectFastSlot0 => EBoundItem.Item10,

                _ => null

            };

            if (!bound.HasValue

                || !pilot.IsController(candidate.InventoryController.Inventory.FastAccess.GetBoundItem(bound.Value)))

            {

                return false;

            }



            // Consume only this special item's input before native equipment

            // transactions begin. No hands callback or item operation is left

            // incomplete. Unrelated quick slots retain their normal behavior.

            pilot.Activate(requestedController: candidate.InventoryController.Inventory.FastAccess

                .GetBoundItem(bound.Value) as PortableRangeFinder);

            return true;

        }



        private void Activate(bool testing = false, PortableRangeFinder? requestedController = null,
            bool replaceTestAircraft = false)

        {

            // Input can arrive before Update processes the loss. Finalize the

            // current kit first so deploying again cannot skip its native drops.

            if (drone != null && (destructionPending || battery.Remaining(Time.time) <= 0))

            {

                EndLostFlight(destructionPending ? "Drone destroyed. Signal lost." : "Battery depleted. Signal lost.");

                if (!testing)

                {

                    return;

                }

            }



            if (testing && replaceTestAircraft && drone != null)
            {
                if (!testAircraft)
                {
                    EFT.Communications.NotificationManager.DisplayWarningNotification(
                        "An inventory drone is still deployed.");
                    return;
                }

                ResetFlight();
            }

            // F9 supplies synthetic aircraft without consuming a purchased kit.

            if (DroneTestPolicy.NeedsFreshAircraft(testing, health.Destroyed,

                battery.Launched, drone != null, battery.Remaining(Time.time)))

            {

                ResetFlight();

                Logger.LogInfo("F9 test reset: fresh aircraft, 100 HP and ten-minute battery; inventory unchanged.");

            }

            if (player == null || (!testing && !testAircraft && !HasController()))

            {

                status = "Put MS-Remote in a special slot first.";

                return;

            }



            if (drone == null || health.Destroyed || battery.Remaining(Time.time) <= 0)

            {

                try

                {

                    Launch(testing, requestedController);

                }

                catch (Exception exception)

                {

                    // Camera creation can fail on a different engine build.

                    // Release only our objects and leave the native camera intact.

                    ResetFlight();

                    status = "Drone camera could not be created. See the local BepInEx log.";

                    Logger.LogError(exception);

                    return;

                }

            }



            if (drone == null || battery.Remaining(Time.time) <= 0)

            {

                return;

            }



            if (requestedController != null && flightPair != null && requestedController != flightController)

            {

                status = "That remote belongs to another aircraft.";

                return;

            }



            if (requestedController != null || testAircraft || viewing.Value == DisplayMode.Sunglasses)

            {

                if (connected)

                {

                    waitForRaisedVisor = true;
                    Disconnect("Disconnected; drone hovering.");

                }

                else

                {

                    Connect();

                }

            }

            else

            {

                status = "Drone airborne. Raise, then lower your helmet visor to connect.";

            }

        }



        private void Launch(bool testing, PortableRangeFinder? requestedController)

        {

            Camera? source = CameraManager.Exist ? CameraManager.Instance.Camera : Camera.main;

            if (source == null || player == null || physicalMask == 0)

            {

                status = "No native player camera available yet.";

                return;

            }



            // Search the existing world loot registry only when explicitly deploying.

            // Inventory ownership has already transferred through native Discard.

            // Synthetic F9 tests never consume a nearby purchased drone.

            var kitPairs = testing ? null : DroneKitPairs.Read();

            var deployedItem = testing ? null : world!.LootList.OfType<LootItem>()

                .Where(item => item.Item.TemplateId == DroneItems.Drone)

                .Where(item => Vector3.Distance(item.transform.position, player.Position) < 4)

                .OrderBy(item => Vector3.Distance(item.transform.position, player.Position))

                .FirstOrDefault(item => requestedController != null

                    && kitPairs!.TryGetValue(item.Item.Id, out var candidatePair)

                    && candidatePair.Remote == requestedController.Id.ToString());

            if (deployedItem == null && !testing)

            {

                status = "Drop this remote's paired drone on clear ground within four metres first.";

                return;

            }

            DroneKitPair? pair = deployedItem != null ? kitPairs![deployedItem.Item.Id] : null;

            Item? glasses = player.Equipment.GetSlot(EquipmentSlot.Eyewear).ContainedItem;

            if (!testing && (pair == null || requestedController?.Id.ToString() != pair.Remote

                || glasses?.Id.ToString() != pair.Glasses))

            {

                status = "Use the remote and glasses from this drone's kit. Older gear needs a newly unpacked kit.";

                return;

            }



            Vector3 launchForward = Quaternion.Euler(0, source.transform.eulerAngles.y, 0)
                * Vector3.forward;
            Vector3 footprint = deployedItem != null
                ? deployedItem.transform.position
                : player.Position + launchForward * 2f;
            if (!TryFindLaunchPosition(footprint, source.transform.position, out Vector3 startPosition))
            {
                status = "Deploy over clear ground nearby with room for the drone.";
                return;
            }
            // A purchased replacement is permitted only after finding and

            // validating its own dropped item. An intact aircraft still toggles

            // the existing feed and never consumes a spare drone.

            if (battery.Launched || health.Destroyed)

            {

                ResetFlight();

            }

            takingOff = true;
            testAircraft = testing;

            flightPair = pair;

            flightController = requestedController;

            drone = new GameObject("Killdg_MS_AR4144");

            drone.transform.position = startPosition;

            drone.layer = LayerMask.NameToLayer("HighPolyCollider");

            BoxCollider hitBox = drone.AddComponent<BoxCollider>();

            hitBox.size = new Vector3(0.7f, 0.2f, 0.7f);

            Rigidbody body = drone.AddComponent<Rigidbody>();

            body.isKinematic = true;

            body.useGravity = false;

            hitTarget = drone.AddComponent<BallisticCollider>();

            hitTarget.TypeOfMaterial = MaterialType.MetalThin;

            hitTarget.SurfaceSound = BaseBallistic.ESurfaceSound.MetalThin;

            hitTarget.PenetrationChance = 0;

            hitTarget.RicochetChance = 0;

            hitTarget.OnHitAction += OnDroneHit;

            yaw = source.transform.eulerAngles.y;

            pitch = 0;

            CreateModel();

            droneAudio = new DroneAudio(drone, droneSoundVolume.Value / 100f);

            holdPosition = drone.transform.position;

            // Consume the dropped aircraft only after construction succeeds. The

            // native loot lifecycle removes interaction/owner registry entries.

            if (deployedItem != null)

            {

                world!.DestroyLoot(deployedItem);

            }

            else

            {

                Logger.LogInfo("F9 test deployment: no inventory aircraft consumed; impacts still use native RGN damage.");

            }

            droneRenderers = drone.GetComponentsInChildren<Renderer>();

            GameObject cameraObject = new("DroneCamera");

            cameraObject.transform.SetParent(drone.transform, false);

            droneView = cameraObject.transform;

            droneView.rotation = Quaternion.Euler(pitch, yaw, 0);

            tracking.Clear();
            battery.Launch(Time.time);

            combat = new DroneCombat(drone.transform, physicalMask);

            DroneCombat.Current = combat;

            previousVisor = VisorDown();

            waitForRaisedVisor = previousVisor;

            status = "Drone airborne; battery running.";

            Logger.LogInfo("Drone launched. Player health, AI and raid clock continue normally.");

        }



        private bool TryFindLaunchPosition(Vector3 footprint, Vector3 cameraPosition, out Vector3 position)
        {
            position = default;
            // Probe only the local floor. A ledge or a gap must not silently
            // place the aircraft on a different storey.
            int floorCount = Physics.RaycastNonAlloc(footprint + Vector3.up * 0.6f,
                Vector3.down, hits, 1.8f, physicalMask, QueryTriggerInteraction.Ignore);
            float nearestFloor = float.PositiveInfinity;
            for (int index = 0; index < floorCount; index++)
            {
                RaycastHit hit = hits[index];
                if (hit.collider != null && hit.normal.y > 0.55f && hit.distance < nearestFloor
                    && hit.collider.GetComponentInParent<Player>() == null)
                {
                    nearestFloor = hit.distance;
                    position = hit.point + Vector3.up * LaunchClearance;
                }
            }

            if (float.IsPositiveInfinity(nearestFloor)
                || Physics.CheckSphere(position, DroneRadius, physicalMask, QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            Vector3 path = position - cameraPosition;
            int obstructionCount = Physics.SphereCastNonAlloc(cameraPosition, 0.2f,
                path.normalized, hits, path.magnitude, physicalMask, QueryTriggerInteraction.Ignore);
            for (int index = 0; index < obstructionCount; index++)
            {
                Collider obstacle = hits[index].collider;
                if (obstacle != null && obstacle.GetComponentInParent<Player>() != player)
                {
                    return false;
                }
            }

            return true;
        }
        private void CreateModel()

        {

            Camera? camera = CameraManager.Exist ? CameraManager.Instance.Camera : Camera.main;

            int layer = drone!.layer;

            foreach (string name in new[] { "Loot", "Interactive", "Default" })

            {

                int candidate = LayerMask.NameToLayer(name);

                if (candidate >= 0 && (camera == null || (camera.cullingMask & (1 << candidate)) != 0))

                {

                    layer = candidate;

                    break;

                }

            }

            DroneModel.Create(drone.transform, layer, false, 3.5f);

        }



        private bool RollRadioLoss(float distance)
        {
            nextRadioRoll = Time.unscaledTime + 1f;
            return distance > DroneSignal.DropRiskDistance
                && UnityEngine.Random.value < DroneSignal.DropChance(distance);
        }

        private void Connect()

        {

            if (droneView == null || player == null || preparingController || restoringHands || health.Destroyed)

            {

                return;

            }



            float distance = Vector3.Distance(droneView.position, player.Position);
            if (distance >= DroneSignal.MaximumConnectionDistance
                || (radioLost && distance > DroneSignal.DropRiskDistance))
            {
                status = "Move within 400 m to reconnect.";
                return;
            }

            if (!testAircraft && viewing.Value == DisplayMode.Sunglasses && !HasEyewear())

            {

                status = "Equip MS Glasses to connect.";

                return;

            }



            if (!testAircraft && viewing.Value == DisplayMode.HelmetVisor && !VisorDown())

            {

                return;

            }



            player.Move(Vector2.zero);

            player.EnableSprint(false);

            if (player.HandsController is Player.FirearmController firearm)

            {

                firearm.SetTriggerPressed(false);

            }



            if (testAircraft)

            {

                FinishConnection();

                return;

            }



            if (player.Equipment.GetSlot(EquipmentSlot.Pockets).ContainedItem is not CompoundItem pockets)

            {

                return;

            }



            PortableRangeFinder? controller = flightController ?? pockets.Slots

                .Where(slot => slot.ID.StartsWith("SpecialSlot", StringComparison.Ordinal))

                .Select(slot => slot.ContainedItem).OfType<PortableRangeFinder>()

                .FirstOrDefault(item => item.TemplateId == DroneItems.Remote || testDeployment.Value);

            if (controller == null)

            {

                return;

            }



            if (player.HandsController?.Item == controller)

            {

                FinishConnection();

                return;

            }



            flightController = controller;

            previousHandsItem = player.HandsController?.Item;

            preparingController = true;

            int request = ++controllerRequest;

            Player operatorPlayer = player;
            Item? priorItem = previousHandsItem;

            status = "Equipping drone controller...";

            // Let the native hands transaction and its callback finish normally.

            // Never clone an item, suppress the callback, or mark hands ready early.

            operatorPlayer.ProceedPortableRangeFinder(controller, result =>

            {

                if (request != controllerRequest || operatorPlayer != player)

                {

                    if (operatorPlayer == player && !connected && !preparingController

                        && operatorPlayer.HealthController?.IsAlive == true

                        && operatorPlayer.HandsController?.Item == controller)

                    {

                        RestoreBodyHands(operatorPlayer, controller, priorItem);

                    }

                    return;

                }



                preparingController = false;

                bool equipped = result.Succeed && operatorPlayer.HandsController?.Item == controller;

                bool canView = viewing.Value == DisplayMode.Sunglasses ? HasEyewear() : VisorDown();

                if (equipped && canView && droneView != null && !health.Destroyed)

                {

                    FinishConnection();

                }

                else

                {

                    RestoreBodyHands(operatorPlayer, controller, priorItem);
                    status = "Controller not ready. Return to body view and try again.";

                    Logger.LogWarning("Drone controller equip did not complete: " + result.Error);

                }

            });

        }



        private void FinishConnection()

        {

            connected = true;
            radioLost = false;

            SetModelVisible(false);

            status = "Connected";

        }



        private void Disconnect(string reason, bool restoreHands = true)

        {

            connected = false;
            worldAudio.SetActive(false);

            diveTarget = null;

            zoomMagnification = 0;
            visionMode = VisionMode.Normal;

            preparingController = false;

            controllerRequest++;

            cameraView?.Release();

            hud?.Disconnected();

            DroneVisibility.Reset();

            flightSpeed = 0;

            velocity = Vector3.zero;

            if (drone != null)

            {

                holdPosition = drone.transform.position;

            }

            SetModelVisible(true);

            movement = Vector2.zero;

            boost = false;

            status = reason;

            if (restoreHands && player != null && flightController != null)
            {
                RestoreBodyHands(player, flightController, previousHandsItem);
            }
        }

        private void RestoreBodyHands(Player operatorPlayer, Item controller, Item? previousItem)
        {
            if (restoringHands || operatorPlayer.HealthController?.IsAlive != true
                || operatorPlayer.HandsController?.Item != controller)
            {
                return;
            }

            // The rangefinder leaves a gripping pose until the native hands
            // controller switches items. Keep the kit equipped for reconnect.
            restoringHands = true;
            if (previousItem != null && previousItem != controller
                && operatorPlayer.IsItemCanBeEquipped(previousItem))
            {
                operatorPlayer.SetInHands(previousItem, _ =>
                {
                    if (operatorPlayer == player && operatorPlayer.HandsController?.Item == controller)
                    {
                        EquipFallback();
                    }
                    else
                    {
                        restoringHands = false;
                    }
                });
            }
            else
            {
                EquipFallback();
            }

            void EquipFallback()
            {
                operatorPlayer.SetFirstAvailableItem(_ => restoringHands = false);
            }
        }

        private void EndLostFlight(string reason)

        {

            Player? operatorPlayer = player;

            bool returnedFromFeed = connected;

            DroneKitPair? pair = flightPair;

            Item? controller = flightController;

            Item? priorItem = previousHandsItem;

            ReleaseDrone(restoreHands: false);

            destructionPending = false;

            status = reason;

            Logger.LogInfo(reason);

            if (operatorPlayer == null || operatorPlayer.HealthController?.IsAlive != true)

            {

                return;

            }



            if (returnedFromFeed && reactionPayload == null)

            {

                operatorPlayer.Say(EPhraseTrigger.BadWork, demand: true);

            }



            if (controller != null && operatorPlayer.HandsController?.Item == controller)

            {

                // Let native hands finish switching before discarding the held

                // controller. Never remove renderers or manufacture replacement loot.

                if (priorItem != null && priorItem != controller && operatorPlayer.IsItemCanBeEquipped(priorItem))

                {

                    operatorPlayer.SetInHands(priorItem, _ => DropPairedGear(operatorPlayer, pair));

                }

                else

                {

                    operatorPlayer.SetFirstAvailableItem(_ => DropPairedGear(operatorPlayer, pair));

                }

            }

            else

            {

                DropPairedGear(operatorPlayer, pair);

            }

        }



        private void DropPairedGear(Player operatorPlayer, DroneKitPair? pair)

        {

            if (pair == null || operatorPlayer != player || operatorPlayer.HealthController?.IsAlive != true)

            {

                return;

            }



            var inventory = operatorPlayer.InventoryController;

            // Search only the operator's carried equipment. Discard is a native

            // inventory transaction; gear already moved away is left alone.

            var gear = operatorPlayer.Equipment.GetAllItemsFromCollection()

                .Where(item => item.Id.ToString() == pair.Remote || item.Id.ToString() == pair.Glasses)

                .ToArray();

            DropNext(0);



            void DropNext(int index)

            {

                if (index >= gear.Length || operatorPlayer != player

                    || operatorPlayer.HealthController?.IsAlive != true)

                {

                    return;

                }



                Item item = gear[index];

                if (!operatorPlayer.Equipment.GetAllItemsFromCollection().Contains(item))

                {

                    DropNext(index + 1);

                    return;

                }



                // Wait for each native operation to finish before starting the

                // next so their inventory and fast-slot changes cannot overlap.

                inventory.ThrowItem(item, downDirection: true, result =>

                {

                    if (!result.Succeed)

                    {

                        Logger.LogWarning("Could not discard paired drone gear: " + result.Error);

                    }

                    DropNext(index + 1);

                });

            }

        }



        private void OnDroneHit(DamageInfo damage)

        {

            // Native bullet damage is applied only to this aircraft. Never send

            // it to the operator's health controller or synthesize successful hits.

            if (health.ApplyDamage(damage.Damage))

            {

                // Teardown occurs next Update, outside the active bullet callback.

                destructionPending = true;

            }

        }



        private void LateUpdate()

        {

            if (connected && !IsConfigurationOpen && player != null && !player.IsInventoryOpened && droneView != null)

            {

                cameraView?.Apply(droneView, player, fisheye.Value, ZoomOpen, visionMode == VisionMode.NightVision,
                    visionMode == VisionMode.Thermal);

                if (world != null && drone != null && CameraManager.Exist)

                {

                    Camera camera = CameraManager.Instance.Camera;
                    bool markPressed = cameraView?.IsNightVisionActive != true && markTargetShortcut.Value.IsDown();
                    tracking.Sample(world, camera, drone.transform, physicalMask, refresh: markPressed);
                    if (markPressed)
                    {
                        status = tracking.MarkTarget(camera) ? "Target marked." : "No tracked target under reticle.";
                    }

                }

            }

            else

            {

                cameraView?.Release();

                if (!connected && world != null && drone != null && droneView != null && CanShowRelayedTargets())
                {
                    tracking.SampleRelay(world, droneView, drone.transform, physicalMask, autoTrackTargets.Value);
                }

            }

        }

        private bool CanShowRelayedTargets()
        {
            if (player == null || droneView == null || drone == null || !raidActive
                || !prototypeEnabled.Value || !battery.Launched || health.Destroyed
                || IsConfigurationOpen || player.IsInventoryOpened)
            {
                return false;
            }

            Item? eyewear = player.Equipment.GetSlot(EquipmentSlot.Eyewear).ContainedItem;
            if (eyewear == null || eyewear.TemplateId != DroneItems.Glasses || !HasEyewear())
            {
                return false;
            }

            float distance = Vector3.Distance(droneView.position, player.Position);
            return distance < DroneSignal.MaximumConnectionDistance
                && (!radioLost || distance <= DroneSignal.DropRiskDistance);
        }



        private void SetModelVisible(bool visible)

        {

            foreach (Renderer renderer in droneRenderers)

            {

                if (renderer != null)

                {

                    renderer.enabled = visible;

                }

            }

        }



        internal static void CaptureAxes(float[] axes)

        {

            Plugin? pilot = Instance;

            if (pilot == null || axes.Length < 4)

            {

                return;

            }



            pilot.movement = new Vector2(axes[0], axes[1]);

            pilot.yaw += axes[2];

            pilot.pitch = Mathf.Clamp(pilot.pitch - axes[3], -80, 80);

            pilot.inputAt = Time.time;

        }



        private void Fly()

        {

            if (drone == null || droneView == null || player == null)

            {

                return;

            }



            if (Input.mouseScrollDelta.y != 0f)
            {
                visionMode = visionMode switch
                {
                    VisionMode.Normal => VisionMode.NightVision,
                    VisionMode.NightVision => VisionMode.Thermal,
                    _ => VisionMode.Normal
                };
            }
            else if (nightVisionShortcut.Value.IsDown())
            {
                visionMode = visionMode == VisionMode.NightVision ? VisionMode.Normal : VisionMode.NightVision;
            }

            if (Input.GetMouseButtonDown(1))

            {

                bool hideZoom = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                zoomMagnification = hideZoom ? 0 : zoomMagnification switch
                {
                    10 => 20,
                    20 => 30,
                    30 => 0,
                    _ => 10
                };

                if (ZoomOpen && zoomCoroutine == null)

                {

                    zoomCoroutine = StartCoroutine(CaptureInset());

                }

            }



            if (DroneSprintBinding.Pressed())

            {

                boost = !boost;

            }



            bool diveClick = Input.GetMouseButtonDown(0);

            if (diveClick && diveTarget != null)

            {

                AbortDive("Dive cancelled. Manual flight restored.");

            }

            else if (diveClick && CameraManager.Exist && world != null)

            {

                // Native camera scripts can restore the body pose before Update.

                // Apply the drone pose before projecting and validating targets.

                cameraView?.Apply(droneView, player, fisheye.Value, ZoomOpen, visionMode == VisionMode.NightVision,
                    visionMode == VisionMode.Thermal);

                Camera camera = CameraManager.Instance.Camera;

                tracking.Sample(world, camera, drone.transform, physicalMask, refresh: true);

                diveTarget = tracking.SelectTarget(camera);

                if (diveTarget != null)

                {

                    status = "Diving at target. F10 aborts.";

                }

                else

                {

                    status = "No targets in sight.";

                }

            }



            if (diveTarget != null)

            {

                FlyDive(diveTarget);

                return;

            }



            droneView.rotation = Quaternion.Euler(pitch, yaw, 0);

            Quaternion heading = Quaternion.Euler(0, yaw, 0);

            float vertical = (Input.GetKey(KeyCode.E) ? 1 : 0) - (Input.GetKey(KeyCode.Q) ? 1 : 0);

            Vector2 input = Time.time - inputAt < 0.15f ? movement : Vector2.zero;

            Vector3 direction = Vector3.ClampMagnitude(

                heading * new Vector3(input.x, vertical, input.y), 1);

            float dt = Mathf.Min(Time.deltaTime, 0.05f);

            bool moving = direction.sqrMagnitude > 0.001f;

            if (moving)

            {

                holdPosition = drone.transform.position;

            }

            else if (hadMovement)

            {

                // Hold the release point: inertia carries past it, then the

                // damped controller gently corrects its small stopping overshoot.

                holdPosition = drone.transform.position;

            }

            hadMovement = moving;

            Vector3 hover = new Vector3(Mathf.Sin(Time.time * 0.71f) * 0.06f,

                Mathf.Sin(Time.time * 1.4f) * 0.09f, Mathf.Cos(Time.time * 0.83f) * 0.06f);

            Vector3 targetVelocity = moving ? direction * speed.Value * (boost ? 3f : 1f)

                : Vector3.ClampMagnitude((holdPosition + hover - drone.transform.position) * 2, 1.5f);

            velocity = Vector3.Lerp(velocity, targetVelocity, 1 - Mathf.Exp(-dt * (moving ? 3 : 4)));

            Vector3 delta = velocity * dt;



            Vector3 original = drone.transform.position;

            // Resolve each axis separately so touching a wall or floor does not

            // prevent the drone from sliding along it or climbing away from it.

            MoveSafely(new Vector3(0, delta.y, 0));

            MoveSafely(new Vector3(delta.x, 0, 0));

            MoveSafely(new Vector3(0, 0, delta.z));

            flightSpeed = (drone.transform.position - original).magnitude / Mathf.Max(Time.deltaTime, 0.001f);

            Physics.SyncTransforms();

            CheckPayloadCollision(original);

        }



        private void FlyDive(Player target)

        {

            if (drone == null || droneView == null || target.HealthController?.IsAlive != true)

            {

                AbortDive("Target lost; manual flight restored.");

                return;

            }



            Vector3 start = drone.transform.position;

            Vector3 chest = target.Position + Vector3.up;

            Vector3 offset = chest - start;

            if (offset.sqrMagnitude > 0.0001f)

            {

                droneView.rotation = Quaternion.LookRotation(offset);

                Vector3 angles = droneView.rotation.eulerAngles;

                yaw = angles.y;

                pitch = Mathf.DeltaAngle(0, angles.x);

            }



            float dt = Mathf.Min(Time.deltaTime, 0.05f);

            Vector3 step = Vector3.ClampMagnitude(offset, speed.Value * 4f * dt);

            // Use the same hull sweep and range limits as manual flight. Only

            // the manual camera clearance is relaxed for a chest-height dive.

            MoveSafely(step, diving: true);

            Physics.SyncTransforms();

            Vector3 travelled = drone.transform.position - start;

            flightSpeed = travelled.magnitude / Mathf.Max(Time.deltaTime, 0.001f);

            CheckPayloadCollision(start, target);

            if (!destructionPending && (offset.sqrMagnitude < 0.0001f

                || (step.sqrMagnitude > 0.000001f && travelled.sqrMagnitude < step.sqrMagnitude * 0.25f)))

            {

                AbortDive("Dive blocked; manual flight restored.");

            }

        }



        private void AbortDive(string reason)

        {

            diveTarget = null;

            velocity = Vector3.zero;

            if (drone != null)

            {

                holdPosition = drone.transform.position;

            }

            status = reason;

        }



        internal static void CaptureFlightCommand(ECommand command)

        {

            if (command == ECommand.ToggleSprinting && !DroneSprintBinding.HasKeyboardBinding

                && Instance is { connected: true, IsConfigurationOpen: false } pilot && pilot.player?.IsInventoryOpened == false)

            {

                pilot.boost = !pilot.boost;

            }

        }

        internal static bool TryDisconnectWithRemote(ECommand command)
        {
            return Instance is { connected: true, player: { } operatorPlayer }
                && HandleController(operatorPlayer, command);
        }



        private void OnDroneVictim(Player victim, IPlayer aggressor, DamageInfo damage, EBodyPart bodyPart)

        {

            // The synthetic grenade instance is unique to this impact.

            // Require both native attacker credit and that exact weapon.

            if (reactionPayload != null && ReferenceEquals(damage.Weapon, reactionPayload)

                && aggressor?.ProfileId == player?.ProfileId)

            {

                reactionKill = true;

            }

        }



        private void CheckPayloadCollision(Vector3 previous, Player? intendedTarget = null)

        {

            if (drone == null || world == null || player == null || destructionPending)

            {

                return;

            }

            Vector3 end = drone.transform.position;

            Vector3 segment = end - previous;

            foreach (var candidate in world.RegisteredPlayers)

            {

                if (candidate is not Player actor || !actor.IsAI || actor.HealthController?.IsAlive != true

                    || (intendedTarget != null && actor != intendedTarget))

                {

                    continue;

                }

                Vector3 chest = actor.Position + Vector3.up;

                float along = segment.sqrMagnitude < 0.00001f ? 0

                    : Mathf.Clamp01(Vector3.Dot(chest - previous, segment) / segment.sqrMagnitude);

                Vector3 closest = previous + segment * along;

                if ((chest - closest).sqrMagnitude > 0.8f * 0.8f

                    || !tracking.HasClearSight(closest, chest, actor, drone.transform, physicalMask))

                {

                    continue;

                }

                DetonatePayload(end, true);
                break;

            }

        }



        private void DetonatePayload(Vector3 end, bool directHit)
        {
            if (world == null || player == null || destructionPending)
            {
                return;
            }

            // RGN is the real damage source; native explosion/fragment damage

            // carries the operator ProfileId. No kill counters are fabricated.

            var payload = (ThrowWeap)Singleton<ItemFactory>.Instance.CreateItem(

                Guid.NewGuid().ToString("N").Substring(0, 24), "617fd91e5539a84ec44ce155", null);

            health.ApplyDamage(health.Remaining);

            destructionPending = true;

            if (connected && directHit)

            {

                reactionPayload = payload;

                reactionKill = false;

                reactionAt = Time.time + 0.6f;

            }



            Grenade.Explosion(null!, payload, end, player.ProfileId,

                world.SharedBallisticsCalculator, payload, Vector3.zero);

            Singleton<Effects>.Instance.EmitGrenade(payload.ExplosionEffectType, end, Vector3.up);

            Logger.LogInfo(directHit ? "Drone target impact." : "Drone dive hit an obstacle.");
        }

        private void MoveSafely(Vector3 delta, bool diving = false)

        {

            if (drone == null || player == null || delta.sqrMagnitude < 0.000001f)

            {

                return;

            }



            float distance = delta.magnitude;

            int count = Physics.SphereCastNonAlloc(drone.transform.position, DroneRadius, delta / distance,

                hits, distance + 0.04f, physicalMask, QueryTriggerInteraction.Ignore);

            float allowed = distance;
            float obstacleDistance = float.PositiveInfinity;
            float actorDistance = float.PositiveInfinity;

            for (int index = 0; index < count; index++)

            {

                Collider obstacle = hits[index].collider;

                if (obstacle != null && obstacle.GetComponentInParent<Player>() != player

                    && !obstacle.transform.IsChildOf(drone.transform))

                {

                    allowed = Math.Min(allowed, Mathf.Max(0, hits[index].distance - 0.05f));
                    if (obstacle.GetComponentInParent<Player>() == null)
                    {
                        obstacleDistance = Mathf.Min(obstacleDistance, hits[index].distance);
                    }
                    else
                    {
                        actorDistance = Mathf.Min(actorDistance, hits[index].distance);
                    }

                    if (hits[index].distance <= distance && obstacle.GetComponentInParent<Player>() == null

                        && Time.time >= nextContactDamage)

                    {

                        // One scrape per cooldown, even when all three manual

                        // movement sweeps touch the same wall in one frame.

                        nextContactDamage = Time.time + 1f;

                        if (health.ApplyDamage(2f))

                        {

                            destructionPending = true;

                        }

                    }

                    if (Time.time >= nextCollisionLog)

                    {

                        // Local, throttled diagnostics identify real blockers;

                        // they contain collider names/layers, never player data.

                        nextCollisionLog = Time.time + 5;

                        Logger.LogInfo("Drone blocked by " + obstacle.name + " on "

                            + LayerMask.LayerToName(obstacle.gameObject.layer));

                    }

                }

            }



            Vector3 start = drone.transform.position;
            Vector3 destination = start + delta.normalized * allowed;
            if (waterContact.Touches(start, destination, DroneRadius))
            {
                health.ApplyDamage(health.Remaining);
                destructionPending = true;
                Logger.LogInfo("Drone lost to water contact.");
                return;
            }

            if (diving && obstacleDistance <= distance && obstacleDistance < actorDistance)
            {
                // A blocked dive uses the native explosion, but always counts
                // as a miss for the voice response, even if splash kills.
                DetonatePayload(destination, false);
                return;
            }

            // Keep the lens well above local walkable surfaces. Physical floor

            // meshes can sit below the visible road surface, so hull radius

            // alone is insufficient clearance for a remote camera.

            Vector3 floorProbe = new Vector3(destination.x,

                Mathf.Max(drone.transform.position.y, destination.y) + 0.5f, destination.z);

            int floorCount = Physics.RaycastNonAlloc(floorProbe, Vector3.down, hits, 3f,

                physicalMask, QueryTriggerInteraction.Ignore);

            for (int index = 0; index < floorCount; index++)

            {

                Collider surface = hits[index].collider;

                if (surface != null && hits[index].normal.y > 0.55f

                    && surface.GetComponentInParent<Player>() != player

                    && !surface.transform.IsChildOf(drone.transform))

                {

                    float clearance = diving || takingOff ? LaunchClearance : FlightClearance;
                    destination.y = Mathf.Max(destination.y, hits[index].point.y + clearance);

                }

            }



            // Sphere casts do not report an initial overlap reliably. Reject

            // an occupied endpoint as well, including floor contact.

            int overlapCount = Physics.OverlapSphereNonAlloc(destination, DroneRadius, overlaps,

                physicalMask, QueryTriggerInteraction.Ignore);

            if (overlapCount == overlaps.Length)

            {

                return;

            }



            for (int index = 0; index < overlapCount; index++)

            {

                if (overlaps[index] != null && overlaps[index].GetComponentInParent<Player>() != player

                    && !overlaps[index].transform.IsChildOf(drone.transform))

                {

                    return;

                }

            }



            if (!limitRadioRange.Value

                || (destination - player.Position).sqrMagnitude <= maximumRange.Value * maximumRange.Value)

            {

                drone.transform.position = destination;
                // Keep launch clearance until the pilot has climbed to the
                // normal flight floor. This avoids a snap upward on first input.
                if (takingOff && !Physics.Raycast(destination, Vector3.down, out _,
                    FlightClearance, physicalMask, QueryTriggerInteraction.Ignore))
                {
                    takingOff = false;
                }

            }

            else

            {

                status = "Radio range limit: " + maximumRange.Value.ToString("0") + " m from the body.";

            }

        }



        private void OnGUI()

        {

            if (player == null || world == null || !prototypeEnabled.Value || !raidActive

                || IsConfigurationOpen || player.IsInventoryOpened || !battery.Launched || drone == null || health.Destroyed)

            {

                return;

            }



            if (connected && CameraManager.Exist)

            {

                if (cameraView?.IsNightVisionActive != true)
                {
                    tracking.Draw(CameraManager.Instance.Camera);
                }

            }

            if (!connected && CameraManager.Exist && CanShowRelayedTargets())
            {
                tracking.Draw(CameraManager.Instance.Camera, relay: true, autoTrack: autoTrackTargets.Value);
            }

            hud?.Draw(connected,

                drone != null, battery.Remaining(Time.time), yaw, pitch, flightSpeed,

                groundHeight, drone != null ? Vector3.Distance(drone.transform.position, player.Position) : 0,

                status, health.Remaining, boost, zoomMagnification, cameraView?.ZoomImage,
                connected && CameraManager.Exist && tracking.HasVisibleTarget(CameraManager.Instance.Camera));

        }



        private void ResetFlight()

        {

            ReleaseDrone();

            battery = new FlightBattery();

            health = new DroneHealth();

            destructionPending = false;

        }



        private void ReleaseDrone(bool restoreHands = true)

        {
            radioLost = false;
            waterContact.Reset();

            Disconnect("Put a rangefinder in a special slot and bind it to 4–0.", restoreHands);

            nextContactDamage = 0;

            testAircraft = false;

            flightPair = null;

            flightController = null;

            previousHandsItem = null;

            combat?.Dispose();

            combat = null;

            if (hitTarget != null)

            {

                hitTarget.OnHitAction -= OnDroneHit;

                hitTarget = null;

            }

            if (drone != null)

            {

                Destroy(drone);

            }



            droneAudio?.Dispose();

            droneAudio = null;

            velocity = Vector3.zero;

            boost = false;

            tracking = new DroneTracking();

            droneView = null;

            drone = null;

            droneRenderers = Array.Empty<Renderer>();

            flightSpeed = 0;

            groundHeight = -1;

            waitForRaisedVisor = false;

            previousVisor = false;

        }



        private void OnDestroy()

        {

            Player.OnPlayerDeadStatic -= OnDroneVictim;

            ResetFlight();

            hud?.Dispose();

            DroneIcons.Dispose();

            cameraView?.Dispose();

            harmony?.UnpatchSelf();

            Instance = null;

        }

    }



    [HarmonyPatch(typeof(EftGamePlayerOwner), nameof(EftGamePlayerOwner.TranslateCommand))]

    internal static class DroneControllerKey

    {

        private static bool Prefix(EftGamePlayerOwner __instance, ECommand command,

            ref InputNode.ETranslateResult __result)

        {

            if (!Plugin.HandleController(__instance.Player, command))

            {

                return true;

            }



            __result = InputNode.ETranslateResult.BlockAll;

            return false;

        }

    }



    [HarmonyPatch(typeof(InputManager), nameof(InputManager.DispatchInput))]

    internal static class DroneFlightCommands

    {

        private static void Prefix(List<ECommand> commandsList)

        {

            // The input manager resolves the player's actual bindings and press

            // modes before the hands and movement hooks block body controls.

            for (int index = 0; index < commandsList.Count;)
            {
                ECommand command = commandsList[index];
                // The pilot's hands and movement translators can block quick
                // slots before the player owner sees them. Consume the bound
                // remote here so it can also disconnect an active feed.
                if (Plugin.TryDisconnectWithRemote(command))
                {
                    commandsList.RemoveAt(index);
                    continue;
                }
                Plugin.CaptureFlightCommand(command);
                index++;
            }

        }

    }



    [HarmonyPatch(typeof(PlayerInputTranslator), nameof(PlayerInputTranslator.TranslateCommand))]

    internal static class DroneHandsInput

    {

        private static bool Prefix(PlayerInputTranslator __instance, ECommand command,

            ref InputNode.ETranslateResult __result)

        {

            if (!Plugin.Piloting(__instance._player) || command == ECommand.ToggleGoggles)

            {

                return true;

            }



            __result = InputNode.ETranslateResult.BlockAll;

            return false;

        }

    }



    [HarmonyPatch(typeof(MoveInputTranslator), nameof(MoveInputTranslator.TranslateAxes))]

    internal static class DroneAxes

    {

        private static bool Prefix(MoveInputTranslator __instance, ref float[] axes)

        {

            if (!Plugin.Piloting(__instance._controller))

            {

                return true;

            }



            Plugin.CaptureAxes(axes);

            return false;

        }

    }



    [HarmonyPatch(typeof(MoveInputTranslator), nameof(MoveInputTranslator.TranslateCommand))]

    internal static class DroneMovementCommands

    {

        private static bool Prefix(MoveInputTranslator __instance, ECommand command,

            ref InputNode.ETranslateResult __result)

        {

            if (!Plugin.Piloting(__instance._controller) || command == ECommand.ToggleGoggles)

            {

                return true;

            }



            __result = InputNode.ETranslateResult.BlockAll;

            return false;

        }

    }

}
