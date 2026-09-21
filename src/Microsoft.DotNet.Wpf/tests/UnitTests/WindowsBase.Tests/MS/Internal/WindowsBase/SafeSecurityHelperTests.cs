// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection;

namespace MS.Internal.WindowsBase.Tests;

public class SafeSecurityHelperTests
{
    [Fact]
    public void GetLoadedAssemblyBySimpleName_IgnoresFrameworkIdentity()
    {
        Assembly currentAssembly = Assembly.GetExecutingAssembly();
        AssemblyName requestedIdentity = new(currentAssembly.GetName().Name)
        {
            Version = new Version(999, 0, 0, 0),
            CultureName = string.Empty,
        };
        requestedIdentity.SetPublicKeyToken(Convert.FromHexString("31BF3856AD364E35"));

        SafeSecurityHelper.GetLoadedAssembly(requestedIdentity).Should().BeNull();
        SafeSecurityHelper.GetLoadedAssemblyBySimpleName(requestedIdentity.Name).Should().BeSameAs(currentAssembly);
    }

    [Fact]
    public void GetLoadedAssemblyBySimpleName_UnknownName_ReturnsNull()
    {
        SafeSecurityHelper.GetLoadedAssemblyBySimpleName($"Missing.{Guid.NewGuid():N}").Should().BeNull();
    }
}
