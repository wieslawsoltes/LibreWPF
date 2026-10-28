#Requires -Version 7.0

param(
    [string] $Configuration = "Release",
    [ValidateSet("vs", "dotnet")]
    [string] $MSBuildEngine = "vs",
    [switch] $Rebuild,
    [switch] $NativeToolsOnMachine,
    [switch] $ValidateDesktopTransform
)

$ErrorActionPreference = "Stop"
if (-not $IsWindows) {
    throw "Windows managed runtime production requires a Windows PowerShell 7 host."
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$buildCommand = Join-Path $repoRoot "eng/common/build.ps1"
$buildPowerShell = Join-Path $PSHOME "pwsh.exe"
if (!(Test-Path $buildPowerShell -PathType Leaf)) {
    throw "The current PowerShell host has no child executable at $buildPowerShell."
}
$buildTasksProject = Join-Path $repoRoot "src/Microsoft.DotNet.Wpf/src/PresentationBuildTasks/PresentationBuildTasks.csproj"
$project = Join-Path $repoRoot "src/Microsoft.DotNet.Wpf/src/PresentationCore/PresentationCore.csproj"
$outputDirectory = Join-Path $repoRoot "artifacts/windows-managed-runtime"
$versionDetailsPath = Join-Path $repoRoot "eng/Version.Details.props"
$globalJsonPath = Join-Path $repoRoot "global.json"
$packagesDirectory = Join-Path $repoRoot ".packages"
$globalJson = Get-Content -Path $globalJsonPath -Raw | ConvertFrom-Json

function Test-X64DotNetHost([string] $path) {
    if (!(Test-Path $path -PathType Leaf)) { return $false }
    $stream = [System.IO.File]::OpenRead($path)
    try {
        $reader = [System.IO.BinaryReader]::new($stream)
        if ($stream.Length -lt 64 -or $reader.ReadUInt16() -ne 0x5a4d) { return $false }
        $stream.Position = 0x3c
        $peOffset = $reader.ReadUInt32()
        if ($peOffset -gt $stream.Length - 6) { return $false }
        $stream.Position = $peOffset
        return $reader.ReadUInt32() -eq 0x00004550 -and $reader.ReadUInt16() -eq 0x8664
    }
    finally {
        $stream.Dispose()
    }
}

function Initialize-BuildSdk {
    $sdkVersion = [string]$globalJson.sdk.version
    if ([string]::IsNullOrWhiteSpace($sdkVersion)) {
        throw "sdk.version is missing from $globalJsonPath."
    }

    # Arcade restores tools.runtimes dotnet/x64 into the SDK root. Keep the
    # build host x64 even on ARM64 Windows; target RIDs remain independent.
    $localDotnetHost = Join-Path $repoRoot ".dotnet/dotnet.exe"
    if ((Test-Path $localDotnetHost) -and !(Test-X64DotNetHost $localDotnetHost)) {
        throw "The repository .dotnet host is not x64. Use a clean build checkout or preserve and move the incompatible SDK directory before building."
    }
    $sdkDirectory = Join-Path $repoRoot ".dotnet/sdk/$sdkVersion"
    if (!(Test-Path (Join-Path $sdkDirectory "Sdks/Microsoft.NET.Sdk/Sdk"))) {
        $sdkDirectory = $null
    }

    $dotnetCommand = Get-Command dotnet.exe -ErrorAction SilentlyContinue
    if ([string]::IsNullOrWhiteSpace($sdkDirectory) -and $null -ne $dotnetCommand -and
        (Test-X64DotNetHost $dotnetCommand.Source)) {
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
        # Arcade's wrapper defaults to a runtime-only install. An empty runtime
        # selects the SDK and omits -Runtime from the upstream installer call.
        & $dotnetInstall -version $sdkVersion -architecture x64 -runtime ''
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
    if (!(Test-X64DotNetHost (Join-Path $dotnetRoot "dotnet.exe"))) {
        throw "The selected SDK does not have the required x64 build host at $dotnetRoot."
    }
    $env:DOTNET_ROOT = $dotnetRoot
    # MSBuild.exe does not populate the CLI's DOTNET_HOST_PATH. SDK tasks
    # hosted out-of-process on .NET require the executable, not just DOTNET_ROOT
    # or MSBuildSDKsPath. Keep task hosting on the same validated x64 SDK host.
    $env:DOTNET_HOST_PATH = Join-Path $dotnetRoot "dotnet.exe"
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
    # Compiler/SDK changes require real compilation, not reuse of assemblies
    # previously produced with missing analyzers. Use Arcade's scoped Rebuild
    # action without deleting unrelated checkout outputs or running tests.
    $buildAction = @("-restore", "-build")
    if ($Rebuild) {
        $buildAction = @("-restore", "-rebuild")
    }

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

    # Arcade exits its process; keep each build isolated and preserve the
    # current host's execution policy rather than requesting an override.
    # The repository's inherited Framework-hosted compiler cannot load the
    # pinned SDK's native-image analyzers on this cross-architecture lane.
    # Use its .NET compiler through DOTNET_HOST_PATH; VS still owns C++/CLI.
    & $buildPowerShell -NoProfile -NonInteractive -File $buildCommand `
        $buildAction `
        -ci `
        -configuration $Configuration `
        -platform $platform `
        -projects $projectPath `
        -msbuildEngine $MSBuildEngine `
        $nativeToolsArgument `
        -excludeCIBinarylog `
        '-warnAsError:$false' `
        "/p:PerlCommand=$perlCommand" `
        $runtimeIdentifierArgument `
        $ijwHostArgument `
        /p:BuildWithNetFrameworkHostedCompiler=false `
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

if ($ValidateDesktopTransform) {
    # The preceding RID loop ends with ARM64. Select and verify an independent
    # x64 source-test closure explicitly; never run whichever output was last.
    $testProject = Join-Path $repoRoot "src/Microsoft.DotNet.Wpf/tests/UnitTests/PresentationCore.Tests/PresentationCore.Tests.csproj"
    $testDirectory = Join-Path $repoRoot "artifacts/bin/PresentationCore.Tests/x64/$Configuration/net10.0-windows/win-x64"
    $testAssembly = Join-Path $testDirectory "PresentationCore.Tests.dll"
    $x64IjwHost = Join-Path $packagesDirectory "microsoft.netcore.app.host.win-x64/$netCoreAppVersion/runtimes/win-x64/native/ijwhost.dll"
    if ($Rebuild -or !(Test-Path $testAssembly -PathType Leaf)) {
        Invoke-WpfProjectBuild $testProject "x64" "win-x64" $x64IjwHost
    }
    if (!(Test-Path $testAssembly -PathType Leaf)) {
        throw "The x64 source test assembly is missing: $testAssembly"
    }
    $expectedCore = Join-Path $repoRoot "artifacts/bin/PresentationCore/x64/$Configuration/net10.0/win-x64/PresentationCore.dll"
    $expectedForwarder = Join-Path $repoRoot "artifacts/bin/DirectWriteForwarder/x64/$Configuration/net10.0/DirectWriteForwarder.dll"
    # C++/CLI's native IJW host is a packaging asset, not a transitive managed
    # project reference. Stage this exact restored x64 input into our test output.
    Copy-Item $x64IjwHost (Join-Path $testDirectory "ijwhost.dll") -Force
    $propertyOutput = & $env:DOTNET_HOST_PATH msbuild $testProject -nologo `
        -getProperty:ArtifactsPackagingDir,GitHubTransportPackageName,WpfRuntimeIdentifier `
        "-p:Configuration=$Configuration" -p:Platform=x64 -p:RuntimeIdentifier=win-x64
    if ($LASTEXITCODE -ne 0) { throw "Unable to read the actual x64 source native payload location." }
    $properties = ($propertyOutput -join [Environment]::NewLine | ConvertFrom-Json).Properties
    if ($properties.WpfRuntimeIdentifier -ne 'win-x64') { throw 'The source test native payload selected a different RID.' }
    $nativeDirectory = Join-Path $properties.ArtifactsPackagingDir "$($properties.GitHubTransportPackageName)/runtimes/win-x64/native"
    $expectedInputs = @($expectedCore, $expectedForwarder, $x64IjwHost)
    $vcRuntimeName = if ($Configuration -eq 'Debug') { 'vcruntime140d.dll' } else { 'vcruntime140_cor3.dll' }
    foreach ($nativeName in @('D3DCompiler_47_cor3.dll', 'PresentationNative_cor3.dll',
            $vcRuntimeName, 'PenImc_cor3.dll', 'wpfgfx_cor3.dll')) {
        $native = Join-Path $nativeDirectory $nativeName
        if (!(Test-X64DotNetHost $native)) { throw "The source native payload is not an x64 PE image: $native" }
        $expectedInputs += $native
    }
    $closure = [ordered]@{}
    foreach ($expected in $expectedInputs) {
        $actual = Join-Path $testDirectory ([IO.Path]::GetFileName($expected))
        if (!(Test-Path $expected -PathType Leaf) -or !(Test-Path $actual -PathType Leaf) -or
            (Get-FileHash $expected -Algorithm SHA256).Hash -ne (Get-FileHash $actual -Algorithm SHA256).Hash) {
            throw "The source test closure does not match its explicit x64 build: $actual"
        }
        $closure[$actual] = (Get-FileHash $actual -Algorithm SHA256).Hash
    }
    if (!(Test-X64DotNetHost $expectedForwarder) -or !(Test-X64DotNetHost $x64IjwHost)) {
        throw "The source test native dependencies are not x64 PE images."
    }
    $evidence = Join-Path $repoRoot "artifacts/desktop-transform-source"
    New-Item -ItemType Directory -Path $evidence -Force | Out-Null
    $closure | ConvertTo-Json | Set-Content (Join-Path $evidence "x64-closure.json")
    $previousBackend = $env:LIBREWPF_TEST_MEDIA_BACKEND
    $previousExpectedCore = $env:LIBREWPF_DESKTOP_QUERY_EXPECTED_CORE
    try {
        $env:LIBREWPF_TEST_MEDIA_BACKEND = "WindowsMil"
        $env:LIBREWPF_DESKTOP_QUERY_EXPECTED_CORE = Join-Path $testDirectory "PresentationCore.dll"
        $start = [Diagnostics.ProcessStartInfo]::new($env:DOTNET_HOST_PATH)
        $start.UseShellExecute = $false
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        foreach ($argument in @($testAssembly, '--filter-class', 'System.Windows.NativeDesktopTransformQueryTests',
                '--minimum-expected-tests', '1', '--fail-skips', 'on', '--timeout', '60s', '--no-progress',
                '--results-directory', $evidence, '--report-trx')) {
            $start.ArgumentList.Add($argument)
        }
        $clock = [Diagnostics.Stopwatch]::StartNew()
        $process = [Diagnostics.Process]::Start($start)
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $timedOut = !$process.WaitForExit(60000)
        if ($timedOut) {
            $process.Kill($true)
            if (!$process.WaitForExit(5000)) { throw 'The owned native query process did not retire after its deadline.' }
        }
        $stdout.GetAwaiter().GetResult() | Set-Content (Join-Path $evidence "native-query.log")
        $stderr.GetAwaiter().GetResult() | Set-Content (Join-Path $evidence "native-query.stderr.log")
        $testExit = $process.ExitCode
        [ordered]@{ exitCode = $testExit; timedOut = $timedOut; elapsedSeconds = $clock.Elapsed.TotalSeconds;
            deadlineSeconds = 60; inputActions = 0; backend = 'WindowsMil'; host = $env:DOTNET_HOST_PATH;
            assembly = $testAssembly } | ConvertTo-Json | Set-Content (Join-Path $evidence "native-query-receipt.json")
        $process.Dispose()
        Get-Content (Join-Path $evidence "native-query.log")
        if ($timedOut -or $testExit -ne 0) { throw "The actual native HWND desktop query control failed ($testExit; timeout=$timedOut)." }
    }
    finally {
        $env:LIBREWPF_TEST_MEDIA_BACKEND = $previousBackend
        $env:LIBREWPF_DESKTOP_QUERY_EXPECTED_CORE = $previousExpectedCore
    }
}

Write-Host "Staged Windows managed runtime payload at $outputDirectory."
