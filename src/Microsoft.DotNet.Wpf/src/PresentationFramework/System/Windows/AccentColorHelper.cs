// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Windows.Media;
using MS.Internal.WindowsRuntime.Windows.UI.ViewManagement;


namespace System.Windows;

internal static class AccentColorHelper
{

    #region Internal Methods

    internal static Color GetAccentColor(UISettingsRCW.UIColorType uiColorType = UISettingsRCW.UIColorType.Accent)
    {
        if (!OperatingSystem.IsWindows())
        {
            return GetPortableAccentColor(uiColorType);
        }

        if (UISettings.TryGetColorValue(uiColorType, out Color color))
        {
            return color;
        }

        return _defaultAccentColor;
    }

    private static Color GetPortableAccentColor(UISettingsRCW.UIColorType uiColorType)
    {
        return uiColorType switch
        {
            UISettingsRCW.UIColorType.AccentLight1 => Color.FromRgb(0x42, 0x9C, 0xE3),
            UISettingsRCW.UIColorType.AccentLight2 => Color.FromRgb(0x76, 0xB9, 0xED),
            UISettingsRCW.UIColorType.AccentLight3 => Color.FromRgb(0xA6, 0xD8, 0xFF),
            UISettingsRCW.UIColorType.AccentDark1 => Color.FromRgb(0x00, 0x67, 0xB9),
            UISettingsRCW.UIColorType.AccentDark2 => Color.FromRgb(0x00, 0x5A, 0x9E),
            UISettingsRCW.UIColorType.AccentDark3 => Color.FromRgb(0x00, 0x42, 0x75),
            _ => _defaultAccentColor
        };
    }

    #endregion

    #region Internal Properties

    internal static Color SystemAccentColor
    {
        get
        {
            return GetAccentColor(UISettingsRCW.UIColorType.Accent);
        }
    }

    private static UISettings UISettings
    {
        get
        {
            if (_uiSettings == null)
            {
                _uiSettings = new UISettings();
            }

            return _uiSettings;
        }
    }

    #endregion

    #region Private Fields

    private static readonly Color _defaultAccentColor = Color.FromArgb(0xff, 0x00, 0x78, 0xd4);

    private static UISettings _uiSettings = null;

    #endregion
}
