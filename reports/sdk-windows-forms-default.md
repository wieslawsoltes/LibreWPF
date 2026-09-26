# Windows Forms SDK package selection

Issue [LibreWPF #168](https://github.com/wieslawsoltes/LibreWPF/issues/168)
reproduces on preview.65: an ordinary `UseWindowsForms=true` project requests
`LibreWinForms.Compatibility.System.Windows.Forms`, which is not published on
NuGet.org. The SDK enabled LibreWinForms automatically but left canonical package
selection disabled. Existing SDK qualification supplied an explicit canonical
override, masking the normal consumer failure.

The SDK now defaults to the published source-built Forms runtime, ProGPU backend
and WindowsFormsIntegration bridge when LibreWinForms is selected. Explicit
canonical and LibreWinForms opt-outs retain their previous behavior. WPF-only
projects do not acquire Forms references. Both ordinary and centrally managed
package references consume the same selection. The SDK-switch fixture follows
the matching LibreWPF version instead of requesting preview.42.

The canonical consumer gate now tests `UseWPF=true` and `false`, each with central
package management enabled and disabled. It clears inherited selection overrides,
uses independent project directories, checks the complete Forms package closure,
and requires byte equality between the staged and restored SDK package. Custom
SDK versions and coordinated WPF dependency versions are honored.

## Validation on 2026-09-26

Source baseline: LibreWPF `6d5505262`, LibreWinForms `37f1f434a`, and ProGPU
`078ddc5ec`, with the changes described above. The original source SDK consumer
failed restore with the reported NU1101. After the fix:

- macOS ARM64: source SDK consumers restored, built and ran with WPF enabled
  and disabled. Explicit opt-out evaluation retained the compatibility policy.
- An isolated SDK package, `0.1.0-sdk-default-validation.20260926`, was packed
  from the changed SDK using the existing Release markup compiler. Published
  preview.65 packages supplied WPF, WinForms and ProGPU dependencies. All four
  fresh package consumer combinations restored, built with zero warnings/errors,
  and ran on macOS ARM64 and Ubuntu ARM64/.NET 10.0.400. The consumer verifies
  actual Forms/integration/backend identities, platform registration and creation
  of a real Form and Button. It does not open a visible window.
- Windows 11 ARM64/.NET 10.0.401: both source SDK consumer configurations
  restored, built with zero warnings/errors, and ran using the published
  preview.65 dependency graph.
- The focused SDK graph test passed. All 1,837 WPF tests passed after supplying
  the preview.65 native runtime directories; the first run without those
  directories had 18 native-library loading failures. Native library paths
  are required by the existing SDK gate and were not bypassed or mocked.
- The canonical cutover verifier, shell syntax and whitespace checks passed.

Windows and Linux VM runs were sequential. This validates package selection and
the exercised consumer behavior, not complete visible application fidelity,
NativeAOT, the full XAML designer, or all MIL/Direct2D functionality. The isolated
SDK package is a local validation artifact; no public package was published.
