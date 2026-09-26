# Forms-only dependency runtime contract

Issue [#119](https://github.com/wieslawsoltes/LibreWPF/issues/119) reproduces with
both ProjectReference and PackageReference: a standard .NET SDK library with
`UseWPF=false` and `UseWindowsForms=true` contributes the distinct
`Microsoft.WindowsDesktop.App.WindowsForms` framework reference. The portable
SDK already removes the aggregate Desktop and WPF identities, but previously
left this identity in the app's runtime configuration. The build succeeded;
launch on macOS failed because Microsoft.WindowsDesktop.App is unavailable.

The shared transitive-reference target now removes the Forms identity only
when portable framework references and portable Forms are enabled and the SDK
selects its package dependency closure. Its existing aggregate/WPF behavior is
unchanged. Native mode, Forms opt-out and local-artifact mode retain the Forms
identity, and unrelated ASP.NET framework references remain untouched. This
does not admit arbitrary Windows-only APIs or qualify all third-party libraries.

## Executable coverage

`eng/tests/test_wpf_sdk_transitive_forms.py` executes seven real MSBuild filter
scenarios without restore. The canonical-source CI job runs them before the
larger source build, including both canonical and explicitly selected legacy
package policies and the opt-out boundaries above.

The existing canonical SDK package consumer gate additionally runs four fresh
applications: project/package dependency routes crossed with `UseWPF=true/false`.
Each application enables Forms normally and imports the exact staged SDK that
the enclosing package smoke has already byte-verified. The child is built and
packed with the standard .NET SDK, not LibreWPF.Sdk. Its real nuspec must contain
exactly the Forms framework reference. Each app's fresh assets file must include
the actual child, and the project route must retain a nonempty project-reference
graph. A unique test package version and byte comparison prevent stale package
cache entries from substituting for the freshly packed child.

Every consumer must have only Microsoft.NETCore.App in its runtime configuration,
execute the actual portable bootstrap, and create/read/dispose a real Button
through code compiled in the child library. No source text is rewritten and the
child is not switched to `UseWPF=true`. Test roots use physical paths: mixing
macOS `/tmp` and `/private/tmp` identities can otherwise lose a restore edge and
produce misleading results.

Local macOS ARM64 validation with .NET SDK 10.0.201 passed all seven filter
scenarios and all four dependency build/launch scenarios. Independent minimal
project and NuGet reproductions failed on the old SDK source with the missing
Desktop runtime and launched successfully after the fix and fresh restore.
An old assets file plus `--no-restore` can retain the old requirement, so SDK
upgrades require restore. CI builds fresh staged packages and runs the dependency
matrix on Linux; this local result is not a substitute for that pending gate or
the full Windows/macOS/Linux application checks.
