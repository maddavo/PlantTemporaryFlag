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
        internal const string LegacyTemporaryName = "Flag";
        internal const float CleanupDelay = 60f;

        private static int pendingPlants;
        private static readonly Dictionary<Guid, float> TemporaryFlags = new Dictionary<Guid, float>();
        private static FieldInfo siteNameField;
        private static FieldInfo plaqueTextField;
        private static FieldInfo vesselNameField;
        private static MethodInfo canPlantFlagMethod;
        private static Harmony harmony;

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            siteNameField = typeof(FlagSite).GetField("siteName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            plaqueTextField = typeof(FlagSite).GetField("newPlaqueText", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            vesselNameField = typeof(Vessel).GetField("vesselName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            canPlantFlagMethod = typeof(KerbalEVA).GetMethod("CanPlantFlag", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            harmony = new Harmony(HarmonyId);
            harmony.Patch(
                AccessTools.Method(typeof(FlagSite), "RenameSite", new[] { typeof(Callback) }),
                prefix: new HarmonyMethod(typeof(Bootstrap), nameof(RenameTemporaryFlag)));

            GameEvents.onLevelWasLoadedGUIReady.Add(OnLevelWasLoadedGUIReady);
            GameEvents.afterFlagPlanted.Add(OnAfterFlagPlanted);
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
            if (__instance == null || pendingPlants == 0)
                return true;

            SetField(siteNameField, __instance, TemporaryName);
            SetField(plaqueTextField, __instance, string.Empty);

            // Stock calls this callback after the rename dialog closes. Calling it here
            // completes the same stock placement/XP path without showing a dialog.
            if (afterDialog != null)
                afterDialog();
            return false;
        }

        private static void OnAfterFlagPlanted(FlagSite flag)
        {
            if (flag == null || pendingPlants == 0 || flag.vessel == null)
                return;

            pendingPlants--;
            SetField(vesselNameField, flag.vessel, TemporaryName);
            if (flag.vessel.protoVessel != null)
                flag.vessel.protoVessel.vesselName = TemporaryName;
            TemporaryFlags[flag.vessel.id] = Time.unscaledTime + CleanupDelay;
        }

        private static bool RemoveTemporaryFlag(Guid vesselId)
        {
            foreach (var vessel in FlightGlobals.Vessels)
            {
                if (vessel == null || vessel.id != vesselId)
                    continue;

                return RemoveTemporaryVessel(vessel);
            }
            return false;
        }

        private static void CleanupTemporaryFlags()
        {
            var vessels = new List<Vessel>(FlightGlobals.Vessels);
            foreach (var vessel in vessels)
            {
                if (vessel == null || !IsTemporaryName(GetVesselName(vessel)))
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

        private static bool IsTemporaryName(string name)
        {
            return string.Equals(name, TemporaryName, StringComparison.Ordinal)
                || string.Equals(name, LegacyTemporaryName, StringComparison.Ordinal);
        }

        private static bool RemoveTemporaryVessel(Vessel vessel)
        {
            if (vessel == null || vessel.vesselType != VesselType.Flag)
                return false;

            vessel.Die();
            return true;
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
