# PlantTemporaryFlag

A tiny Kerbal Space Program 1.12.5 quality-of-life mod.

While on EVA, `Plant Temporary Flag` uses stock flag planting, automatically names the flag `temp`, and removes it after 60 seconds. The saved vessel name is also `temp`, so a later game load can remove the flag even if the original session ended before the timer fired. For migration from v0.1, the load cleanup also removes flags named exactly `Flag`. Because stock planting completes the operation, the crew member still receives the normal flag-planting XP.

## Build

```powershell
./build.ps1
```

Copy `artifacts/PlantTemporaryFlag.dll` and `GameData/PlantTemporaryFlag/PlantTemporaryFlag.cfg` into the KSP installation's `GameData` directory.

Requires the Harmony library bundled by many modern KSP mod installations (`GameData/000_Harmony/0Harmony.dll`).
