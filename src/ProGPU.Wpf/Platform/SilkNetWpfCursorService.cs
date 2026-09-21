using System;
using Silk.NET.Windowing;
using SilkInput = Silk.NET.Input;

namespace System.Windows.Media.ProGPU.Platform;

public sealed class SilkNetWpfCursorService : IWpfCursorService
{
    public bool SetCursor(object inputSource, WpfCursor cursor)
    {
        switch (inputSource)
        {
            case SilkInput.IInputContext inputContext:
                return SetCursor(inputContext, cursor);

            case IView silkView:
                if (!silkView.IsInitialized)
                {
                    return false;
                }

                try
                {
                    using (var inputContext = SilkInput.InputWindowExtensions.CreateInput(silkView))
                    {
                        return SetCursor(inputContext, cursor);
                    }
                }
                catch (InvalidOperationException)
                {
                    return false;
                }

            default:
                throw new ArgumentException("Silk.NET cursor services require a Silk.NET view or input context.", nameof(inputSource));
        }
    }

    public bool SetCursor(SilkInput.IInputContext inputContext, WpfCursor cursor)
    {
        ArgumentNullException.ThrowIfNull(inputContext);

        var silkCursor = TranslateCursor(cursor);
        var applied = false;

        var mice = inputContext.Mice;
        for (var i = 0; i < mice.Count; i++)
        {
            var mouse = mice[i];
            try
            {
                if (!mouse.Cursor.IsSupported(silkCursor))
                {
                    continue;
                }

                mouse.Cursor.StandardCursor = silkCursor;
                applied = true;
            }
            catch
            {
                // Some backends expose cursor objects but reject specific cursor changes.
            }
        }

        return applied;
    }

    public static SilkInput.StandardCursor TranslateCursor(WpfCursor cursor)
    {
        return cursor switch
        {
            WpfCursor.Default => SilkInput.StandardCursor.Default,
            WpfCursor.Arrow => SilkInput.StandardCursor.Arrow,
            WpfCursor.IBeam => SilkInput.StandardCursor.IBeam,
            WpfCursor.Crosshair => SilkInput.StandardCursor.Crosshair,
            WpfCursor.Hand => SilkInput.StandardCursor.Hand,
            WpfCursor.SizeWE => SilkInput.StandardCursor.HResize,
            WpfCursor.SizeNS => SilkInput.StandardCursor.VResize,
            WpfCursor.SizeNWSE => SilkInput.StandardCursor.NwseResize,
            WpfCursor.SizeNESW => SilkInput.StandardCursor.NeswResize,
            WpfCursor.SizeAll => SilkInput.StandardCursor.ResizeAll,
            WpfCursor.No => SilkInput.StandardCursor.NotAllowed,
            WpfCursor.Wait => SilkInput.StandardCursor.Wait,
            WpfCursor.AppStarting => SilkInput.StandardCursor.WaitArrow,
            _ => throw new ArgumentOutOfRangeException(nameof(cursor), cursor, "Unsupported WPF cursor.")
        };
    }
}
