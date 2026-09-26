// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if PROGPU_WPF_ALIAS_WINCORE
#nullable enable
extern alias WinCore;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ProGPU.Wpf.Interop;
using WinCore::System.Private.Windows.Ole;
using WinCore::Windows.Win32;
using Com = WinCore::Windows.Win32.System.Com;
using FORMATETC = WinCore::Windows.Win32.System.Com.FORMATETC;
using HRESULT = WinCore::Windows.Win32.Foundation.HRESULT;
using STGMEDIUM = WinCore::Windows.Win32.System.Com.STGMEDIUM;
using HBITMAP = WinCore::Windows.Win32.Graphics.Gdi.HBITMAP;
using TYMED = WinCore::Windows.Win32.System.Com.TYMED;

namespace System.Windows.Ole;

internal sealed unsafe class WpfOleServices : IOleServices
{
    private static HRESULT NotImplemented => (HRESULT)unchecked((int)0x80004001);

    private WpfOleServices()
    {
    }

    public static void EnsureThreadState()
    {
        OleServicesContext.EnsureThreadState();
    }

    public static HRESULT GetDataHere(string format, object data, FORMATETC* pformatetc, STGMEDIUM* pmedium)
    {
        if (pformatetc is null || pmedium is null)
            return HRESULT.E_POINTER;

        if (format != DataFormatNames.Bitmap || data is not BitmapSource source)
            return NotImplemented;

        if (pformatetc->cfFormat != 2 || pformatetc->dwAspect != (uint)Com.DVASPECT.DVASPECT_CONTENT
            || pformatetc->lindex != -1 || pformatetc->ptd is not null)
            return HRESULT.DV_E_FORMATETC;

        // The core OLE adapter supplies an empty GDI medium for GetData. Do not
        // replace a caller-owned handle or a custom release owner in GetDataHere.
        if (((TYMED)pformatetc->tymed & TYMED.TYMED_GDI) == 0
            || pmedium->tymed != TYMED.TYMED_GDI || !pmedium->hGlobal.IsNull
            || pmedium->pUnkForRelease is not null)
            return HRESULT.DV_E_TYMED;

        // The source encoder already owns pixel format, palette, stride and
        // premultiplication conversion. Under portable media it never uses WIC.
        // Do not route through SystemDrawingHelper: canonical Bitmap.GetHbitmap
        // deliberately does not interpret portable image identities as HBITMAPs.
        BmpBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using MemoryStream stream = new();
        encoder.Save(stream);
        using WindowsGdiBitmapHandle bitmap = WindowsGdiBitmap.CreateFromBmp(
            stream.GetBuffer().AsSpan(0, checked((int)stream.Length)));

        pmedium->u.hBitmap = (HBITMAP)bitmap.Detach();
        return HRESULT.S_OK;
    }

    public static bool TryGetObjectFromDataObject<T>(
        Com.IDataObject* dataObject,
        string format,
        [NotNullWhen(true)] out T data)
    {
        data = default!;
        if (format != DataFormatNames.Bitmap
            || !WindowsClipboardBitmapSource.TryGet((nint)dataObject, out BitmapSource? bitmap)
            || bitmap is not T typedBitmap)
            return false;

        data = typedBitmap;
        return true;
    }

    public static bool AllowTypeWithoutResolver<T>() => false;

    public static bool IsValidTypeForFormat(Type type, string format) =>
        type != typeof(object) && !string.IsNullOrEmpty(format);

    public static void ValidateDataStoreData(ref string format, bool autoConvert, object? data)
    {
    }

    public static IComVisibleDataObject CreateDataObject() => new DataObject();

    static HRESULT IOleServices.OleGetClipboard(Com.IDataObject** dataObject) =>
        PInvokeCore.OleGetClipboard(dataObject);

    static HRESULT IOleServices.OleSetClipboard(Com.IDataObject* dataObject) =>
        PInvokeCore.OleSetClipboard(dataObject);

    static HRESULT IOleServices.OleFlushClipboard() =>
        PInvokeCore.OleFlushClipboard();
}

