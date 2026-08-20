// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection;

namespace System.Windows.Controls;

public sealed class DefinitionBaseTests
{
    [Fact]
    public void DetachedDefinitions_CanSetSizingProperties()
    {
        RowDefinition row = new()
        {
            Height = GridLength.Auto,
            MinHeight = 1,
            MaxHeight = 100
        };
        ColumnDefinition column = new()
        {
            Width = new GridLength(2, GridUnitType.Star),
            MinWidth = 2,
            MaxWidth = 200
        };

        Assert.True(row.Height.IsAuto);
        Assert.Equal(1, row.MinHeight);
        Assert.Equal(100, row.MaxHeight);
        Assert.Equal(new GridLength(2, GridUnitType.Star), column.Width);
        Assert.Equal(2, column.MinWidth);
        Assert.Equal(200, column.MaxWidth);
    }

    [Fact]
    public void Constructor_IsNotInlineable()
    {
        ConstructorInfo constructor = typeof(DefinitionBase).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(bool)],
            modifiers: null)!;

        Assert.True((constructor.MethodImplementationFlags & MethodImplAttributes.NoInlining) != 0);
    }
}
