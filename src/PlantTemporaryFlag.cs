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
        private static readonly Dictionary<int, float> TemporaryFlags = new Dictionary<int, float>();
        private static FieldInfo siteNameField;
        private static FieldInfo plaqueTextField;
        private static MethodInfo canPlantFlagMethod;
        private static Harmony harmony;

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            siteNameField = typeof(FlagSite).GetField("siteName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            plaqueTextField = typeof(FlagSite).GetField("newPlaqueText", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            canPlantFlagMethod = typeof(KerbalEVA).GetMethod("CanPlantFlag", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            harmony = new Harmony(HarmonyId);
            harmony.Patch(
                AccessTools.Method(typeof(FlagSite), "RenameSite", new[] { typeof(Callback) }),
                prefix: new HarmonyMethod(typeof(Bootstrap), nameof(RenameTemporaryFlag)));
        }

        private void Update()
        {
            if (TemporaryFlags.Count == 0 || Time.unscaledTime < 0f)
                return;

            var expired = new List<int>();
            foreach (var item in TemporaryFlags)
            {
                if (Time.unscaledTime >= item.Value)
                    expired.Add(item.Key);
            }

            foreach (var vesselId in expired)
            {
                TemporaryFlags.Remove(vesselId);
                RemoveTemporaryFlag(vesselId);
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
            TemporaryFlags[__instance.vessel.id.GetHashCode()] = Time.unscaledTime + CleanupDelay;

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

        private static void RemoveTemporaryFlag(int vesselId)
        {
            foreach (var vessel in FlightGlobals.VesselsLoaded)
            {
                if (vessel == null || vessel.id.GetHashCode() != vesselId)
                    continue;

                var flag = vessel.FindPartModuleImplementing<FlagSite>();
                if (flag != null && string.Equals(GetSiteName(flag), TemporaryName, StringComparison.Ordinal))
                    flag.TakeDown();
                return;
            }
        }

        private static string GetSiteName(FlagSite flag)
        {
            return siteNameField == null ? null : siteNameField.GetValue(flag) as string;
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
