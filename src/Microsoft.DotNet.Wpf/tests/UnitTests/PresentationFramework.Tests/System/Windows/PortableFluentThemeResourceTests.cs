// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Windows;

public class PortableFluentThemeResourceTests
{
    [Theory]
    [InlineData("/PresentationFramework.Fluent;component/Themes/Fluent.xaml", true)]
    [InlineData("/presentationframework.fluent;component/themes/Fluent.Light.xaml", true)]
    [InlineData("/PresentationFramework.Aero;component/Themes/Aero.NormalColor.xaml", false)]
    [InlineData("/Other.Assembly;component/Themes/Fluent.xaml", false)]
    public void FluentThemeRecognitionIncludesRelativeComponentUris(string uri, bool expected)
    {
        ThemeManager.IsFluentThemeResourceDictionary(new Uri(uri, UriKind.RelativeOrAbsolute))
            .Should().Be(expected);
    }
}
