// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection;

namespace MS.Internal.Resources;

public class ResourceManagerWrapperTests
{
    [Fact]
    public void TryUpdateAssembly_SameIdentityStaticAssembly_PreservesDesignerReload()
    {
        Assembly currentAssembly = typeof(ResourceManagerWrapperTests).Assembly;
        Assembly reloadedAssembly = Assembly.Load(File.ReadAllBytes(currentAssembly.Location));
        var wrapper = new ResourceManagerWrapper(currentAssembly);

        reloadedAssembly.IsDynamic.Should().BeFalse();
        reloadedAssembly.FullName.Should().Be(currentAssembly.FullName);
        wrapper.TryUpdateAssembly(reloadedAssembly).Should().BeTrue();
        wrapper.Assembly.Should().BeSameAs(reloadedAssembly);
    }
}
