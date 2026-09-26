// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if PROGPU_WPF_ALIAS_WINCORE
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using System.Windows.Ole;
using ProGPU.Wpf.Interop;
using ProGPU.Wpf.ShowcaseApp;
using Com = System.Runtime.InteropServices.ComTypes;

namespace System.Windows;

public sealed partial class WindowsClipboardImageTests
{
    [WindowsWpfFact]
    public void ExportRejectsWrongDescriptorAndCallerOwnedMediumAtomically()
    {
        Com.IDataObject source = new DataObject(DataFormats.Bitmap, ShowcaseClipboardImage.CreateSource());
        Com.FORMATETC format = BitmapFormat();
        Com.STGMEDIUM medium = new() { tymed = Com.TYMED.TYMED_GDI };
        format.cfFormat = 8; // CF_DIB, not CF_BITMAP.
        Assert.Throws<COMException>(() => source.GetDataHere(ref format, ref medium));
        Assert.Equal((nint)0, medium.unionmember);
        format = BitmapFormat();
        format.dwAspect = Com.DVASPECT.DVASPECT_THUMBNAIL;
        Assert.Throws<COMException>(() => source.GetDataHere(ref format, ref medium));
        format = BitmapFormat();
        format.lindex = 0;
        Assert.Throws<COMException>(() => source.GetDataHere(ref format, ref medium));
        format = BitmapFormat();
        using WindowsGdiBitmapHandle caller = CreateBitmap();
        medium.unionmember = caller.DangerousGetHandle();
        Assert.Throws<COMException>(() => source.GetDataHere(ref format, ref medium));
        Assert.Equal(caller.DangerousGetHandle(), medium.unionmember);
        Assert.Equal(3, WindowsGdiBitmap.CopyBgr32(caller.DangerousGetHandle()).Width);
    }

    [WindowsWpfFact]
    public void ImportReleasesCustomMediumOwnerOnSuccessFailureAndWrongTymed()
    {
        foreach (MediumOutcome outcome in Enum.GetValues<MediumOutcome>())
        {
            BitmapSource? result;
            using (NativeMediumProvider provider = new(outcome))
            {
                bool accepted = WindowsClipboardBitmapSource.TryGet(provider.Pointer, out result);
                Assert.Equal(outcome == MediumOutcome.Bitmap, accepted);
                Assert.Equal(1, provider.GetDataCalls);
                Assert.Equal(1, provider.MediumReleaseCalls);
                Assert.Equal(1, provider.ReferenceCount); // Only the provider's own reference remains.
                if (accepted) ShowcaseClipboardImage.Verify(result!);
                else Assert.Null(result);
            }
            if (result is not null) ShowcaseClipboardImage.Verify(result);
        }
    }

    private static Com.FORMATETC BitmapFormat() => new()
    {
        cfFormat = 2, dwAspect = Com.DVASPECT.DVASPECT_CONTENT,
        lindex = -1, tymed = Com.TYMED.TYMED_GDI
    };

