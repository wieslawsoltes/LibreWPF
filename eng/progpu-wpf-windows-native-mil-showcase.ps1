param(
    [string] $PackageDirectory = "",
    [string] $Version = "0.1.0-preview.65",
    [ValidateSet("x64", "arm64")]
    [string] $TargetArchitecture = "x64",
    [switch] $AllowEmulatedX64
)

$ErrorActionPreference = "Stop"

$osArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
$requiredArchitecture = if ($TargetArchitecture -eq "arm64") {
    [System.Runtime.InteropServices.Architecture]::Arm64
} else {
    [System.Runtime.InteropServices.Architecture]::X64
}
if ($osArchitecture -ne $requiredArchitecture -and
    !($TargetArchitecture -eq "x64" -and $AllowEmulatedX64 -and
      $osArchitecture -eq [System.Runtime.InteropServices.Architecture]::Arm64)) {
    throw "The Windows $TargetArchitecture native MIL gate requires a $requiredArchitecture Windows host; detected $osArchitecture."
}
$targetRid = "win-$TargetArchitecture"
$targetMachine = if ($TargetArchitecture -eq "arm64") { 0xAA64 } else { 0x8664 }

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($PackageDirectory)) {
    $PackageDirectory = Join-Path $repoRoot "artifacts/packages/Release/NonShipping"
}
$PackageDirectory = [System.IO.Path]::GetFullPath($PackageDirectory)

$sdkPackage = Join-Path $PackageDirectory "LibreWPF.Sdk.$Version.nupkg"
$transportPackage = Join-Path $PackageDirectory "LibreWPF.Transport.$Version.nupkg"
$bridgePackage = Join-Path $PackageDirectory "LibreWPF.ProGPU.$Version.nupkg"
$nativePackages = @(Get-ChildItem -LiteralPath $PackageDirectory -Filter "ProGPU.Backend.Native.*.nupkg" -File)
foreach ($package in @($sdkPackage, $transportPackage, $bridgePackage)) {
    if (!(Test-Path -LiteralPath $package -PathType Leaf)) {
        throw "Windows native MIL Showcase requires the package $package."
    }
}
if ($nativePackages.Count -ne 1) {
    throw "Windows native MIL Showcase requires exactly one ProGPU.Backend.Native package; found $($nativePackages.Count)."
}
$nativePackageVersion = $nativePackages[0].BaseName -replace '^ProGPU\.Backend\.Native\.', ''

Add-Type -AssemblyName System.IO.Compression.FileSystem

function Get-PackageEntryHash {
    param([string] $PackagePath, [string] $EntryPath)

    $archive = [System.IO.Compression.ZipFile]::OpenRead($PackagePath)
    try {
        $entry = $archive.GetEntry($EntryPath)
        if ($null -eq $entry) {
            throw "Package $PackagePath is missing $EntryPath."
        }
        $stream = $entry.Open()
        try {
            $sha = [System.Security.Cryptography.SHA256]::Create()
            try {
                return [System.BitConverter]::ToString($sha.ComputeHash($stream)).Replace("-", "")
            }
            finally {
                $sha.Dispose()
            }
        }
        finally {
            $stream.Dispose()
        }
    }
    finally {
        $archive.Dispose()
    }
}

