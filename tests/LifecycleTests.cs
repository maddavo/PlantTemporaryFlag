// Offline behaviour harness. Compile the production source against these narrow KSP/Unity
// doubles. This exercises our logic; it is not an in-game Unity/Harmony integration test.
using System;
using System.Collections.Generic;
using System.Reflection;
using PlantTemporaryFlag;

namespace UnityEngine
{
    public class MonoBehaviour { public object gameObject; public static void DontDestroyOnLoad(object o) {} }
    public struct Vector3 {}
    public struct Quaternion {}
    public static class Time { public static float unscaledTime; public static int frameCount; }
    public static class Debug
    {
        public static readonly List<string> Errors = new List<string>();
        public static void Log(object o) {}
        public static void LogError(object o) { Errors.Add(o.ToString()); }
    }
}
namespace HarmonyLib
{
    public class HarmonyMethod
    {
        public MethodInfo Method;
        public HarmonyMethod(Type t, string n) { Method = AccessTools.Method(t, n); }
    }
    public class Harmony
    {
        public static MethodInfo Created, Rename;
        public Harmony(string id) {}
        public void Patch(MethodInfo m, HarmonyMethod prefix = null, HarmonyMethod postfix = null)
        {
            if (m == null) throw new Exception("Missing patch target");
            if (m.Name == "CreateFlag") Created = postfix.Method;
            if (m.Name == "RenameSite") Rename = prefix.Method;
        }
        public void UnpatchAll(string id) {}
    }
    public static class AccessTools
    {
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        public static FieldInfo Field(Type t, string n) { return t.GetField(n, All); }
        public static MethodInfo Method(Type t, string n, Type[] args = null)
        { return args == null ? t.GetMethod(n, All) : t.GetMethod(n, All, null, args, null); }
    }
}
public class KSPAddon : Attribute
{
    public enum Startup { MainMenu, Instantly }
    public KSPAddon(Startup s, bool once) {}
}
public class KSPEvent : Attribute { public bool guiActive, active; public string guiName; }
public delegate void Callback();
public enum GameScenes { MAINMENU, FLIGHT, TRACKSTATION, SPACECENTER }
public enum VesselType { Flag, Ship, EVA }
public class ConfigNode {}
public class EventData<T>
{
    public delegate void OnEvent(T value);
    readonly List<OnEvent> handlers = new List<OnEvent>();
    public void Add(OnEvent h)
    {
        // Match the actual installed KSP EventData/EvtDelegate constructor.
        h.Target.GetType();
        handlers.Add(h);
    }
    public void Remove(OnEvent h) { handlers.Remove(h); }
    public void Fire(T v) { foreach (var h in handlers.ToArray()) h(v); }
    public int Count { get { return handlers.Count; } }
}
public static class GameEvents
{
    public static EventData<ConfigNode> onGameStateLoad = new EventData<ConfigNode>();
    public static EventData<GameScenes> onLevelWasLoadedGUIReady = new EventData<GameScenes>();
    public static EventData<FlagSite> afterFlagPlanted = new EventData<FlagSite>();
}
public class PartModule { public Part part; public Vessel vessel { get { return part.vessel; } } }
public class Part
{
    public Vessel vessel;
    public KerbalEVA Eva;
    public T FindModuleImplementing<T>() where T : class { return Eva as T; }
}
public class ProtoVessel { public string vesselName; }
public class Vessel
{
    public string vesselName = "Flag";
    public Guid id; // Intentionally empty for EVERY flag in this test.
    public uint persistentId;
    public VesselType vesselType = VesselType.Flag;
    public ProtoVessel protoVessel = new ProtoVessel();
    public bool Dead, ThrowOnDie, loaded = true;
    public void Die()
    {
        if (ThrowOnDie) throw new Exception("simulated deletion failure");
        Dead = true;
        FlightGlobals.Vessels.Remove(this);
    }
}
public static class FlightGlobals { public static List<Vessel> Vessels = new List<Vessel>(); }
public class KerbalEVA : PartModule
{
    public bool Allowed = true, ThrowPlant;
    public bool XP;
    private bool CanPlantFlag() { return Allowed; }
    public void PlantFlag() { if (ThrowPlant) throw new Exception("simulated planting failure"); }
    public FlagSite Create(uint id) { return FlagSite.CreateFlag(new UnityEngine.Vector3(), new UnityEngine.Quaternion(), part, id); }
    public void Complete(FlagSite flag)
    {
        flag.RenameSite(() => GameEvents.afterFlagPlanted.Fire(flag));
        XP = true; // The stock KerbalEVA flagPlant_OnLeave continues to its XP code.
    }
}
public class FlagSite : PartModule
{
    public string siteName;
    public bool Dialog;
    public static FlagSite CreateFlag(UnityEngine.Vector3 p, UnityEngine.Quaternion q, Part source)
    { return CreateFlag(p, q, source, 1); }
    public static FlagSite CreateFlag(UnityEngine.Vector3 p, UnityEngine.Quaternion q, Part source, uint id)
    {
        var v = new Vessel { persistentId = id };
        FlightGlobals.Vessels.Add(v);
        var f = new FlagSite { part = new Part { vessel = v } };
        HarmonyLib.Harmony.Created.Invoke(null, new object[] { f, source });
        return f;
    }
    public void RenameSite(Callback callback)
    {
        if ((bool)HarmonyLib.Harmony.Rename.Invoke(null, new object[] { this, callback })) Dialog = true;
    }
}
public static class LifecycleTests
{
    static Bootstrap manager;
    static int assertions;
    static void Check(bool okay, string message)
    { assertions++; if (!okay) throw new Exception(message); }
    static void Call(string name) { HarmonyLib.AccessTools.Method(typeof(Bootstrap), name).Invoke(manager, null); }
    static void Tick(float time)
    { UnityEngine.Time.unscaledTime = time; UnityEngine.Time.frameCount += 3; Call("Update"); }
    static KerbalEVA Eva()
    {
        var p = new Part { vessel = new Vessel { vesselName = "Test Kerbal", vesselType = VesselType.EVA } };
        return p.Eva = new KerbalEVA { part = p };
    }
    static void Reset()
    {
        GameEvents.onLevelWasLoadedGUIReady.Fire(GameScenes.MAINMENU);
        FlightGlobals.Vessels.Clear(); UnityEngine.Time.unscaledTime = 0;
    }
    public static void Main()
    {
        // Demonstrate that the old static subscriber fails against actual KSP semantics.
        bool rejected = false;
        try { new EventData<int>().Add(StaticHandler); } catch (NullReferenceException) { rejected = true; }
        Check(rejected, "Old static event-handler defect must reproduce");
        manager = new Bootstrap(); Call("Awake");
        Check(UnityEngine.Debug.Errors.Count == 0, "Startup must succeed");
        Check(GameEvents.onGameStateLoad.Count == 1, "Load event must subscribe");
        Check(GameEvents.onLevelWasLoadedGUIReady.Count == 1, "Scene event must subscribe");

        var a = Eva(); var b = Eva();
        Bootstrap.Plant(a); Bootstrap.Plant(b);
        var ordinary = Eva().Create(10);
        Check(ordinary.vessel.vesselName == "Flag", "Another EVA cannot consume a request");
        ordinary.RenameSite(() => {});
        Check(ordinary.Dialog, "Ordinary flag must keep its stock dialog");
        var first = a.Create(11); var second = b.Create(12);
        Check(first.vessel.id == second.vessel.id, "Reproduce shared empty GUIDs");
        Check(first.vessel.vesselName == "temp" && first.vessel.protoVessel.vesselName == "temp", "Persist temporary identity");
        Tick(5); a.Complete(first); Tick(15); b.Complete(second);
        Check(a.XP && b.XP, "Placement must return to stock XP path");
        Check(!first.Dialog && !second.Dialog, "No initial rename dialog");
        first.RenameSite(null); Check(first.Dialog, "Later manual rename must remain available");
        first.vessel.vesselName = "Renamed by user";
        GameEvents.onLevelWasLoadedGUIReady.Fire(GameScenes.TRACKSTATION);
        Tick(64.9f); Check(!first.vessel.Dead && !second.vessel.Dead, "Do not remove early or on scene change");
        Tick(65); Check(first.vessel.Dead && !second.vessel.Dead, "Independent deadline survives rename");
        second.vessel.loaded = false;
        Tick(75); Check(second.vessel.Dead && !ordinary.vessel.Dead, "Unloaded flag expiry and ordinary flag safety");

        Reset(); a = Eva(); Bootstrap.Plant(a); first = a.Create(20); a.Complete(first);
        Tick(10); GameEvents.onGameStateLoad.Fire(new ConfigNode());
        Check(!first.vessel.Dead, "Load sweep defers beyond load callback");
        Tick(11); Check(first.vessel.Dead, "Loading before expiry removes saved temp immediately");
        Reset();
        var saved = new Vessel { vesselName = "temp", persistentId = 30, loaded = false };
        var permanent = new Vessel { vesselName = "Flag", persistentId = 31 };
        var ship = new Vessel { vesselName = "temp", vesselType = VesselType.Ship };
        var caps = new Vessel { vesselName = "Temp" };
        FlightGlobals.Vessels.AddRange(new[] { saved, permanent, ship, caps });
        GameEvents.onGameStateLoad.Fire(new ConfigNode()); Tick(1);
        Check(saved.Dead && !permanent.Dead && !ship.Dead && !caps.Dead, "Exact-name/type load filter");

        Reset(); a = Eva(); Bootstrap.Plant(a); Tick(31); first = a.Create(40);
        Check(first.vessel.vesselName == "Flag", "Expired failed request cannot mark later stock planting");
        Reset(); a = Eva(); a.ThrowPlant = true;
        try { Bootstrap.Plant(a); } catch (Exception) {}
        a.ThrowPlant = false; Check(Bootstrap.CanPlantFlag(a), "Exception must release request");
        Bootstrap.Plant(a); first = a.Create(50); a.Complete(first);
        first.vessel.ThrowOnDie = true; Tick(60);
        Check(!first.vessel.Dead, "Simulated deletion failure");
        first.vessel.ThrowOnDie = false; Tick(65);
        Check(first.vessel.Dead, "Deletion failure retries");
        Reset(); a = Eva(); a.Allowed = false; Bootstrap.Plant(a); first = a.Create(60);
        Check(first.vessel.vesselName == "Flag", "Respect stock planting restrictions");
        Call("OnDestroy");
        Check(GameEvents.onGameStateLoad.Count == 0 && GameEvents.onLevelWasLoadedGUIReady.Count == 0, "Unsubscribe cleanly");
        Console.WriteLine(assertions + " lifecycle assertions passed.");
    }
    static void StaticHandler(int n) {}
}