// This source adapter borrows a real native IDataObject, never a portable source
// identity. The caller retains its COM reference throughout the synchronous read.
internal static unsafe class WindowsClipboardBitmapSource
{
    internal static bool TryGet(nint nativeDataObject, [NotNullWhen(true)] out BitmapSource? bitmap)
    {
        bitmap = null;
        Com.IDataObject* dataObject = (Com.IDataObject*)nativeDataObject;
        if (dataObject is null)
            return false;

        FORMATETC formatEtc = new()
        {
            cfFormat = 2, // CF_BITMAP
            dwAspect = (uint)Com.DVASPECT.DVASPECT_CONTENT,
            lindex = -1,
            tymed = (uint)TYMED.TYMED_GDI
        };
        if (dataObject->QueryGetData(formatEtc).Failed)
            return false;

        STGMEDIUM medium = default;
        HRESULT result = dataObject->GetData(formatEtc, out medium);
        try
        {
            if (result.Failed || medium.tymed != TYMED.TYMED_GDI || medium.hGlobal.IsNull)
                return false;

            if (BitmapSource.UsesPortablePixelStorage)
            {
                // Copy while the actual OLE medium still owns the HBITMAP.
                // BitmapSource.Create copies again into source-owned storage;
                // the returned image never borrows the clipboard's lifetime.
                PortableBitmapSourcePixels pixels = WindowsGdiBitmap.CopyBgr32((nint)medium.hGlobal);
                bitmap = BitmapSource.Create(pixels.Width, pixels.Height, pixels.DpiX, pixels.DpiY,
                    PixelFormats.Bgr32, null, pixels.Pixels, pixels.Stride);
            }
            else
            {
                // Explicit native media keeps its existing WIC/MIL-backed image.
                bitmap = Imaging.CreateBitmapSourceFromHBitmap(
                    (nint)medium.hGlobal, 0, Int32Rect.Empty, sizeOptions: null);
            }

            return true;
        }
        finally
        {
            PInvokeCore.ReleaseStgMedium(ref medium);
        }
    }

}
#else
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Private.Windows.Ole;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using MS.Internal;
using Windows.Win32;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.System.Com;
using Windows.Win32.System.Ole;
using Com = Windows.Win32.System.Com;
using HRESULT = Windows.Win32.Foundation.HRESULT;

namespace System.Windows.Ole;

internal sealed unsafe class WpfOleServices : IOleServices
{
    // Prevent instantiation
    private WpfOleServices() { }

    public static void EnsureThreadState() => OleServicesContext.EnsureThreadState();

    public static HRESULT GetDataHere(string format, object data, FORMATETC* pformatetc, STGMEDIUM* pmedium)
    {
        TYMED mediumType = (TYMED)pformatetc->tymed;

        // Handle bitmaps.
        if (mediumType.HasFlag(TYMED.TYMED_GDI)
            && format.Equals(DataFormatNames.Bitmap)
            && (SystemDrawingHelper.IsBitmap(data) || data is BitmapSource))
        {
            pmedium->u.hBitmap = GetCompatibleBitmap(data);
            return HRESULT.S_OK;
        }

        // Handle enhanced metafiles.
        if (mediumType.HasFlag(TYMED.TYMED_ENHMF) && format.Equals(DataFormatNames.Emf))
        {
            if (SystemDrawingHelper.IsMetafile(data))
            {
                pmedium->u.hEnhMetaFile = SystemDrawingHelper.GetHandleFromMetafile(data);
            }
            else if (data is MemoryStream memoryStream && memoryStream.GetBuffer() is { } buffer && buffer.Length != 0)
            {
                HENHMETAFILE hemf = PInvoke.SetEnhMetaFileBits(buffer);

                if (hemf.IsNull)
                {
                    throw new Win32Exception();
                }
            }

            return HRESULT.S_OK;
        }

        return HRESULT.DV_E_TYMED;

        static HBITMAP GetCompatibleBitmap(object data)
        {
            HBITMAP hbitmap = SystemDrawingHelper.GetHBitmap(data, out int width, out int height);

            return hbitmap.IsNull ? HBITMAP.Null : hbitmap.CreateCompatibleBitmap(width, height);
        }
    }

