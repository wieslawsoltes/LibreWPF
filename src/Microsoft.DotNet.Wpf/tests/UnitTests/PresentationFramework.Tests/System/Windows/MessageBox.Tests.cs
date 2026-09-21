// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.Reflection;
using System.Windows.Interop;

namespace System.Windows;

public sealed class MessageBoxTests
{
    [Fact]
    public void PortableDialog_ButtonConfigurations_PreserveAllWpfResults()
    {
        Assert.Equal(
            new[] { MessageBoxResult.OK },
            GetPortableButtonResults(MessageBoxButton.OK));
        Assert.Equal(
            new[] { MessageBoxResult.OK, MessageBoxResult.Cancel },
            GetPortableButtonResults(MessageBoxButton.OKCancel));
        Assert.Equal(
            new[] { MessageBoxResult.Abort, MessageBoxResult.Retry, MessageBoxResult.Ignore },
            GetPortableButtonResults(MessageBoxButton.AbortRetryIgnore));
        Assert.Equal(
            new[] { MessageBoxResult.Yes, MessageBoxResult.No, MessageBoxResult.Cancel },
            GetPortableButtonResults(MessageBoxButton.YesNoCancel));
        Assert.Equal(
            new[] { MessageBoxResult.Yes, MessageBoxResult.No },
            GetPortableButtonResults(MessageBoxButton.YesNo));
        Assert.Equal(
            new[] { MessageBoxResult.Retry, MessageBoxResult.Cancel },
            GetPortableButtonResults(MessageBoxButton.RetryCancel));
        Assert.Equal(
            new[] { MessageBoxResult.Cancel, MessageBoxResult.TryAgain, MessageBoxResult.Continue },
            GetPortableButtonResults(MessageBoxButton.CancelTryContinue));
    }

    [Fact]
    public void PortableDialog_DefaultResult_MustBelongToConfiguration()
    {
        MessageBoxResult[] results = GetPortableButtonResults(MessageBoxButton.YesNoCancel);

        Assert.Equal(MessageBoxResult.No, GetPortableDefaultResult(results, MessageBoxResult.No));
        Assert.Equal(MessageBoxResult.Yes, GetPortableDefaultResult(results, MessageBoxResult.None));
        Assert.Equal(MessageBoxResult.Yes, GetPortableDefaultResult(results, MessageBoxResult.Retry));
    }

    [Fact]
    public void PortableDialog_Centering_UsesOwnerCoordinates()
    {
        Type dialogType = GetPortableDialogType();
        MethodInfo method = dialogType.GetMethod(
            "GetCenteredPosition",
            BindingFlags.Static | BindingFlags.NonPublic)!;

        var position = (Point)method.Invoke(
            null,
            new object[] { 132d, 64d, 2560d, 1600d, 520d, 200d })!;

        Assert.Equal(new Point(1152d, 764d), position);
    }

