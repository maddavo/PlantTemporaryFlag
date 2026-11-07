$ErrorActionPreference = 'Stop'
$csc = 'C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
& $csc /nologo /target:exe /langversion:8 /out:artifacts\LifecycleTests.exe src\PlantTemporaryFlag.cs tests\LifecycleTests.cs
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed' }
& .\artifacts\LifecycleTests.exe
if ($LASTEXITCODE -ne 0) { throw 'Lifecycle tests failed' }
