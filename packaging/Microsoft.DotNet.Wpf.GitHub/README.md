# LibreWPF.Transport

This package carries the managed WPF transport assemblies built from this repository for the LibreWPF SDK preview lane. It supplies the WPF assembly identities that SDK-switched applications expect, including `WindowsBase`, `PresentationCore`, `PresentationFramework`, theme assemblies, Ribbon, UIAutomation, XAML, printing, and related managed framework assets.

`LibreWPF.Sdk` consumes this package through `ProGpuWpfManagedPackageId` and `ProGpuWpfManagedPackageVersion` while ProGPU and Silk.NET packages provide the portable native windowing, rendering, input, and runtime services. Applications should not reference this package directly unless they are validating the transport layer.

The package is generated from current repository build artifacts during the SDK CI gate. If package-mode validation fails because a WPF assembly is missing or stale, rebuild the managed WPF transport payload and repack this package instead of adding application-side workarounds.
