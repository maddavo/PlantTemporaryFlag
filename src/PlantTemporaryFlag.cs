using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace PlantTemporaryFlag
{
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public sealed class Bootstrap : MonoBehaviour
    {
        internal const string HarmonyId = "maddavo.planttemporaryflag";
        internal const string TemporaryName = "temp";
        internal const float CleanupDelay = 60f;

        private sealed class TrackedFlag
        {
            internal Vessel Vessel;
            internal uint PersistentId;
            internal float Deadline;
            internal bool Placed;
        }

        // Stock flags can share Guid.Empty. Never key timers by vessel GUID.
        private static readonly List<TrackedFlag> Flags = new List<TrackedFlag>();
        private static readonly Dictionary<Part, float> Requests = new Dictionary<Part, float>();
        private readonly List<Vessel> loadedFlags = new List<Vessel>();
        private int cleanupFrame = -1;
        private static bool ready;
        private static Harmony harmony;
        private static FieldInfo siteNameField;
        private static MethodInfo canPlantFlagMethod;

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            try
            {
                siteNameField = AccessTools.Field(typeof(FlagSite), "siteName");
                canPlantFlagMethod = AccessTools.Method(typeof(KerbalEVA), "CanPlantFlag", Type.EmptyTypes);
                if (siteNameField == null || canPlantFlagMethod == null)
                    throw new MissingMemberException("Required stock flag members were not found.");
                harmony = new Harmony(HarmonyId);
                harmony.Patch(AccessTools.Method(typeof(FlagSite), "CreateFlag",
                    new[] { typeof(Vector3), typeof(Quaternion), typeof(Part) }),
                    postfix: new HarmonyMethod(typeof(Bootstrap), nameof(FlagCreated)));
                harmony.Patch(AccessTools.Method(typeof(FlagSite), "RenameSite", new[] { typeof(Callback) }),
                    prefix: new HarmonyMethod(typeof(Bootstrap), nameof(RenameTemporaryFlag)));
                // KSP EventData dereferences delegate.Target: subscribers MUST be instance methods.
                GameEvents.onGameStateLoad.Add(OnGameStateLoad);
                GameEvents.onLevelWasLoadedGUIReady.Add(OnSceneReady);
                ready = true;
                Log("0.3.0 ready; creation/placement patches and instance event handlers installed.");
            }
            catch (Exception error)
            {
                ready = false;
                Debug.LogError("[PlantTemporaryFlag] Initialisation failed: " + error);
            }
        }

        private void OnDestroy()
        {
            ready = false;
            GameEvents.onGameStateLoad.Remove(OnGameStateLoad);
            GameEvents.onLevelWasLoadedGUIReady.Remove(OnSceneReady);
            harmony?.UnpatchAll(HarmonyId);
            Flags.Clear();
            Requests.Clear();
        }

        internal static bool CanPlantFlag(KerbalEVA eva)
        {
            return ready && eva != null && (bool)canPlantFlagMethod.Invoke(eva, null)
                && !Requests.ContainsKey(eva.part);
        }

        internal static void Plant(KerbalEVA eva)
        {
            if (!CanPlantFlag(eva)) return;
            Requests[eva.part] = Time.unscaledTime + 30f;
            Log("Plant requested by " + eva.vessel.vesselName);
            try { eva.PlantFlag(); }
            catch { Requests.Remove(eva.part); throw; }
        }

        // __2 is the source EVA part: concurrent plants cannot consume each other's request.
        private static void FlagCreated(FlagSite __result, Part __2)
        {
            if (__2 == null || !Requests.TryGetValue(__2, out var deadline)) return;
            Requests.Remove(__2);
            if (Time.unscaledTime > deadline || __result == null || __result.vessel == null)
            {
                Log("Plant request expired or stock creation failed.");
                return;
            }
            SetName(__result);
            var vessel = __result.vessel;
            Flags.Add(new TrackedFlag { Vessel = vessel, PersistentId = vessel.persistentId,
                Deadline = Time.unscaledTime + CleanupDelay });
            Log("Captured temporary flag " + vessel.persistentId);
        }

        private static void SetName(FlagSite flag)
        {
            siteNameField.SetValue(flag, TemporaryName);
            flag.vessel.vesselName = TemporaryName;
            if (flag.vessel.protoVessel != null) flag.vessel.protoVessel.vesselName = TemporaryName;
        }

        private static bool RenameTemporaryFlag(FlagSite __instance, Callback afterDialog)
        {
            if (__instance == null) return true;
            var tracked = Flags.Find(item => ReferenceEquals(item.Vessel, __instance.vessel));
            // Only bypass initial placement. Later manual renaming stays stock.
            if (tracked == null || tracked.Placed || afterDialog == null) return true;
            SetName(__instance);
            tracked.Placed = true;
            tracked.PersistentId = __instance.vessel.persistentId;
            tracked.Deadline = Time.unscaledTime + CleanupDelay;
            Log("Placement complete; flag " + tracked.PersistentId + " expires in 60 real seconds.");
            // Preserve afterFlagPlanted and KerbalEVA's subsequent stock XP path.
            afterDialog();
            return false;
        }

        private void OnGameStateLoad(ConfigNode node)
        {
            Flags.Clear();
            Requests.Clear();
            loadedFlags.Clear();
            // Installed Game.Load fires AFTER FlightState.Load instantiates all vessels.
            foreach (var vessel in FlightGlobals.Vessels)
                if (vessel != null && vessel.vesselType == VesselType.Flag &&
                    string.Equals(vessel.vesselName, TemporaryName, StringComparison.Ordinal))
                    loadedFlags.Add(vessel);
            cleanupFrame = Time.frameCount + 2;
            Log("Loaded-game sweep queued for " + loadedFlags.Count + " temp flags.");
        }

        private void OnSceneReady(GameScenes scene)
        {
            if (scene != GameScenes.MAINMENU) return;
            Flags.Clear();
            Requests.Clear();
            loadedFlags.Clear();
            cleanupFrame = -1;
        }

        private void Update()
        {
            if (!ready) return;
            if (cleanupFrame >= 0 && Time.frameCount >= cleanupFrame)
            {
                for (int i = loadedFlags.Count - 1; i >= 0; --i)
                    if (RemoveFlag(loadedFlags[i], "loaded-game cleanup")) loadedFlags.RemoveAt(i);
                cleanupFrame = loadedFlags.Count == 0 ? -1 : Time.frameCount + 60;
            }
            var expired = new List<Part>();
            foreach (var request in Requests)
                if (request.Key == null || Time.unscaledTime >= request.Value) expired.Add(request.Key);
            foreach (var part in expired) Requests.Remove(part);
            for (int i = Flags.Count - 1; i >= 0; --i)
            {
                var tracked = Flags[i];
                if (Time.unscaledTime < tracked.Deadline) continue;
                var vessel = tracked.Vessel;
                if (vessel == null && tracked.PersistentId != 0)
                    vessel = FlightGlobals.Vessels.Find(v => v != null && v.vesselType == VesselType.Flag
                        && v.persistentId == tracked.PersistentId);
                if (RemoveFlag(vessel, "60-second expiry")) Flags.RemoveAt(i);
                else tracked.Deadline = Time.unscaledTime + 5f;
            }
        }

        private static bool RemoveFlag(Vessel vessel, string reason)
        {
            if (vessel == null || vessel.vesselType != VesselType.Flag) return true;
            try
            {
                Log("Removing flag " + vessel.persistentId + " (" + vessel.vesselName + "): " + reason);
                vessel.Die(); // Stock removal handles loaded and unloaded vessels.
                return true;
            }
            catch (Exception error)
            {
                Debug.LogError("[PlantTemporaryFlag] Removal failed; will retry: " + error);
                return false;
            }
        }

        private static void Log(string message) { Debug.Log("[PlantTemporaryFlag] " + message); }
    }

    public sealed class TemporaryFlagEVA : PartModule
    {
        [KSPEvent(guiActive = true, guiName = "Plant Temporary Flag", active = true)]
        public void PlantTemporaryFlag()
        {
            Bootstrap.Plant(part.FindModuleImplementing<KerbalEVA>());
        }
    }
}
