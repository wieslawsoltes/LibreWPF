// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Windows.Media;

[Collection("Sequential")]
public class PortableMediaContextRenderServiceTests
{
    [Fact]
    public void ExplicitRegistrationDeliversInvalidationAndDelayOnEveryPlatform()
    {
        PortableMediaContextRenderService.IsEnabled.Should().BeFalse();
        var source = new object();
        int calls = 0;
        object? reportedSource = null;
        TimeSpan reportedDelay = default;
        IDisposable registration = PortableMediaContextRenderService.Register((value, delay) =>
        {
            calls++;
            reportedSource = value;
            reportedDelay = delay;
        });
        try
        {
            PortableMediaContextRenderService.IsEnabled.Should().BeTrue();
            PortableMediaContextRenderService.RequestRender(source, TimeSpan.FromMilliseconds(16));
            calls.Should().Be(1);
            reportedSource.Should().BeSameAs(source);
            reportedDelay.Should().Be(TimeSpan.FromMilliseconds(16));
            PortableMediaContextRenderService.RequestRender(source, TimeSpan.FromMilliseconds(-1));
            calls.Should().Be(2);
            reportedDelay.Should().Be(TimeSpan.Zero);
        }
        finally
        {
            registration.Dispose();
        }

        registration.Dispose();
        PortableMediaContextRenderService.IsEnabled.Should().BeFalse();
        PortableMediaContextRenderService.RequestRender(source);
        calls.Should().Be(2);
    }

    [Fact]
    public void DisposingOneHostPreservesOtherHostWakeups()
    {
        int firstCalls = 0, secondCalls = 0;
        using IDisposable first = PortableMediaContextRenderService.Register(() => firstCalls++);
        using IDisposable second = PortableMediaContextRenderService.Register(() => secondCalls++);
        PortableMediaContextRenderService.RequestRender();
        first.Dispose();
        PortableMediaContextRenderService.IsEnabled.Should().BeTrue();
        PortableMediaContextRenderService.RequestRender();
        firstCalls.Should().Be(1);
        secondCalls.Should().Be(2);
    }
}
