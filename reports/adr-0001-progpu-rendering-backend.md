# ADR 0001: Use ProGPU as the Cross-Platform WPF Rendering Backend

## Status

Accepted for the port lane.

## Context

The current WPF rendering path records managed drawing and visual state into DUCE/MIL resources, then relies on Windows-only native composition, D3D, WIC, DWrite, HWND, and Win32 message infrastructure. ProGPU already provides a Silk.NET/WebGPU backend, a retained scene model, a compositor, vector/text pipelines, and an initial WPF-shaped `PresentationCore` shim.

## Decision

Use ProGPU as a git submodule under `external/ProGPU`, tracking `fix/render-invalidation-and-leaks`.

Introduce a separate `src/ProGPU.Wpf` port lane that can build and run cross-platform before the existing WPF `PresentationCore` project is rewired. This port lane uses Silk.NET windowing and ProGPU WebGPU surfaces, and records drawing through the ProGPU WPF shim.

## Consequences

- Existing WPF sources remain intact while DUCE, Win32, and native imaging/text dependencies are replaced incrementally.
- The first executable boundary is ProGPU/Silk.NET rather than HWND/D3D.
- Porting can be validated on macOS before the full `PresentationCore` build is made cross-platform.
- Existing ProGPU vector, text, bitmap, and Mesh3D extension pipelines can be reused while WPF managed drawing, visual, and 3D object models are moved over incrementally.
- The ProGPU shim is temporary; final code should reuse WPF managed types where possible and replace native-only calls behind abstractions.
