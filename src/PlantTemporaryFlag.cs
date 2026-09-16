using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace PlantTemporaryFlag
{
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public sealed class Bootstrap : MonoBehaviour
    {
        internal const string HarmonyId = "maddavo.planttemporaryflag";
        internal const string TemporaryName = "temp";
        internal const float CleanupDelay = 60f;

        private static int pendingPlants;
        private static readonly Dictionary<Guid, float> TemporaryFlags = new Dictionary<Guid, float>();
        private static FieldInfo siteNameField;
        private static FieldInfo plaqueTextField;
        private static MethodInfo vesselRenameAcceptMethod;
        private static MethodInfo canPlantFlagMethod;
        private static Harmony harmony;

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            siteNameField = typeof(FlagSite).GetField("siteName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            plaqueTextField = typeof(FlagSite).GetField("newPlaqueText", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            vesselRenameAcceptMethod = typeof(Vessel).GetMethod("onVesselRenameAccept", BindingFlags.Instance | BindingFlags.NonPublic);
            canPlantFlagMethod = typeof(KerbalEVA).GetMethod("CanPlantFlag", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            harmony = new Harmony(HarmonyId);
            harmony.Patch(
                AccessTools.Method(typeof(FlagSite), "RenameSite", new[] { typeof(Callback) }),
                prefix: new HarmonyMethod(typeof(Bootstrap), nameof(RenameTemporaryFlag)));

            GameEvents.onLevelWasLoadedGUIReady.Add(OnLevelWasLoadedGUIReady);
        }

        private void OnLevelWasLoadedGUIReady(GameScenes scene)
        {
            CleanupTemporaryFlags();
        }

        private void Update()
        {
            if (TemporaryFlags.Count == 0 || Time.unscaledTime < 0f)
                return;

            var expired = new List<Guid>();
            foreach (var item in TemporaryFlags)
            {
                if (Time.unscaledTime >= item.Value)
                    expired.Add(item.Key);
            }

            foreach (var vesselId in expired)
            {
                if (RemoveTemporaryFlag(vesselId))
                    TemporaryFlags.Remove(vesselId);
                else
                    TemporaryFlags[vesselId] = Time.unscaledTime + 5f;
            }
        }

        internal static void QueuePlant(Vessel vessel)
        {
            if (vessel != null)
                pendingPlants++;
        }

        internal static bool CanPlantFlag(KerbalEVA eva)
        {
            return eva != null && canPlantFlagMethod != null && (bool)canPlantFlagMethod.Invoke(eva, null);
        }

        private static bool RenameTemporaryFlag(FlagSite __instance, Callback afterDialog)
        {
            if (__instance == null || !IsPending(__instance.vessel))
                return true;

            SetField(siteNameField, __instance, TemporaryName);
            SetField(plaqueTextField, __instance, string.Empty);
            if (__instance.vessel != null)
            {
                // The FlagSite field above is runtime-only. Rename the vessel through
                // KSP's own path so the marker is written to the save file as well.
                vesselRenameAcceptMethod?.Invoke(__instance.vessel, new object[] { TemporaryName, VesselType.Flag });
                if (__instance.vessel.protoVessel != null)
                    __instance.vessel.protoVessel.vesselName = TemporaryName;
                TemporaryFlags[__instance.vessel.id] = Time.unscaledTime + CleanupDelay;
            }

            // Stock calls this callback after the rename dialog closes. Calling it here
            // completes the same stock placement/XP path without showing a dialog.
            if (afterDialog != null)
                afterDialog();
            return false;
        }

        private static bool IsPending(Vessel flagVessel)
        {
            if (flagVessel == null || pendingPlants == 0)
                return false;
            pendingPlants--;
            return true;
        }

        private static bool RemoveTemporaryFlag(Guid vesselId)
        {
            foreach (var vessel in FlightGlobals.Vessels)
            {
                if (vessel == null || vessel.id != vesselId)
                    continue;

                RemoveTemporaryVessel(vessel);
                return true;
            }
            return false;
        }

        private static void CleanupTemporaryFlags()
        {
            var vessels = new List<Vessel>(FlightGlobals.Vessels);
            foreach (var vessel in vessels)
            {
                if (vessel == null || !string.Equals(GetVesselName(vessel), TemporaryName, StringComparison.Ordinal))
                    continue;

                RemoveTemporaryVessel(vessel);
            }

            TemporaryFlags.Clear();
            pendingPlants = 0;
        }

        private static string GetSiteName(FlagSite flag)
        {
            return siteNameField == null ? null : siteNameField.GetValue(flag) as string;
        }

        private static string GetVesselName(Vessel vessel)
        {
            return vessel == null ? null : vessel.GetName();
        }

        private static void RemoveTemporaryVessel(Vessel vessel)
        {
            if (vessel == null || vessel.vesselType != VesselType.Flag)
                return;

            if (!FlightGlobals.VesselsLoaded.Contains(vessel))
            {
                vessel.Die();
                return;
            }

            var flag = vessel.FindPartModuleImplementing<FlagSite>();
            if (flag != null)
                flag.TakeDown();
            else
                vessel.Die();
        }

        private static void SetField(FieldInfo field, object target, object value)
        {
            if (field != null)
                field.SetValue(target, value);
        }
    }

    public sealed class TemporaryFlagEVA : PartModule
    {
        [KSPEvent(guiActive = true, guiName = "Plant Temporary Flag", active = true)]
        public void PlantTemporaryFlag()
        {
            var eva = part.FindModuleImplementing<KerbalEVA>();
            if (!Bootstrap.CanPlantFlag(eva))
                return;

            Bootstrap.QueuePlant(vessel);
            eva.PlantFlag();
        }
    }
}
