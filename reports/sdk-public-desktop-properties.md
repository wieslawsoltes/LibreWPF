# SDK public desktop property contracts

Issue #116: portable SDK imports previously captured `UseWPF` and
`UseWindowsForms` privately, then overwrote the public properties with `false`.
Consumer item conditions and executing targets therefore observed different
intent from the project source.

Portable mode now suppresses the WindowsDesktop target import rather than
changing those public properties. It imports the same real `Microsoft.WinFX.targets`
markup pipeline directly, preserving the SDK's portable XAML items and bundled
PresentationBuildTasks selection. The Windows-only targeting check is overridden
only in that portable import. Native opt-out and the independent foreign
WindowsDesktop dependency check remain unchanged. The already-existing duplicate
XAML globs in native opt-out are outside this change.

## Regression gates

`python3 eng/tests/test_wpf_sdk_desktop_properties.py --dotnet dotnet -v`
executes 26 SDK-evaluation/target scenarios without restore or Windows:

- Empty, false, WPF-only, Forms-only and mixed flags, from project properties
  and global properties, with `net10.0` and `net10.0-windows`.
- Conditional item and target observations of public intent; absence of desktop
  framework references; one application/page item; markup target wiring and
  nonduplicated implicit Forms namespaces.
- Explicit markup opt-out; native opt-out's three framework selections;
  native non-Windows targeting and foreign desktop dependency rejection.

The fast source matrix runs before the CI canonical source build. The existing
packaged canonical consumer gate also verifies the public flags during item
evaluation and before actual compilation, retaining its four independent
WPF/Forms and central-package-management combinations, runtime checks and exact
staged-SDK package comparison.

## Local evidence

On macOS ARM64, the matrix passed with .NET SDK 10.0.201 and
11.0.100-preview.5.26302.115. The original SDK fails the public-intent assertion.
A fresh isolated package containing the changed SDK sources and existing
source-built PresentationBuildTasks passed the same matrix from its extracted
package contents. No global package-cache files were edited.

All four canonical consumers restored, compiled and ran against that exact
staged SDK with published preview.65 dependencies. Two additional isolated
ResourceDictionary consumers, targeting `net10.0` and `net10.0-windows`, compiled
with zero warnings/errors and generated real `Dictionary.baml` and
`MarkupConsumer.g.resources` files. A source file included conditionally on
`UseWPF=true` was required by their compiled C# code.

This qualifies the SDK property/import and managed markup build contracts,
not interactive rendering, Windows native execution, trimming or NativeAOT.
No VM, package publication or renderer-policy change was involved.
