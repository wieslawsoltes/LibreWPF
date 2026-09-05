param(
    [string] $Configuration = "Release",
    [ValidateSet("vs", "dotnet")]
    [string] $MSBuildEngine = "vs",
    [switch] $NativeToolsOnMachine
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$buildCommand = Join-Path $repoRoot "build.cmd"
$buildTasksProject = Join-Path $repoRoot "src/Microsoft.DotNet.Wpf/src/PresentationBuildTasks/PresentationBuildTasks.csproj"
$project = Join-Path $repoRoot "src/Microsoft.DotNet.Wpf/src/PresentationCore/PresentationCore.csproj"
$outputDirectory = Join-Path $repoRoot "artifacts/windows-managed-runtime"
$versionDetailsPath = Join-Path $repoRoot "eng/Version.Details.props"
$globalJsonPath = Join-Path $repoRoot "global.json"
$packagesDirectory = Join-Path $repoRoot ".packages"
$globalJson = Get-Content -Path $globalJsonPath -Raw | ConvertFrom-Json

function Initialize-BuildSdk {
    $sdkVersion = [string]$globalJson.sdk.version
    if ([string]::IsNullOrWhiteSpace($sdkVersion)) {
        throw "sdk.version is missing from $globalJsonPath."
    }

    $sdkDirectory = Join-Path $repoRoot ".dotnet/sdk/$sdkVersion"
    if (!(Test-Path (Join-Path $sdkDirectory "Sdks/Microsoft.NET.Sdk/Sdk"))) {
        $sdkDirectory = $null
    }

    $dotnetCommand = Get-Command dotnet.exe -ErrorAction SilentlyContinue
    if ([string]::IsNullOrWhiteSpace($sdkDirectory) -and $null -ne $dotnetCommand) {
        Push-Location $repoRoot
        try {
            $effectiveSdkVersion = (& $dotnetCommand.Source --version 2>$null | Select-Object -Last 1)
            $sdkResolutionExitCode = $LASTEXITCODE
        }
        finally {
            Pop-Location
        }

        if ($sdkResolutionExitCode -eq 0 -and ![string]::IsNullOrWhiteSpace($effectiveSdkVersion)) {
            $sdkLine = & $dotnetCommand.Source --list-sdks |
                Where-Object { $_ -like "$effectiveSdkVersion *" } |
                Select-Object -Last 1
            if ($sdkLine -match '^\S+\s+\[(.+)\]$') {
                $candidate = Join-Path $Matches[1] $effectiveSdkVersion
                if (Test-Path (Join-Path $candidate "Sdks/Microsoft.NET.Sdk/Sdk")) {
                    $sdkDirectory = $candidate
                }
            }
        }
    }

    if ([string]::IsNullOrWhiteSpace($sdkDirectory)) {
        $dotnetInstall = Join-Path $repoRoot "eng/common/dotnet-install.ps1"
        & $dotnetInstall -version $sdkVersion -runtime sdk
        if ($LASTEXITCODE -ne 0) {
            throw "Installing the pinned .NET SDK $sdkVersion failed."
        }

        $sdkDirectory = Join-Path $repoRoot ".dotnet/sdk/$sdkVersion"
    }

    $sdkResolverPath = Join-Path $sdkDirectory "Sdks"
    if (!(Test-Path (Join-Path $sdkResolverPath "Microsoft.NET.Sdk/Sdk"))) {
        throw "The pinned .NET SDK resolver is missing from $sdkResolverPath."
    }

    $dotnetRoot = Split-Path -Parent (Split-Path -Parent $sdkDirectory)
    $env:DOTNET_ROOT = $dotnetRoot
    $env:PATH = "$dotnetRoot;$env:PATH"
    $env:MSBuildSDKsPath = $sdkResolverPath
    # PresentationCore does not consume SDK workloads. Visual Studio MSBuild
    # otherwise asks its own resolver for workload locator SDKs that are not
    # part of the standalone pinned SDK layout used by clean Build Tools VMs.
    $env:MSBuildEnableWorkloadResolver = "false"
}

Initialize-BuildSdk

Remove-Item -Path $outputDirectory -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

$perlCommandInfo = Get-Command perl.exe -ErrorAction SilentlyContinue
if ($null -ne $perlCommandInfo) {
    $perlCommand = $perlCommandInfo.Source
}
else {
    # Arcade restores this pinned native tool before invoking MSBuild. Resolve
    # the future path now so a clean Windows build agent or integration VM does
    # not also need a machine-wide Strawberry Perl installation.
    $strawberryPerlVersion = [string]$globalJson.'native-tools'.'strawberry-perl'
    if ([string]::IsNullOrWhiteSpace($strawberryPerlVersion)) {
        throw "native-tools.strawberry-perl is missing from $globalJsonPath."
    }

    $perlCommand = Join-Path $repoRoot ".tools/native/bin/strawberry-perl/$strawberryPerlVersion/portableshell.bat"
}

$versionDetails = [xml](Get-Content -Path $versionDetailsPath -Raw)
$netCoreAppVersion = [string]($versionDetails.Project.PropertyGroup.MicrosoftNETCoreAppRefPackageVersion | Select-Object -First 1)
if ([string]::IsNullOrWhiteSpace($netCoreAppVersion)) {
    throw "MicrosoftNETCoreAppRefPackageVersion is missing from $versionDetailsPath."
}

$runtimeIdentifiers = @("win-x86", "win-x64", "win-arm64")
$restoreRoot = Join-Path ([System.IO.Path]::GetTempPath()) "librewpf-ijw-host-$([guid]::NewGuid().ToString('N'))"
$restoreProject = Join-Path $restoreRoot "IjwHostRestore.csproj"
New-Item -ItemType Directory -Path $restoreRoot -Force | Out-Null
try {
    $packageDownloads = ($runtimeIdentifiers | ForEach-Object {
        "    <PackageDownload Include=`"Microsoft.NETCore.App.Host.$_`" Version=`"[$netCoreAppVersion]`" />"
    }) -join [Environment]::NewLine

    @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RestorePackagesPath>$packagesDirectory</RestorePackagesPath>
  </PropertyGroup>
  <ItemGroup>
$packageDownloads
  </ItemGroup>
</Project>
"@ | Set-Content -Path $restoreProject -Encoding utf8

    dotnet restore $restoreProject --configfile (Join-Path $repoRoot "NuGet.config") --force --no-cache
    if ($LASTEXITCODE -ne 0) {
        throw "Restoring the Windows IJW host packs failed."
    }
}
finally {
    Remove-Item -Path $restoreRoot -Recurse -Force -ErrorAction SilentlyContinue
}

function Invoke-WpfProjectBuild([string] $projectPath, [string] $platform, [string] $runtimeIdentifier, [string] $ijwHostSourcePath = "") {
    $runtimeIdentifierArgument = @()
    if (![string]::IsNullOrWhiteSpace($runtimeIdentifier)) {
        $runtimeIdentifierArgument = "/p:RuntimeIdentifier=$runtimeIdentifier"
    }

    $ijwHostArgument = @()
    if (![string]::IsNullOrWhiteSpace($ijwHostSourcePath)) {
        $ijwHostArgument = "/p:IjwHostSourcePath=$ijwHostSourcePath"
    }

    $nativeToolsArgument = @()
    if ($NativeToolsOnMachine) {
        # This is an optimization for prepared build images only. Clean agents
        # and integration VMs must let Arcade restore the versions pinned by
        # global.json instead of depending on mutable machine-wide tools.
        $nativeToolsArgument = @("-nativeToolsOnMachine")
    }

    & $buildCommand `
        -ci `
        -configuration $Configuration `
        -platform $platform `
        -projects $projectPath `
        -msbuildEngine $MSBuildEngine `
        $nativeToolsArgument `
        -excludeCIBinarylog `
        -warnAsError 0 `
        "/p:PerlCommand=$perlCommand" `
        $runtimeIdentifierArgument `
        $ijwHostArgument `
        /p:RunNetFrameworkApiCompat=false `
        /p:RunRefApiCompat=false
    if ($LASTEXITCODE -ne 0) {
        throw "Building $projectPath for $platform failed."
    }
}

Invoke-WpfProjectBuild $buildTasksProject "x86" ""

$runtimePlatforms = [ordered]@{
    "win-x86" = "x86"
    "win-x64" = "x64"
    "win-arm64" = "arm64"
}

foreach ($entry in $runtimePlatforms.GetEnumerator()) {
    $runtimeIdentifier = $entry.Key
    $platform = $entry.Value
    $ijwHost = Join-Path $packagesDirectory "microsoft.netcore.app.host.$runtimeIdentifier/$netCoreAppVersion/runtimes/$runtimeIdentifier/native/ijwhost.dll"
    if (!(Test-Path $ijwHost)) {
        throw "The $runtimeIdentifier IJW host was not restored at $ijwHost."
    }

    Invoke-WpfProjectBuild $project $platform $runtimeIdentifier $ijwHost

    $presentationCore = Join-Path $repoRoot "artifacts/bin/PresentationCore/$platform/$Configuration/net10.0/$runtimeIdentifier/PresentationCore.dll"
    if (!(Test-Path $presentationCore)) {
        throw "The Windows PresentationCore build did not produce $presentationCore."
    }

    $directWriteForwarderRoot = Join-Path $repoRoot "artifacts/bin/DirectWriteForwarder"
    if ($platform -ne "x86") {
        $directWriteForwarderRoot = Join-Path $directWriteForwarderRoot $platform
    }

    $directWriteForwarder = Join-Path $directWriteForwarderRoot "$Configuration/net10.0/DirectWriteForwarder.dll"
    if (!(Test-Path $directWriteForwarder)) {
        throw "The Windows PresentationCore build did not produce $directWriteForwarder."
    }

    $runtimeOutput = Join-Path $outputDirectory "$runtimeIdentifier/net10.0"
    New-Item -ItemType Directory -Path $runtimeOutput -Force | Out-Null
    Copy-Item $presentationCore (Join-Path $runtimeOutput "PresentationCore.dll") -Force
    Copy-Item $directWriteForwarder (Join-Path $runtimeOutput "DirectWriteForwarder.dll") -Force

    $nativeRuntimeOutput = Join-Path $outputDirectory "$runtimeIdentifier/native"
    New-Item -ItemType Directory -Path $nativeRuntimeOutput -Force | Out-Null
    Copy-Item $ijwHost (Join-Path $nativeRuntimeOutput "ijwhost.dll") -Force

    $pdb = [System.IO.Path]::ChangeExtension($presentationCore, ".pdb")
    if (Test-Path $pdb) {
        Copy-Item $pdb (Join-Path $runtimeOutput "PresentationCore.pdb") -Force
    }

    $directWriteForwarderPdb = [System.IO.Path]::ChangeExtension($directWriteForwarder, ".pdb")
    if (Test-Path $directWriteForwarderPdb) {
        Copy-Item $directWriteForwarderPdb (Join-Path $runtimeOutput "DirectWriteForwarder.pdb") -Force
    }
}

Write-Host "Staged Windows managed runtime payload at $outputDirectory."
