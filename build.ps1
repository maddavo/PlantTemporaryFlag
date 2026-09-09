param(
    [string]$KspDir = 'C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program'
)

$ErrorActionPreference = 'Stop'
$csc = 'C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
$managed = Join-Path $KspDir 'KSP_x64_Data\Managed'
$harmony = Join-Path $KspDir 'GameData\000_Harmony\0Harmony.dll'

if (!(Test-Path -LiteralPath $csc)) { throw "Roslyn compiler not found: $csc" }
if (!(Test-Path -LiteralPath (Join-Path $managed 'Assembly-CSharp.dll'))) { throw "KSP assemblies not found: $managed" }
if (!(Test-Path -LiteralPath $harmony)) { throw "Harmony not found: $harmony" }

New-Item -ItemType Directory -Force -Path 'artifacts' | Out-Null
& $csc /nologo /target:library /langversion:8 /optimize+ /debug- `
    /out:artifacts\PlantTemporaryFlag.dll `
    /reference:"$(Join-Path $managed 'Assembly-CSharp.dll')" `
    /reference:"$(Join-Path $managed 'UnityEngine.dll')" `
    /reference:"$(Join-Path $managed 'UnityEngine.CoreModule.dll')" `
    /reference:"$(Join-Path $managed 'UnityEngine.IMGUIModule.dll')" `
    /reference:"$harmony" `
    /reference:System.Core.dll /reference:System.dll `
    src\AssemblyInfo.cs src\PlantTemporaryFlag.cs

if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Output 'Built artifacts\PlantTemporaryFlag.dll'