    public static bool TryGetObjectFromDataObject<T>(
        Com.IDataObject* dataObject,
        string format,
        [NotNullWhen(true)] out T data)
    {
        data = default!;

        TYMED mediumType;
        ushort formatId;

        if (format == DataFormatNames.Bitmap)
        {
            mediumType = TYMED.TYMED_GDI;
            formatId = (ushort)CLIPBOARD_FORMAT.CF_BITMAP;
        }
        else if (format == DataFormatNames.Emf)
        {
            mediumType = TYMED.TYMED_ENHMF;
            formatId = (ushort)CLIPBOARD_FORMAT.CF_ENHMETAFILE;
        }
        else
        {
            return false;
        }

        FORMATETC formatEtc = new()
        {
            cfFormat = formatId,
            dwAspect = (uint)DVASPECT.DVASPECT_CONTENT,
            lindex = -1,
            tymed = (uint)mediumType
        };

        HRESULT result = dataObject->QueryGetData(formatEtc);

        if (result.Failed)
        {
            return false;
        }

        result = dataObject->GetData(formatEtc, out STGMEDIUM medium);

        try
        {
            if (result.Failed)
            {
                return false;
            }

            if (mediumType == TYMED.TYMED_GDI)
            {
                // Get the bitmap from the handle of bitmap.
                object bitmap = Imaging.CreateBitmapSourceFromHBitmap(
                    (HBITMAP)(nint)medium.hGlobal,
                    0,
                    Int32Rect.Empty,
                    sizeOptions: null);

                if (bitmap is T t)
                {
                    data = t;
                    return true;
                }
            }
            else
            {
                // Get the metafile object form the enhanced metafile handle.
                object metafile = SystemDrawingHelper.GetMetafileFromHemf((HENHMETAFILE)(nint)medium.hGlobal);
                if (metafile is T t)
                {
                    data = t;
                    return true;
                }
            }
        }
        finally
        {
            PInvokeCore.ReleaseStgMedium(ref medium);
        }

        return false;
    }

    public static bool AllowTypeWithoutResolver<T>()
    {
        // Image is a special case because we are reading bitmaps directly from the SerializationRecord.
        return typeof(T).FullName.Equals("System.Drawing.Image");
    }

    public static bool IsValidTypeForFormat(Type type, string format) => format switch
    {
        DataFormatNames.Bitmap or DataFormatNames.BinaryFormatBitmap =>
            type == typeof(BitmapSource) || type.FullName is "System.Drawing.Bitmap" or "System.Drawing.Image",
        DataFormatNames.Emf or DataFormatNames.BinaryFormatMetafile =>
            type.FullName is "System.Drawing.Imaging.Metafile" or "System.Drawing.Image",

        // All else should fall through as valid.
        _ => true
    };

    public static void ValidateDataStoreData(ref string format, bool autoConvert, object data)
    {
        // We do not have proper support for Dibs, so if the user explicitly asked
        // for Dib and provided a Bitmap object we can't convert.  Instead, publish as an HBITMAP
        // and let the system provide the conversion for us.
        if (format == DataFormats.Dib && autoConvert && (SystemDrawingHelper.IsBitmap(data) || data is BitmapSource))
        {
            format = DataFormats.Bitmap;
        }
    }

    public static IComVisibleDataObject CreateDataObject() => new DataObject();

    static HRESULT IOleServices.OleGetClipboard(Com.IDataObject** dataObject) =>
        PInvokeCore.OleGetClipboard(dataObject);

    static HRESULT IOleServices.OleSetClipboard(Com.IDataObject* dataObject) =>
        PInvokeCore.OleSetClipboard(dataObject);

    static HRESULT IOleServices.OleFlushClipboard() =>
        PInvokeCore.OleFlushClipboard();
}
#endif
