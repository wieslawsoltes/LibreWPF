using System.IO;
using System.Windows.Media.ProGPU;
using Xunit;

namespace ProGPU.Wpf.Tests;

public sealed partial class ProGpuWpfWindowHostTests
{
    private const string NativeModalArgument = "--libre-native-modal-sessions";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void StartupWithoutModalArgumentKeepsExistingDefault(bool isMacOS)
    {
        Assert.False(ProGpuWpfStartupOptions.ParseArguments(
            ["--application-option", "value"], isMacOS, nativePopupsDisabled: true).EnableNativeModalSessions);
        Assert.False(default(ProGpuWpfStartupOptions).EnableNativeModalSessions);
    }

    [Theory]
    [InlineData("--libre-native-modal-sessions=true")]
    [InlineData("--libre-native-modal-sessions=false")]
    [InlineData("--libre-native-modal-sessions=automatic")]
    [InlineData("--libre-native-modal-session")]
    [InlineData("--libre-native-modal-other")]
    public void StartupRejectsUnknownNativeModalArgument(string argument)
        => Assert.Throws<ArgumentException>(() => ProGpuWpfStartupOptions.ParseArguments(
            [argument], isMacOS: true, nativePopupsDisabled: false));

    [Fact]
    public void StartupRejectsDuplicateModalSelectionBeforePublishingOptions()
        => Assert.Throws<ArgumentException>(() => ProGpuWpfStartupOptions.ParseArguments(
            [NativeModalArgument, "unrelated", NativeModalArgument], true, false));

    [Fact]
    public void StartupRejectsUnsupportedPlatformAndDisabledNativePopups()
    {
        Assert.Throws<PlatformNotSupportedException>(() => ProGpuWpfStartupOptions.ParseArguments(
            [NativeModalArgument], isMacOS: false, nativePopupsDisabled: false));
        Assert.Throws<InvalidOperationException>(() => ProGpuWpfStartupOptions.ParseArguments(
            [NativeModalArgument], isMacOS: true, nativePopupsDisabled: true));
    }

    [Fact]
    public void StartupDoesNotConsumeArgumentsOrReinterpretLateApplicationData()
    {
        string[] arguments = ["application-data", NativeModalArgument, "--", NativeModalArgument];
        string[] original = (string[])arguments.Clone();
        var startup = ProGpuWpfStartupOptions.ParseArguments(arguments, true, false);
        Assert.True(startup.EnableNativeModalSessions);
        Assert.Equal(original, arguments);
        arguments[1] = "changed-after-startup";
        Assert.True(startup.EnableNativeModalSessions);
        Assert.False(ProGpuWpfStartupOptions.ParseArguments(
            ["--", NativeModalArgument], false, true).EnableNativeModalSessions);
    }

    [Theory]
    [InlineData(ProGpuWpfRendererMode.ManagedPortable)]
    [InlineData(ProGpuWpfRendererMode.NativeMilWgpu)]
    public void StartupSelectionSurvivesActualSourceWindowOptionCopy(ProGpuWpfRendererMode renderer)
    {
        var startup = ProGpuWpfStartupOptions.ParseArguments([NativeModalArgument], true, false);
        var fallback = new ProGpuWpfWindowOptions
        {
            RendererMode = renderer,
            EnableNativeModalSessions = startup.EnableNativeModalSessions,
            EnableNativeMilHitTesting = renderer == ProGpuWpfRendererMode.NativeMilWgpu
        };
        var first = WpfPortableWindowActivation.CreateHostOptions(new object(), fallback);
        var second = WpfPortableWindowActivation.CreateHostOptions(new object(), fallback);
        Assert.NotSame(first, second);
        Assert.True(first.EnableNativeModalSessions);
        Assert.True(second.EnableNativeModalSessions);
        Assert.Equal(renderer, first.RendererMode);
        Assert.Equal(fallback.EnableNativeMilHitTesting, first.EnableNativeMilHitTesting);
        first.EnableNativeModalSessions = false;
        Assert.True(second.EnableNativeModalSessions);
        Assert.True(fallback.EnableNativeModalSessions);
    }

    [Fact]
    public void ActualSdkBootstrapCapturesStartupBeforeMediaAndPropagatesBothSourceHosts()
    {
        string source = File.ReadAllText(FindRepoPath("packaging", "ProGPU.Wpf.Sdk", "targets", "ProGPU.Wpf.Sdk.PortableBootstrap.cs"));
        int parse = source.IndexOf("ProGpuWpfStartupOptions.ParseArguments(", StringComparison.Ordinal);
        Assert.True(parse >= 0);
        Assert.True(parse < source.IndexOf("ProGpuWpfNativeMediaServices.Initialize()", StringComparison.Ordinal));
        Assert.True(parse < source.IndexOf("ProGpuPlatform.Register(enableNativeModalSessions: true)", StringComparison.Ordinal));
        Assert.Contains("global::System.MemoryExtensions.AsSpan(global::System.Environment.GetCommandLineArgs(), 1)", source);
        Assert.Contains("EnableNativeModalSessions = startup.EnableNativeModalSessions", source);
        Assert.Contains("global::LibreWinForms.ProGPU.ProGpuPlatform.Register();", source);
        Assert.Contains("Explicit native modal startup requires the source-built typed WPF activation service.", source);
        Assert.Equal(1, source.Split("ProGpuWpfStartupOptions.ParseArguments(", StringSplitOptions.None).Length - 1);
        // Both sample applications use this exact packaged SDK initializer, not
        // an OnStartup replacement after the first source host has been chosen.
        foreach (string application in new[] { "ProGPU.Wpf.ShowcaseApp", "ProGPU.Wpf.SciChartApp" })
        {
            string project = File.ReadAllText(FindRepoPath("samples", application, application + ".csproj"));
            Assert.Contains("Sdk=\"LibreWPF.Sdk/", project);
        }
    }
}
