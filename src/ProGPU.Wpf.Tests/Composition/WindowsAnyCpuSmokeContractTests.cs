using Xunit;

namespace ProGPU.Wpf.Tests.Composition;

public sealed class WindowsAnyCpuSmokeContractTests
{
    [Fact]
    public void SmokeKeepsTextWindowAliveAfterContentIsRendered()
    {
        var scriptPath = FindRepoPath("eng", "progpu-wpf-windows-anycpu-smoke.ps1");
        var script = File.ReadAllText(scriptPath);

        Assert.Contains("ContentRendered += OnContentRendered", script, StringComparison.Ordinal);
        Assert.Contains("ContentRendered -= OnContentRendered", script, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Threading.DispatcherTimer", script, StringComparison.Ordinal);
        Assert.Contains("Interval = TimeSpan.FromSeconds(1)", script, StringComparison.Ordinal);
        Assert.Contains("_renderLifetimeTimer.Start();", script, StringComparison.Ordinal);
        Assert.Contains("private void OnRenderLifetimeElapsed", script, StringComparison.Ordinal);
        Assert.Contains("_renderLifetimeTimer.Stop();", script, StringComparison.Ordinal);
        Assert.Contains("Application.Current.Shutdown(0);", script, StringComparison.Ordinal);
        Assert.DoesNotContain("DispatcherPriority.ApplicationIdle", script, StringComparison.Ordinal);
    }

    [Fact]
    public void SmokeWaitsForTheBuiltAnyCpuAppHostAndPropagatesItsExitCode()
    {
        var scriptPath = FindRepoPath("eng", "progpu-wpf-windows-anycpu-smoke.ps1");
        var script = File.ReadAllText(scriptPath);

        Assert.Contains("-Filter \"AnyCpuSmoke.exe\"", script, StringComparison.Ordinal);
        Assert.Contains("$appProcess = Start-Process", script, StringComparison.Ordinal);
        Assert.Contains("-FilePath $appHost.FullName", script, StringComparison.Ordinal);
        Assert.Contains("-RedirectStandardOutput $stdoutPath", script, StringComparison.Ordinal);
        Assert.Contains("-RedirectStandardError $stderrPath", script, StringComparison.Ordinal);
        Assert.Contains("Get-Content $stderrPath", script, StringComparison.Ordinal);
        Assert.Contains("$appProcess.WaitForExit(30000)", script, StringComparison.Ordinal);
        Assert.Contains("Stop-Process -Id $appProcess.Id -Force", script, StringComparison.Ordinal);
        Assert.Contains("$appProcess.ExitCode -ne 0", script, StringComparison.Ordinal);
        Assert.DoesNotContain("& $appHost.FullName", script, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet run --project", script, StringComparison.Ordinal);
    }

    [Fact]
    public void SmokeRequiresVisibleTextPixelsInsteadOfProcessSurvivalAlone()
    {
        var scriptPath = FindRepoPath("eng", "progpu-wpf-windows-anycpu-smoke.ps1");
        var script = File.ReadAllText(scriptPath);

        Assert.Contains("x:Name=\"SmokeText\"", script, StringComparison.Ordinal);
        Assert.Contains("new RenderTargetBitmap", script, StringComparison.Ordinal);
        Assert.Contains("bitmap.Render(SmokeText);", script, StringComparison.Ordinal);
        Assert.Contains("bitmap.CopyPixels", script, StringComparison.Ordinal);
        Assert.Contains("if (coveredPixels < 32)", script, StringComparison.Ordinal);
        Assert.Contains("LibreWPF rendered no visible text pixels", script, StringComparison.Ordinal);
        Assert.Contains("AssertTextRendered();", script, StringComparison.Ordinal);
    }

    [Fact]
    public void SmokeRequiresManagedFrameworkInTheWindowsDependencyFile()
    {
        var scriptPath = FindRepoPath("eng", "progpu-wpf-windows-anycpu-smoke.ps1");
        var script = File.ReadAllText(scriptPath);

        Assert.Contains("-Filter \"PresentationFramework.dll\"", script, StringComparison.Ordinal);
        Assert.Contains("build output is missing PresentationFramework.dll", script, StringComparison.Ordinal);
        Assert.Contains("-Filter \"AnyCpuSmoke.deps.json\"", script, StringComparison.Ordinal);
        Assert.Contains("lib/net10.0/PresentationFramework.dll", script, StringComparison.Ordinal);
        Assert.Contains("dependency file does not contain PresentationFramework.dll", script, StringComparison.Ordinal);
        Assert.Contains("-Filter \"DirectWriteForwarder.dll\"", script, StringComparison.Ordinal);
        Assert.Contains("build output is missing DirectWriteForwarder.dll", script, StringComparison.Ordinal);
        Assert.Contains("dependency file does not contain DirectWriteForwarder.dll", script, StringComparison.Ordinal);
        Assert.Contains("-Filter \"ijwhost.dll\"", script, StringComparison.Ordinal);
        Assert.Contains("build output is missing ijwhost.dll", script, StringComparison.Ordinal);
        Assert.Contains("dependency file does not contain ijwhost.dll", script, StringComparison.Ordinal);
    }

    [Fact]
    public void TransportPackagesWindowsBuiltPresentationCoreAsRidRuntimeAssets()
    {
        var projectPath = FindRepoPath(
            "packaging",
            "Microsoft.DotNet.Wpf.GitHub",
            "Microsoft.DotNet.Wpf.GitHub.ArchNeutral.csproj");
        var buildScriptPath = FindRepoPath("eng", "progpu-wpf-windows-managed-runtime.ps1");
        var auditPath = FindRepoPath("eng", "progpu-preview-package-audit.sh");
        var ciWorkflowPath = FindRepoPath(".github", "workflows", "progpu-wpf-sdk.yml");
        var releaseWorkflowPath = FindRepoPath(".github", "workflows", "progpu-wpf-release.yml");
        var presentationBuildTasksTargetsPath = FindRepoPath("eng", "WpfArcadeSdk", "tools", "Pbt.targets");

        var project = File.ReadAllText(projectPath);
        var buildScript = File.ReadAllText(buildScriptPath);
        var audit = File.ReadAllText(auditPath);
        var ciWorkflow = File.ReadAllText(ciWorkflowPath);
        var releaseWorkflow = File.ReadAllText(releaseWorkflowPath);
        var presentationBuildTasksTargets = File.ReadAllText(presentationBuildTasksTargetsPath);

        Assert.Contains("LibreWpfWindowsManagedPayloadDir", project, StringComparison.Ordinal);
        Assert.Contains("runtimes/win-x86/lib/net10.0", project, StringComparison.Ordinal);
        Assert.Contains("runtimes/win-x64/lib/net10.0", project, StringComparison.Ordinal);
        Assert.Contains("runtimes/win-arm64/lib/net10.0", project, StringComparison.Ordinal);
        Assert.Contains("PresentationCore.dll", buildScript, StringComparison.Ordinal);
        Assert.Contains("PresentationBuildTasks.csproj", buildScript, StringComparison.Ordinal);
        Assert.Contains("build.cmd", buildScript, StringComparison.Ordinal);
        Assert.Contains("[ValidateSet(\"vs\", \"dotnet\")]", buildScript, StringComparison.Ordinal);
        Assert.Contains("[string] $MSBuildEngine = \"vs\"", buildScript, StringComparison.Ordinal);
        Assert.Contains("-msbuildEngine $MSBuildEngine", buildScript, StringComparison.Ordinal);
        Assert.Contains("function Initialize-BuildSdk", buildScript, StringComparison.Ordinal);
        Assert.Contains("$globalJson.sdk.version", buildScript, StringComparison.Ordinal);
        Assert.Contains(".dotnet/sdk/$sdkVersion", buildScript, StringComparison.Ordinal);
        Assert.Contains("$effectiveSdkVersion", buildScript, StringComparison.Ordinal);
        Assert.Contains("Push-Location $repoRoot", buildScript, StringComparison.Ordinal);
        Assert.Contains("eng/common/dotnet-install.ps1", buildScript, StringComparison.Ordinal);
        Assert.Contains("$env:MSBuildSDKsPath = $sdkResolverPath", buildScript, StringComparison.Ordinal);
        Assert.Contains("$env:MSBuildEnableWorkloadResolver = \"false\"", buildScript, StringComparison.Ordinal);
        Assert.Contains("[switch] $NativeToolsOnMachine", buildScript, StringComparison.Ordinal);
        Assert.Contains("if ($NativeToolsOnMachine)", buildScript, StringComparison.Ordinal);
        Assert.Contains("-nativeToolsOnMachine", buildScript, StringComparison.Ordinal);
        Assert.Contains("$nativeToolsArgument", buildScript, StringComparison.Ordinal);
        Assert.Contains("\"win-x86\" = \"x86\"", buildScript, StringComparison.Ordinal);
        Assert.Contains("\"win-x64\" = \"x64\"", buildScript, StringComparison.Ordinal);
        Assert.Contains("\"win-arm64\" = \"arm64\"", buildScript, StringComparison.Ordinal);
        Assert.Contains("Get-Command perl.exe", buildScript, StringComparison.Ordinal);
        Assert.Contains("-ErrorAction SilentlyContinue", buildScript, StringComparison.Ordinal);
        Assert.Contains("$globalJson.'native-tools'.'strawberry-perl'", buildScript, StringComparison.Ordinal);
        Assert.Contains(".tools/native/bin/strawberry-perl", buildScript, StringComparison.Ordinal);
        Assert.Contains("/p:PerlCommand=$perlCommand", buildScript, StringComparison.Ordinal);
        Assert.Contains("/p:RuntimeIdentifier=$runtimeIdentifier", buildScript, StringComparison.Ordinal);
        Assert.Contains("Microsoft.NETCore.App.Host.$_", buildScript, StringComparison.Ordinal);
        Assert.Contains("/p:IjwHostSourcePath=$ijwHostSourcePath", buildScript, StringComparison.Ordinal);
        Assert.Contains("net10.0/$runtimeIdentifier/PresentationCore.dll", buildScript, StringComparison.Ordinal);
        Assert.Contains("artifacts/bin/DirectWriteForwarder", buildScript, StringComparison.Ordinal);
        Assert.Contains("DirectWriteForwarder.dll", buildScript, StringComparison.Ordinal);
        Assert.Contains("_LibreWpfWinX64DirectWriteForwarder", project, StringComparison.Ordinal);
        Assert.Contains("runtimes/${rid}/lib/${transport_target_framework}/DirectWriteForwarder.dll", audit, StringComparison.Ordinal);
        Assert.Contains("$runtimeIdentifier/native", buildScript, StringComparison.Ordinal);
        Assert.Contains("_LibreWpfWinX64IjwHost", project, StringComparison.Ordinal);
        Assert.Contains("runtimes/${rid}/native/ijwhost.dll", audit, StringComparison.Ordinal);
        Assert.Contains("RemoveProperties=\"RuntimeIdentifier\"", presentationBuildTasksTargets, StringComparison.Ordinal);
        Assert.Contains("require_entry_sha256 LibreWPF.Transport", audit, StringComparison.Ordinal);
        Assert.Contains("windows-managed-runtime:", ciWorkflow, StringComparison.Ordinal);
        Assert.Contains("needs: windows-managed-runtime", ciWorkflow, StringComparison.Ordinal);
        Assert.Contains("windows-managed-runtime:", releaseWorkflow, StringComparison.Ordinal);
        Assert.Contains("needs: windows-managed-runtime", releaseWorkflow, StringComparison.Ordinal);
    }

    private static string FindRepoPath(params string[] pathSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(pathSegments).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate repo file '{Path.Combine(pathSegments)}' from the test output directory.");
    }
}
