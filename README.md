# PlantTemporaryFlag

A tiny Kerbal Space Program 1.12.5 quality-of-life mod.

While on EVA, `Plant Temporary Flag` uses stock flag planting, automatically names the flag `temp`, and removes it 60 real seconds after placement completes. Each flag has its own timer, which continues across flight/tracking-station scene changes and after manual renaming. The stock planting/XP path is preserved.

Separately, loading a game or quicksave schedules deletion of every saved **Flag** vessel whose name is exactly `temp`, including flags on distant bodies that are not loaded into physics. This does not wait 60 seconds. Ordinary flag planting and other vessel types are unaffected. The automatic load sweep does not remove flags named `Flag`.

## 0.3.0 repair

- Replace the static KSP event subscriber that crashed in `EventData.EvtDelegate` at every startup with instance subscribers. Start the persistent manager at the main menu, and log successful initialization or its full exception.
- Capture stock `FlagSite.CreateFlag` results using the actual source EVA part, rather than a global count. Immediately persist the `temp` name and associate the returned vessel with its own timer.
- Bypass only that flag's initial rename dialog, invoke the stock completion callback, and start its deadline at placement completion. Later manual renaming remains available.
- Do not use vessel GUIDs as timer keys: stock flags in the reported save share the all-zero GUID. Retry failed deletion, discard cancelled planting requests, and clear session state when a new save is loaded.
- Capture the saved `temp` flag vessels in the instance `onGameStateLoad` handler, after stock `FlightState.Load`; delete them outside the event callback. Ordinary scene changes do not erase fresh timers.

The two surviving Moho flags reported in quicksave #42 are named `Flag`, with persistent IDs `534411080` and `419995446`. They need a separate, backed-up save repair at installation; the permanent mod deliberately contains no broad legacy `Flag` deletion rule.

## Validation

`./test.ps1` compiles the production source against a small offline KSP/Unity harness. Its 23 assertions reproduce the old static-subscriber crash and exercise startup, concurrent plants, shared zero GUIDs, saved naming, stock callback continuation, independent deadlines, manual renaming, unloaded vessels, scene transitions, save-load cleanup, exact name/type filtering, planting failure/expiry, removal retry, and event unsubscription.

The production DLL also builds against the installed KSP 1.12.5 and Harmony assemblies. Offline tests do not prove native Harmony patching, Unity lifetime behaviour, visual disappearance, XP, or quickload behaviour in the running game. Those gameplay checks remain pending. Operation logs use the `[PlantTemporaryFlag]` prefix.

## Build

```powershell
./build.ps1
```

Copy `artifacts/PlantTemporaryFlag.dll` and `GameData/PlantTemporaryFlag/PlantTemporaryFlag.cfg` into the KSP installation's `GameData` directory.

Requires the Harmony library bundled by many modern KSP mod installations (`GameData/000_Harmony/0Harmony.dll`).