    private static WindowsGdiBitmapHandle CreateBitmap()
    {
        BmpBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(ShowcaseClipboardImage.CreateSource()));
        using MemoryStream stream = new();
        encoder.Save(stream);
        return WindowsGdiBitmap.CreateFromBmp(stream.GetBuffer().AsSpan(0, (int)stream.Length));
    }

    private enum MediumOutcome { Bitmap, FailedBitmap, HGlobal }

    // Actual native COM producer with the Windows FORMATETC/STGMEDIUM ABI.
    // Public ComTypes.STGMEDIUM contains a managed object, so it cannot be used
    // in an UnmanagedCallersOnly signature. These native layouts retain x86/x64
    // pointer alignment and a real IUnknown release pointer, not an RCW.
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeFormat
    {
        public ushort Format;
        public nint TargetDevice;
        public uint Aspect;
        public int Index;
        public uint Tymed;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMedium { public uint Tymed; public nint Storage, ReleaseOwner; }

    // The source adapter invokes QueryGetData/GetData and the returned owner's
    // IUnknown. No fake WPF source object or replacement clipboard is involved.
    private sealed unsafe class NativeMediumProvider : IDisposable
    {
        private readonly MediumOutcome _outcome;
        private readonly WindowsGdiBitmapHandle? _bitmap;
        private readonly nint _global;
        private GCHandle _self;
        private readonly Instance* _instance;
        private readonly nint* _vtable;
        internal int GetDataCalls { get; private set; }
        internal int MediumReleaseCalls { get; private set; }
        internal int ReferenceCount { get; private set; } = 1;
        internal nint Pointer => (nint)_instance;

        [StructLayout(LayoutKind.Sequential)]
        private struct Instance { public nint* Vtable; public nint Owner; }

        internal NativeMediumProvider(MediumOutcome outcome)
        {
            _outcome = outcome;
            if (outcome == MediumOutcome.HGlobal)
            {
                _global = GlobalAlloc(0x42 /* GMEM_MOVEABLE | GMEM_ZEROINIT */, 8);
                if (_global == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
            }
            else _bitmap = CreateBitmap();
            _vtable = (nint*)NativeMemory.AllocZeroed(12, (nuint)sizeof(nint));
            _vtable[0] = (nint)(delegate* unmanaged[Stdcall]<Instance*, Guid*, void**, int>)&QueryInterface;
            _vtable[1] = (nint)(delegate* unmanaged[Stdcall]<Instance*, uint>)&AddRef;
            _vtable[2] = (nint)(delegate* unmanaged[Stdcall]<Instance*, uint>)&Release;
            _vtable[3] = (nint)(delegate* unmanaged[Stdcall]<Instance*, NativeFormat*, NativeMedium*, int>)&GetData;
            _vtable[5] = (nint)(delegate* unmanaged[Stdcall]<Instance*, NativeFormat*, int>)&QueryGetData;
            _self = GCHandle.Alloc(this);
            _instance = (Instance*)NativeMemory.Alloc((nuint)sizeof(Instance));
            _instance->Vtable = _vtable;
            _instance->Owner = GCHandle.ToIntPtr(_self);
        }

        private static NativeMediumProvider Owner(Instance* instance) =>
            (NativeMediumProvider)GCHandle.FromIntPtr(instance->Owner).Target!;

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
        private static int QueryInterface(Instance* self, Guid* iid, void** value)
        {
            if (value is null || iid is null) return unchecked((int)0x80004003); // E_POINTER
            *value = null;
            if (*iid != new Guid("00000000-0000-0000-C000-000000000046")
                && *iid != new Guid("0000010E-0000-0000-C000-000000000046"))
                return unchecked((int)0x80004002); // E_NOINTERFACE
            Owner(self).ReferenceCount++;
            *value = self;
            return 0;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
        private static uint AddRef(Instance* self) => (uint)++Owner(self).ReferenceCount;

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
        private static uint Release(Instance* self)
        {
            NativeMediumProvider owner = Owner(self);
            owner.MediumReleaseCalls++;
            return (uint)--owner.ReferenceCount;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
        private static int QueryGetData(Instance* self, NativeFormat* format) => 0;

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
        private static int GetData(Instance* self, NativeFormat* format, NativeMedium* medium)
        {
            NativeMediumProvider owner = Owner(self);
            owner.GetDataCalls++;
            owner.ReferenceCount++;
            *medium = default;
            medium->ReleaseOwner = (nint)self;
            medium->Tymed = owner._outcome == MediumOutcome.HGlobal ? 1u : 16u;
            medium->Storage = owner._outcome == MediumOutcome.HGlobal ? owner._global : owner._bitmap!.DangerousGetHandle();
            return owner._outcome == MediumOutcome.FailedBitmap ? unchecked((int)0x80004005) : 0;
        }

        public void Dispose()
        {
            Assert.Equal(1, ReferenceCount);
            _bitmap?.Dispose();
            if (_global != 0) Assert.Equal((nint)0, GlobalFree(_global));
            NativeMemory.Free(_instance);
            NativeMemory.Free(_vtable);
            _self.Free();
        }

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern nint GlobalAlloc(uint flags, nuint bytes);
        [DllImport("kernel32.dll", ExactSpelling = true)]
        private static extern nint GlobalFree(nint memory);
    }
}
#endif