function Assert-ExactPackageAsset {
    param(
        [string] $OutputPath,
        [string] $PackagePath,
        [string] $EntryPath
    )

    if (!(Test-Path -LiteralPath $OutputPath -PathType Leaf)) {
        throw "Windows native MIL Showcase output is missing $OutputPath."
    }
    $expected = Get-PackageEntryHash $PackagePath $EntryPath
    $actual = (Get-FileHash -LiteralPath $OutputPath -Algorithm SHA256).Hash
    if (![string]::Equals($expected, $actual, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Windows native MIL Showcase output $OutputPath does not match $EntryPath in $PackagePath."
    }
    Write-Host "Exact package asset: $OutputPath ($actual)"
}

function Install-ExactSdkPackage {
    param([string] $PackagePath, [string] $PackageVersion, [string] $PackagesRoot)

    # The NuGet MSBuild SDK resolver uses NUGET_PACKAGES before normal restore.
    # RestorePackagesPath alone does not isolate a mutable same-version SDK cache.
    $sdkDirectory = Join-Path $PackagesRoot "librewpf.sdk/$PackageVersion"
    New-Item -ItemType Directory -Path $sdkDirectory -Force | Out-Null
    [System.IO.Compression.ZipFile]::ExtractToDirectory($PackagePath, $sdkDirectory)

    $sdkTargets = Join-Path $sdkDirectory "targets/ProGPU.Wpf.Sdk.targets"
    $expected = Get-PackageEntryHash $PackagePath "targets/ProGPU.Wpf.Sdk.targets"
    $actual = (Get-FileHash -LiteralPath $sdkTargets -Algorithm SHA256).Hash
    if (![string]::Equals($expected, $actual, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "The isolated LibreWPF SDK targets do not match the exact package $PackagePath."
    }

    $cachedPackageName = "librewpf.sdk.$PackageVersion.nupkg"
    Copy-Item -LiteralPath $PackagePath -Destination (Join-Path $sdkDirectory $cachedPackageName)
    $sha = [System.Security.Cryptography.SHA512]::Create()
    try {
        $contentHash = [Convert]::ToBase64String(
            $sha.ComputeHash([System.IO.File]::ReadAllBytes($PackagePath)))
    }
    finally {
        $sha.Dispose()
    }
    [System.IO.File]::WriteAllText((Join-Path $sdkDirectory "$cachedPackageName.sha512"), $contentHash)
    $metadata = [pscustomobject]@{
        version = 2
        contentHash = $contentHash
        source = (Split-Path -Parent $PackagePath)
    } | ConvertTo-Json
    [System.IO.File]::WriteAllText((Join-Path $sdkDirectory ".nupkg.metadata"), $metadata)
    Write-Host "Isolated exact LibreWPF SDK: $sdkTargets ($actual)"
}

function Assert-RequestedRendererMode {
    param([string] $AppHost)

    $configurationPath = [System.IO.Path]::ChangeExtension($AppHost, ".runtimeconfig.json")
    if (!(Test-Path -LiteralPath $configurationPath -PathType Leaf)) {
        throw "Native MIL executable is missing $configurationPath."
    }
    $configuration = Get-Content -LiteralPath $configurationPath -Raw | ConvertFrom-Json
    $mode = $configuration.runtimeOptions.configProperties.'LibreWPF.RequestedRendererMode'
    if ($mode -ne "NativeMilWgpu") {
        throw "Native MIL executable $AppHost requested '$mode', not NativeMilWgpu."
    }
}

function Invoke-DotNet {
    param([string[]] $Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function Invoke-CapturedApplication {
    param(
        [string] $AppHost,
        [string] $StdoutPath,
        [string] $StderrPath,
        [int] $TimeoutMilliseconds
    )

    # Windows PowerShell 5 does not retain ExitCode from Start-Process -PassThru
    # after a manually timed WaitForExit. Own the .NET process handle directly.
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $AppHost
    $startInfo.WorkingDirectory = Split-Path -Parent $AppHost
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    $started = $false
    try {
        $started = $process.Start()
        if (!$started) {
            throw "Windows test executable did not start: $AppHost."
        }
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $timedOut = !$process.WaitForExit($TimeoutMilliseconds)
        if ($timedOut) {
            try { $process.Kill() } catch [System.InvalidOperationException] { }
        }
        $process.WaitForExit()
        $stdout = $stdoutTask.GetAwaiter().GetResult()
        $stderr = $stderrTask.GetAwaiter().GetResult()
        [System.IO.File]::WriteAllText($StdoutPath, $stdout)
        [System.IO.File]::WriteAllText($StderrPath, $stderr)
        if ($timedOut) {
            throw "Windows test executable $AppHost timed out after $TimeoutMilliseconds milliseconds."
        }
        return [pscustomobject]@{
            ExitCode = $process.ExitCode
            Stdout = $stdout
            Stderr = $stderr
        }
    }
    finally {
        if ($started -and !$process.HasExited) {
            try { $process.Kill() } catch [System.InvalidOperationException] { }
        }
        $process.Dispose()
    }
}

function Invoke-ShowcaseCheck {
    param(
        [string] $Name,
        [string] $ExpectedMarker,
        [string] $AppHost,
        [string] $OutputDirectory
    )

    $env:PROGPU_WPF_SHOWCASE_VALIDATE = if ($Name -eq "pre-display") { "1" } else { "0" }
    $env:PROGPU_WPF_SHOWCASE_RUN_VALIDATE = if ($Name -eq "displayed") { "1" } else { "0" }
    $env:PROGPU_WPF_SHOWCASE_LIVE_VALIDATE = "0"
    $stdoutPath = Join-Path $OutputDirectory "$Name-stdout.log"
    $stderrPath = Join-Path $OutputDirectory "$Name-stderr.log"

    $result = Invoke-CapturedApplication $AppHost $stdoutPath $stderrPath 180000
    Write-Host $result.Stdout
    if (![string]::IsNullOrWhiteSpace($result.Stderr)) {
        Write-Warning $result.Stderr
    }
    if ($result.ExitCode -ne 0) {
        throw "Windows native MIL Showcase $Name check exited $($result.ExitCode)."
    }
    if ($result.Stdout.IndexOf($ExpectedMarker, [System.StringComparison]::Ordinal) -lt 0) {
        throw "Windows native MIL Showcase $Name check did not print its success marker."
    }
}

function Invoke-TextLayoutCheck {
    param(
        [string] $Name,
        [string] $AppHost,
        [string] $OutputDirectory
    )

    $env:PROGPU_WPF_TEXT_LAYOUT_REPORT = "1"
    $env:PROGPU_WPF_TEXT_LAYOUT_DETAIL = "1"
    $env:PROGPU_WPF_TEXT_LAYOUT_EXIT_AFTER_REPORT = "1"
    $stdoutPath = Join-Path $OutputDirectory "$Name-text-layout-stdout.log"
    $stderrPath = Join-Path $OutputDirectory "$Name-text-layout-stderr.log"
    $result = Invoke-CapturedApplication $AppHost $stdoutPath $stderrPath 120000
    $stdout = $result.Stdout
    Write-Host "$Name text layout: $stdout"
    if (![string]::IsNullOrWhiteSpace($result.Stderr)) {
        Write-Warning $result.Stderr
    }
    if ($result.ExitCode -ne 0) {
        throw "Windows $Name text-layout check exited $($result.ExitCode)."
    }
    if ($Name -eq "ProGPU-native-MIL" -and
        $stdout.IndexOf("TEXT_RENDERER NativeMilWgpu", [System.StringComparison]::Ordinal) -lt 0) {
        throw "Windows $Name text-layout check did not prove its live native MIL host."
    }
    $caseMatches = [regex]::Matches($stdout,
        '(?m)^TEXT_CASE name=(?<name>[a-z0-9-]+) width=(?<width>-?[0-9.]+) height=(?<height>-?[0-9.]+) desiredWidth=(?<desiredWidth>-?[0-9.]+) desiredHeight=(?<desiredHeight>-?[0-9.]+) font=(?<font>-?[0-9.]+) lines=(?<lines>[0-9]+) tops=(?<tops>-?[0-9.,]+) heights=(?<heights>-?[0-9.,]+) starts=(?<starts>[0-9,]+) positions=(?<positions>[0-9]+) endOffset=(?<endOffset>[0-9]+) caretX=(?<caretX>-?[0-9.]+) caretY=(?<caretY>-?[0-9.]+) caretHeight=(?<caretHeight>-?[0-9.]+)\r?$')
    if ($caseMatches.Count -eq 0) {
        throw "Windows $Name text-layout check did not report any text cases."
    }
    $positionMatches = [regex]::Matches($stdout,
        '(?m)^TEXT_POSITION name=(?<name>[a-z0-9-]+) offset=(?<offset>[0-9]+) x=(?<x>-?[0-9.]+) y=(?<y>-?[0-9.]+) height=(?<height>-?[0-9.]+)\r?$')
    if ($positionMatches.Count -eq 0) {
        throw "Windows $Name text-layout check did not report insertion-position geometry."
    }
    $geometry = [regex]::Match($stdout,
        '(?m)^WINDOW_GEOMETRY outer=(?<outerWidth>[0-9]+)x(?<outerHeight>[0-9]+) client=(?<clientWidth>[0-9]+)x(?<clientHeight>[0-9]+) dpi=(?<dpi>[0-9]+) source=(?<sourceWidth>[0-9.]+)x(?<sourceHeight>[0-9.]+)\r?$')
    if (!$geometry.Success) {
        throw "Windows $Name text-layout check did not report its actual native window geometry."
    }
    $numberGlyphs = [regex]::Match($stdout,
        '(?m)^TEXT_GLYPHS name=number-symbols ids=(?<ids>[0-9,]+)\r?$')
    if (!$numberGlyphs.Success) {
        throw "Windows $Name text-layout check did not report actual number-symbol glyph IDs."
    }
    $culture = [System.Globalization.CultureInfo]::InvariantCulture
    $cases = @{}
    foreach ($caseMatch in $caseMatches) {
        $caseName = $caseMatch.Groups['name'].Value
        if ($cases.ContainsKey($caseName)) {
            throw "Windows $Name text-layout check reported duplicate case '$caseName'."
        }
        $cases[$caseName] = [pscustomobject]@{
            Width = [double]::Parse($caseMatch.Groups['width'].Value, $culture)
            Height = [double]::Parse($caseMatch.Groups['height'].Value, $culture)
            DesiredWidth = [double]::Parse($caseMatch.Groups['desiredWidth'].Value, $culture)
            DesiredHeight = [double]::Parse($caseMatch.Groups['desiredHeight'].Value, $culture)
            Font = [double]::Parse($caseMatch.Groups['font'].Value, $culture)
            Lines = [int]::Parse($caseMatch.Groups['lines'].Value, $culture)
            Tops = @($caseMatch.Groups['tops'].Value.Split(',') | ForEach-Object { [double]::Parse($_, $culture) })
            Heights = @($caseMatch.Groups['heights'].Value.Split(',') | ForEach-Object { [double]::Parse($_, $culture) })
            Starts = @($caseMatch.Groups['starts'].Value.Split(',') | ForEach-Object { [int]::Parse($_, $culture) })
            Positions = [int]::Parse($caseMatch.Groups['positions'].Value, $culture)
            EndOffset = [int]::Parse($caseMatch.Groups['endOffset'].Value, $culture)
            CaretX = [double]::Parse($caseMatch.Groups['caretX'].Value, $culture)
            CaretY = [double]::Parse($caseMatch.Groups['caretY'].Value, $culture)
            CaretHeight = [double]::Parse($caseMatch.Groups['caretHeight'].Value, $culture)
            PositionGeometry = @{}
        }
    }
    foreach ($positionMatch in $positionMatches) {
        $caseName = $positionMatch.Groups['name'].Value
        if (!$cases.ContainsKey($caseName)) {
            throw "Windows $Name text-layout position reported unknown case '$caseName'."
        }
        $offset = [int]::Parse($positionMatch.Groups['offset'].Value, $culture)
        if ($cases[$caseName].PositionGeometry.ContainsKey($offset)) {
            throw "Windows $Name text-layout case '$caseName' reported duplicate insertion offset $offset."
        }
        $cases[$caseName].PositionGeometry[$offset] = [pscustomobject]@{
            X = [double]::Parse($positionMatch.Groups['x'].Value, $culture)
            Y = [double]::Parse($positionMatch.Groups['y'].Value, $culture)
            Height = [double]::Parse($positionMatch.Groups['height'].Value, $culture)
        }
    }
    foreach ($caseName in $cases.Keys) {
        if ($cases[$caseName].PositionGeometry.Count -ne $cases[$caseName].Positions) {
            throw "Windows $Name text-layout case '$caseName' insertion geometry count does not match its reported position count."
        }
    }
    return [pscustomobject]@{
        Cases = $cases
        OuterWidth = [int]::Parse($geometry.Groups['outerWidth'].Value, $culture)
        OuterHeight = [int]::Parse($geometry.Groups['outerHeight'].Value, $culture)
        ClientWidth = [int]::Parse($geometry.Groups['clientWidth'].Value, $culture)
        ClientHeight = [int]::Parse($geometry.Groups['clientHeight'].Value, $culture)
        Dpi = [int]::Parse($geometry.Groups['dpi'].Value, $culture)
        SourceWidth = [double]::Parse($geometry.Groups['sourceWidth'].Value, $culture)
        SourceHeight = [double]::Parse($geometry.Groups['sourceHeight'].Value, $culture)
        NumberGlyphs = $numberGlyphs.Groups['ids'].Value
    }
}

function Assert-TextMetricNear {
    param([string] $Name, [double] $Native, [double] $Portable, [double] $Tolerance)
    if ([math]::Abs($Native - $Portable) -gt $Tolerance) {
        throw "Windows text-layout $Name differs: native=$Native portable=$Portable tolerance=$Tolerance."
    }
}

$smokeRoot = Join-Path ([System.IO.Path]::GetTempPath()) "librewpf-native-mil-$targetRid-$([guid]::NewGuid().ToString('N'))"
$artifactsRoot = Join-Path $smokeRoot "artifacts"
$packagesRoot = Join-Path $smokeRoot "nuget"
New-Item -ItemType Directory -Path $artifactsRoot, $packagesRoot -Force | Out-Null
$previousNugetPackages = $env:NUGET_PACKAGES
Install-ExactSdkPackage $sdkPackage $Version $packagesRoot
$env:NUGET_PACKAGES = $packagesRoot
try {
$showcaseNugetConfig = Join-Path $repoRoot "samples/ProGPU.Wpf.ShowcaseApp/NuGet.config"
$privateNugetConfig = Join-Path $smokeRoot "NuGet.config"
[xml] $privateConfig = Get-Content -LiteralPath $showcaseNugetConfig -Raw
$localFeedNode = $privateConfig.SelectSingleNode("/configuration/packageSources/add[@key='ProGPUWpfLocalArtifacts']")
$globalPackagesNode = $privateConfig.SelectSingleNode("/configuration/config/add[@key='globalPackagesFolder']")
if ($null -eq $localFeedNode -or $null -eq $globalPackagesNode) {
    throw "The Showcase NuGet config no longer provides its local feed and package-cache entries."
}
$localFeedNode.SetAttribute("value", $PackageDirectory)
$globalPackagesNode.SetAttribute("value", $packagesRoot)
$privateConfig.Save($privateNugetConfig)

$artifactsProperty = $artifactsRoot.Replace('\', '/') + '/'
$packagesProperty = $packagesRoot.Replace('\', '/')
$feedProperty = $PackageDirectory.Replace('\', '/')
$buildTasksProject = Join-Path $repoRoot "src/Microsoft.DotNet.Wpf/src/PresentationBuildTasks/PresentationBuildTasks.csproj"
$showcaseProject = Join-Path $repoRoot "samples/ProGPU.Wpf.ShowcaseApp/ProGPU.Wpf.ShowcaseApp.csproj"

Write-Host "Building unchanged SDK Showcase for $targetRid from $PackageDirectory."
Write-Host "Private test output: $smokeRoot"
Invoke-DotNet -Arguments @(
    "build", $buildTasksProject, "-f", "net10.0", "-c", "Release",
    "-p:ArtifactsDir=$artifactsProperty",
    "-p:RestorePackagesPath=$packagesProperty",
    "-p:RestoreAdditionalProjectSources=$feedProperty",
    "-p:RunNetFrameworkApiCompat=false", "-v:minimal"
)
Invoke-DotNet -Arguments @(
    "build", $showcaseProject, "-c", "Release", "-r", $targetRid,
    "-p:RestoreConfigFile=$privateNugetConfig",
    "-p:PlatformTarget=$TargetArchitecture",
    "-p:ArtifactsDir=$artifactsProperty",
    "-p:RestorePackagesPath=$packagesProperty",
    "-p:RestoreAdditionalProjectSources=$feedProperty",
    "-p:ProGpuWpfReferenceMode=Package",
    "-p:ProGpuPackageVersion=$nativePackageVersion",
    "-p:ProGpuWpfRendererMode=NativeMilWgpu",
    "-p:ProGpuWpfNativeMilHitTesting=true",
    "-p:RunNetFrameworkApiCompat=false", "-v:minimal"
)

$appDirectory = Join-Path $artifactsRoot "bin/ProGPU.Wpf.ShowcaseApp/Release/net10.0-windows"
$appHost = Join-Path $appDirectory "ProGPU.Wpf.ShowcaseApp.exe"
if (!(Test-Path -LiteralPath $appHost -PathType Leaf)) {
    throw "Windows native MIL Showcase build is missing the $TargetArchitecture apphost $appHost."
}
$appHostBytes = [System.IO.File]::ReadAllBytes($appHost)
$peOffset = [System.BitConverter]::ToInt32($appHostBytes, 60)
$machine = [System.BitConverter]::ToUInt16($appHostBytes, $peOffset + 4)
if ($machine -ne $targetMachine) {
    throw "Windows native MIL Showcase apphost is not $TargetArchitecture (PE machine $machine)."
}

Assert-ExactPackageAsset (Join-Path $appDirectory "PresentationCore.dll") $transportPackage "runtimes/$targetRid/lib/net10.0/PresentationCore.dll"
Assert-ExactPackageAsset (Join-Path $appDirectory "PresentationFramework.dll") $transportPackage "lib/net10.0/PresentationFramework.dll"
Assert-ExactPackageAsset (Join-Path $appDirectory "ProGPU.Wpf.dll") $bridgePackage "lib/net10.0/ProGPU.Wpf.dll"
Assert-ExactPackageAsset (Join-Path $appDirectory "progpu_native.dll") $nativePackages[0].FullName "runtimes/$targetRid/native/progpu_native.dll"
Assert-RequestedRendererMode $appHost

Invoke-ShowcaseCheck "pre-display" "ProGPU WPF Showcase validation succeeded." $appHost $smokeRoot
Invoke-ShowcaseCheck "displayed" "ProGPU WPF Showcase Application.Run validation succeeded." $appHost $smokeRoot

# Compile one source-only WPF fixture under both SDKs. The stock Windows WPF
# build is the geometry oracle; both processes must exercise their live text
# views, including the final hidden formatting-edge caret insertion position.
$textProject = Join-Path $repoRoot "samples/ProGPU.Wpf.TextLayoutParityApp/ProGPU.Wpf.TextLayoutParityApp.csproj"
$windowsTextProject = Join-Path $repoRoot "samples/ProGPU.Wpf.TextLayoutParityApp.Windows/ProGPU.Wpf.TextLayoutParityApp.Windows.csproj"
$windowsTextObj = Join-Path $smokeRoot "windows-text-obj"
$windowsTextBin = Join-Path $smokeRoot "windows-text-bin"
New-Item -ItemType Directory -Path $windowsTextObj, $windowsTextBin -Force | Out-Null
Invoke-DotNet -Arguments @(
    "build", $textProject, "-c", "Release", "-r", $targetRid,
    "-p:PlatformTarget=$TargetArchitecture",
    "-p:ArtifactsDir=$artifactsProperty",
    "-p:RestorePackagesPath=$packagesProperty",
    "-p:RestoreAdditionalProjectSources=$feedProperty",
    "-p:ProGpuWpfReferenceMode=Package",
    "-p:ProGpuPackageVersion=$nativePackageVersion",
    "-p:ProGpuWpfRendererMode=NativeMilWgpu",
    "-p:ProGpuWpfNativeMilHitTesting=true",
    "-p:RunNetFrameworkApiCompat=false", "-v:minimal"
)
$textDirectory = Join-Path $artifactsRoot "bin/ProGPU.Wpf.TextLayoutParityApp/Release/net10.0-windows"
$textAppHost = Join-Path $textDirectory "ProGPU.Wpf.TextLayoutParityApp.exe"
Assert-ExactPackageAsset (Join-Path $textDirectory "PresentationCore.dll") $transportPackage "runtimes/$targetRid/lib/net10.0/PresentationCore.dll"
Assert-ExactPackageAsset (Join-Path $textDirectory "PresentationFramework.dll") $transportPackage "lib/net10.0/PresentationFramework.dll"
Assert-ExactPackageAsset (Join-Path $textDirectory "progpu_native.dll") $nativePackages[0].FullName "runtimes/$targetRid/native/progpu_native.dll"
Assert-RequestedRendererMode $textAppHost
Invoke-DotNet -Arguments @(
    "build", $windowsTextProject, "-c", "Release", "-r", $targetRid,
    "-p:PlatformTarget=$TargetArchitecture",
    "-p:BaseIntermediateOutputPath=$($windowsTextObj.Replace('\', '/'))/",
    "-p:OutputPath=$($windowsTextBin.Replace('\', '/'))/",
    "-p:AppendTargetFrameworkToOutputPath=false",
    "-p:AppendRuntimeIdentifierToOutputPath=false", "-v:minimal"
)
$windowsTextAppHost = Join-Path $windowsTextBin "ProGPU.Wpf.TextLayoutParityApp.Windows.exe"
if (!(Test-Path -LiteralPath $windowsTextAppHost -PathType Leaf)) {
    throw "Windows native WPF text-layout build is missing $windowsTextAppHost."
}
$nativeLayout = Invoke-TextLayoutCheck "native-WPF" $windowsTextAppHost $smokeRoot
$portableLayout = Invoke-TextLayoutCheck "ProGPU-native-MIL" $textAppHost $smokeRoot
$expectedNumberGlyphs = "257,258,16,259,260,261,18,262,263,266,4,264,265,16,256,257,258,18,259,260,266"
if ($nativeLayout.NumberGlyphs -cne $expectedNumberGlyphs) {
    throw "Windows stock WPF number-symbol oracle changed: $($nativeLayout.NumberGlyphs)."
}
if ($nativeLayout.NumberGlyphs -cne $portableLayout.NumberGlyphs) {
    throw "Windows number-symbol glyphs differ: native=$($nativeLayout.NumberGlyphs) portable=$($portableLayout.NumberGlyphs)."
}
$expectedTextCases = @(
    "wrapped-composite",
    "mixed-runs",
    "overflow-token",
    "tabs-whitespace",
    "explicit-line-height",
    "bidirectional",
    "national-digits",
    "number-symbols",
    "contextual-digits"
)
if ($nativeLayout.Cases.Count -ne $expectedTextCases.Count -or
    $portableLayout.Cases.Count -ne $expectedTextCases.Count) {
    throw "Windows native-WPF and ProGPU text-layout case counts differ from the required matrix."
}
foreach ($caseName in $expectedTextCases) {
    if (!$nativeLayout.Cases.ContainsKey($caseName) -or
        !$portableLayout.Cases.ContainsKey($caseName)) {
        throw "Windows native-WPF or ProGPU text-layout output is missing required case '$caseName'."
    }

    $nativeCase = $nativeLayout.Cases[$caseName]
    $portableCase = $portableLayout.Cases[$caseName]
    if ($nativeCase.Lines -lt 1 -or
        $portableCase.Lines -ne $nativeCase.Lines -or
        $nativeCase.Tops.Count -ne $nativeCase.Lines -or
        $portableCase.Tops.Count -ne $portableCase.Lines -or
        $nativeCase.Heights.Count -ne $nativeCase.Lines -or
        $portableCase.Heights.Count -ne $portableCase.Lines -or
        $portableCase.Positions -ne $nativeCase.Positions -or
        $portableCase.EndOffset -ne $nativeCase.EndOffset -or
        ($nativeCase.Starts -join ',') -ne ($portableCase.Starts -join ',')) {
        throw "Windows native-WPF and ProGPU text-layout line geometry differs for '$caseName'."
    }
    Assert-TextMetricNear "$caseName content width" $nativeCase.Width $portableCase.Width 0.01
    Assert-TextMetricNear "$caseName content height" $nativeCase.Height $portableCase.Height 0.10
    Assert-TextMetricNear "$caseName desired width" $nativeCase.DesiredWidth $portableCase.DesiredWidth 0.01
    Assert-TextMetricNear "$caseName desired height" $nativeCase.DesiredHeight $portableCase.DesiredHeight 0.10
    Assert-TextMetricNear "$caseName font size" $nativeCase.Font $portableCase.Font 0.001
    Assert-TextMetricNear "$caseName final caret X" $nativeCase.CaretX $portableCase.CaretX 0.10
    Assert-TextMetricNear "$caseName final caret Y" $nativeCase.CaretY $portableCase.CaretY 0.10
    Assert-TextMetricNear "$caseName final caret height" $nativeCase.CaretHeight $portableCase.CaretHeight 0.10
    for ($i = 0; $i -lt $nativeCase.Tops.Count; $i++) {
        Assert-TextMetricNear "$caseName line $i top" $nativeCase.Tops[$i] $portableCase.Tops[$i] 0.10
        Assert-TextMetricNear "$caseName line $i height" $nativeCase.Heights[$i] $portableCase.Heights[$i] 0.10
    }
    foreach ($offset in $nativeCase.PositionGeometry.Keys) {
        if (!$portableCase.PositionGeometry.ContainsKey($offset)) {
            throw "Windows ProGPU text-layout case '$caseName' is missing native-WPF insertion offset $offset."
        }
        $nativePosition = $nativeCase.PositionGeometry[$offset]
        $portablePosition = $portableCase.PositionGeometry[$offset]
        Assert-TextMetricNear "$caseName insertion $offset X" $nativePosition.X $portablePosition.X 0.10
        Assert-TextMetricNear "$caseName insertion $offset Y" $nativePosition.Y $portablePosition.Y 0.10
        Assert-TextMetricNear "$caseName insertion $offset height" $nativePosition.Height $portablePosition.Height 0.10
    }
}
if ($nativeLayout.Dpi -lt 96 -or $portableLayout.Dpi -lt 96 -or
    $nativeLayout.Dpi -ne $portableLayout.Dpi) {
    throw "Windows native-WPF and ProGPU window DPI differ: native=$($nativeLayout.Dpi) portable=$($portableLayout.Dpi)."
}
Assert-TextMetricNear "declared Window width" 780 $nativeLayout.SourceWidth 0.001
Assert-TextMetricNear "declared Window height" 720 $nativeLayout.SourceHeight 0.001
Assert-TextMetricNear "outer window width" $nativeLayout.OuterWidth $portableLayout.OuterWidth 1
Assert-TextMetricNear "outer window height" $nativeLayout.OuterHeight $portableLayout.OuterHeight 1
Assert-TextMetricNear "client window width" $nativeLayout.ClientWidth $portableLayout.ClientWidth 1
Assert-TextMetricNear "client window height" $nativeLayout.ClientHeight $portableLayout.ClientHeight 1
Assert-TextMetricNear "source Window width" $nativeLayout.SourceWidth $portableLayout.SourceWidth 0.001
Assert-TextMetricNear "source Window height" $nativeLayout.SourceHeight $portableLayout.SourceHeight 0.001
Write-Host "Windows $TargetArchitecture package-only native MIL Showcase and same-source text-layout matrix checks succeeded."
}
finally {
    if ([string]::IsNullOrEmpty($previousNugetPackages)) {
        Remove-Item Env:NUGET_PACKAGES -ErrorAction SilentlyContinue
    } else {
        $env:NUGET_PACKAGES = $previousNugetPackages
    }
}