    [Fact]
    public void PortableService_ExplicitRegistration_IsAnOverride()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Type serviceType = typeof(MessageBox).Assembly.GetType(
            "System.Windows.PortableMessageBoxService",
            throwOnError: true)!;
        MethodInfo register = serviceType.GetMethod(
            "Register",
            BindingFlags.Static | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(Func<object, object>) },
            modifiers: null)!;
        MethodInfo tryShowOverride = serviceType.GetMethod(
            "TryShowOverride",
            BindingFlags.Static | BindingFlags.NonPublic)!;

        using IDisposable registration = (IDisposable)register.Invoke(
            null,
            new object[] { (Func<object, object>)(_ => MessageBoxResult.No) })!;
        object[] arguments =
        {
            null!,
            "message",
            "caption",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.Yes,
            MessageBoxOptions.None,
            MessageBoxResult.None,
        };

        Assert.True((bool)tryShowOverride.Invoke(null, arguments)!);
        Assert.Equal(MessageBoxResult.No, arguments[^1]);
    }

    // MessageBoxButton range is from 0x00000000 to 0x00000006
    // Values outside are illegal.
    [WpfTheory]
    [InlineData(0xFFFFFFFF)]
    [InlineData(0x00000007)]
    public void Show_InvalidMessageBoxButton_ThrowsInvalidEnumArgumentException(uint invalidButton)
    {
        InvalidEnumArgumentException exception = Assert.Throws<InvalidEnumArgumentException>(() =>
            MessageBox.Show("Test Message", "Test Caption", (MessageBoxButton)invalidButton));
        Assert.Contains("button", exception.Message);
    }

    // MessageBoxButton range is from 0x00000000 to 0x00000006
    // Values outside are illegal.
    [WpfTheory]
    [InlineData(0xFFFFFFFF)]
    [InlineData(0x00000007)]
    public void Show_WithOwner_InvalidMessageBoxButton_ThrowsInvalidEnumArgumentException(uint invalidButton)
    {
        InvalidEnumArgumentException exception = Assert.Throws<InvalidEnumArgumentException>(() =>
            MessageBox.Show(new Window(), "Test Message", "Test Caption", (MessageBoxButton)invalidButton));
        Assert.Contains("button", exception.Message);
    }

    // MessageBoxResult range is from 0 to 11 with 8 and 9 not used.
    // Values outside are illegal as well as values 8 and 9.
    [WpfTheory]
    [InlineData(-1)]    // Just outside enum range
    [InlineData(8)]     // Not defined - hole in enum range
    [InlineData(9)]     // Not defined - hole in enum range
    [InlineData(12)]    // Just outside enum range
    public void Show_InvalidMessageBoxResult_ThrowsInvalidEnumArgumentException(int invalidResult)
    {
        InvalidEnumArgumentException exception = Assert.Throws<InvalidEnumArgumentException>(() =>
            MessageBox.Show("Test Message", "Test Caption", MessageBoxButton.OK, MessageBoxImage.None, (MessageBoxResult)invalidResult));
        Assert.Contains("defaultResult", exception.Message);
    }

    // MessageBoxResult range is from 0 to 11 with 8 and 9 not used.
    // Values outside are illegal as well as values 8 and 9.
    [WpfTheory]
    [InlineData(-1)]    // Just outside enum range
    [InlineData(8)]     // Not defined - hole in enum range
    [InlineData(9)]     // Not defined - hole in enum range
    [InlineData(12)]    // Just outside enum range
    public void Show_WithOwner_InvalidMessageBoxResult_ThrowsInvalidEnumArgumentException(int invalidResult)
    {
        InvalidEnumArgumentException exception = Assert.Throws<InvalidEnumArgumentException>(() =>
            MessageBox.Show(new Window(), "Test Message", "Test Caption", MessageBoxButton.OK, MessageBoxImage.None, (MessageBoxResult)invalidResult));
        Assert.Contains("defaultResult", exception.Message);
    }

    // MessageBoxImage values are 0x00000000, 0x00000010, 0x00000020, 0x00000030, 0x00000040
    // Any other value is illegal.
    [WpfTheory]
    [InlineData(0xFFFFFFFF)]
    [InlineData(0x00000001)]
    [InlineData(0x0000000F)]
    [InlineData(0x00000011)]
    [InlineData(0x0000001F)]
    [InlineData(0x00000021)]
    [InlineData(0x0000002F)]
    [InlineData(0x00000031)]
    [InlineData(0x0000003F)]
    [InlineData(0x00000041)]
    [InlineData(0x0000004F)]
    public void Show_InvalidMessageBoxImage_ThrowsInvalidEnumArgumentException(uint invalidImage)
    {
        InvalidEnumArgumentException exception = Assert.Throws<InvalidEnumArgumentException>(() =>
            MessageBox.Show("Test Message", "Test Caption", MessageBoxButton.OK, (MessageBoxImage)invalidImage, MessageBoxResult.None));
        Assert.Contains("icon", exception.Message);
    }

    // MessageBoxImage values are 0x00000000, 0x00000010, 0x00000020, 0x00000030, 0x00000040
    // Any other value is illegal.
    [WpfTheory]
    [InlineData(0xFFFFFFFF)]
    [InlineData(0x00000001)]
    [InlineData(0x0000000F)]
    [InlineData(0x00000011)]
    [InlineData(0x0000001F)]
    [InlineData(0x00000021)]
    [InlineData(0x0000002F)]
    [InlineData(0x00000031)]
    [InlineData(0x0000003F)]
    [InlineData(0x00000041)]
    [InlineData(0x0000004F)]
    public void Show_WithOwner_InvalidMessageBoxImage_ThrowsInvalidEnumArgumentException(uint invalidImage)
    {
        InvalidEnumArgumentException exception = Assert.Throws<InvalidEnumArgumentException>(() =>
            MessageBox.Show(new Window(), "Test Message", "Test Caption", MessageBoxButton.OK, (MessageBoxImage)invalidImage, MessageBoxResult.None));
        Assert.Contains("icon", exception.Message);
    }

    // MessageBoxOptions values are 0x00000000, 0x00020000, 0x00080000, 0x00100000, 0x00200000
    [WpfTheory]
    [InlineData(0xFFFFFFFF)]
    [InlineData(0x00000001)]
    [InlineData(0x0001FFFF)]
    [InlineData(0x00020001)]
    [InlineData(0x0007FFFF)]
    [InlineData(0x00080001)]
    [InlineData(0x000FFFFF)]
    [InlineData(0x00100001)]
    [InlineData(0x001FFFFF)]
    [InlineData(0x00200001)]
    public void Show_InvalidMessageBoxOptions_ThrowsInvalidEnumArgumentException(uint invalidOptions)
    {
        InvalidEnumArgumentException exception = Assert.Throws<InvalidEnumArgumentException>(() =>
            MessageBox.Show("Test Message", "Test Caption", MessageBoxButton.OK, MessageBoxImage.None, MessageBoxResult.None, (MessageBoxOptions)invalidOptions));
        Assert.Contains("options", exception.Message);
    }

    // MessageBoxOptions values are 0x00000000, 0x00020000, 0x00080000, 0x00100000, 0x00200000
    [WpfTheory]
    [InlineData(0xFFFFFFFF)]
    [InlineData(0x00000001)]
    [InlineData(0x0001FFFF)]
    [InlineData(0x00020001)]
    [InlineData(0x0007FFFF)]
    [InlineData(0x00080001)]
    [InlineData(0x000FFFFF)]
    [InlineData(0x00100001)]
    [InlineData(0x001FFFFF)]
    [InlineData(0x00200001)]
    public void Show_WithOwner_InvalidMessageBoxOptions_ThrowsInvalidEnumArgumentException(uint invalidOptions)
    {
        var exception = Assert.Throws<InvalidEnumArgumentException>(() =>
            MessageBox.Show(new Window(), "Test Message", "Test Caption", MessageBoxButton.OK, MessageBoxImage.None, MessageBoxResult.None, (MessageBoxOptions)invalidOptions));
        Assert.Contains("options", exception.Message);
    }

    // For either of these two MessageBoxOptions values, MessageBox.ShowCore throws an ArgumentException if the
    // owner window handle is non-zero (!= IntPtr.Zero).
    [WpfTheory]
    [InlineData(MessageBoxOptions.ServiceNotification)]
    [InlineData(MessageBoxOptions.DefaultDesktopOnly)]
    [InlineData(MessageBoxOptions.DefaultDesktopOnly | MessageBoxOptions.RightAlign)]
    [InlineData(MessageBoxOptions.ServiceNotification | MessageBoxOptions.RightAlign)]
    public void Show_WithOwner_WithSpecificMessageBoxOptions_ThrowsArgumentException(MessageBoxOptions messageBoxOptions)
    {
        // Arrange
        Window owner = new Window();
        WindowInteropHelper windowInteropHelper = new WindowInteropHelper(owner);
        windowInteropHelper.EnsureHandle();

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            MessageBox.Show(owner, "Test Message", "Test Caption", MessageBoxButton.OK, MessageBoxImage.None, MessageBoxResult.None, messageBoxOptions));
    }

    private static MessageBoxResult[] GetPortableButtonResults(MessageBoxButton button)
    {
        Type dialogType = GetPortableDialogType();
        MethodInfo method = dialogType.GetMethod(
            "GetButtonResults",
            BindingFlags.Static | BindingFlags.NonPublic)!;

        return (MessageBoxResult[])method.Invoke(null, new object[] { button })!;
    }

    private static MessageBoxResult GetPortableDefaultResult(
        MessageBoxResult[] results,
        MessageBoxResult requestedDefault)
    {
        Type dialogType = GetPortableDialogType();
        MethodInfo method = dialogType.GetMethod(
            "GetDefaultResult",
            BindingFlags.Static | BindingFlags.NonPublic)!;

        return (MessageBoxResult)method.Invoke(null, new object[] { results, requestedDefault })!;
    }

    private static Type GetPortableDialogType()
    {
        return typeof(MessageBox).Assembly.GetType(
            "System.Windows.PortableMessageBoxDialog",
            throwOnError: true)!;
    }
}
