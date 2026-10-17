param([Parameter(Mandatory = $true)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'This control requires actual Windows native windows.' }
$repo = Split-Path -Parent $PSScriptRoot
$root = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $root -ErrorAction Stop | Out-Null
Copy-Item (Join-Path $repo 'eng/WindowsTaskbarPolicy/Program.cs') $root
foreach ($file in @('WpfWin32TaskbarPolicy.cs', 'Win32WpfTaskbarOperations.cs', 'WpfWin32TaskbarIcons.cs')) {
    Copy-Item (Join-Path $repo "src/ProGPU.Wpf/Platform/$file") $root
}
Copy-Item (Join-Path $repo 'external/ProGPU/src/ProGPU.Backend/NativeWindowTypes.cs') $root
'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0-windows</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup></Project>' | Set-Content "$root/Probe.csproj"
'{"sdk":{"version":"10.0.201","rollForward":"disable"}}' | Set-Content "$root/global.json"
Get-FileHash "$root/*.cs" -Algorithm SHA256 | ConvertTo-Json | Set-Content "$root/source-hashes.json"
Push-Location $root
try {
    dotnet build Probe.csproj -c Release -m:1 -p:UseSharedCompilation=false *> build.log
    if ($LASTEXITCODE -ne 0) { throw 'Actual native taskbar policy did not compile.' }
    $process = Start-Process dotnet -ArgumentList @('bin/Release/net10.0-windows/Probe.dll') -PassThru -NoNewWindow -RedirectStandardOutput observations.json -RedirectStandardError stderr.log
    if (-not $process.WaitForExit(30000)) { $process.Kill(); throw 'Native control exceeded its 30-second process bound.' }
    if ($process.ExitCode -ne 0) { throw 'Native taskbar ownership control failed.' }
    $result = Get-Content observations.json -Raw | ConvertFrom-Json
    if ($result.records.Count -ne 10 -or $result.qualified -or -not $result.diagnosticOnly -or $result.failure) { throw 'Unexpected native control inventory or failure.' }
} finally { Pop-Location }
