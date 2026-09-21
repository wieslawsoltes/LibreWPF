# Native MIL application naming

The SDK acceptance application is `ProGPU.Wpf.ShowcaseApp`. The independent chart
integration application is `ProGPU.Wpf.SciChartApp`. These names describe their
purpose and do not depend on a delivery milestone.

- Showcase launcher: `eng/run-progpu-wpf-showcase.sh`.
- Showcase quick check: `eng/progpu-wpf-showcase-quickcheck.sh`.
- Showcase configuration: `PROGPU_WPF_SHOWCASE_*` environment variables.
- Chart launcher and configuration remain `eng/run-progpu-wpf-scichart.sh` and
  `PROGPU_WPF_SCICHART_*`.
- Source classes, XAML namespaces, pack resource URIs, resource assets, automation
  element names, output directories and source-contract fixtures use these names.

Build fresh outputs under the new project identities. Previously produced local
artifacts and commit history are not rewritten or relabeled; historical build
results retain their recorded source commits and do not qualify renamed packages.
Documentation and linked reports use current application names for navigation.
There are no legacy launcher aliases or configuration fallbacks.

This is a naming change, not a scope reduction or completion claim. The native
MIL delivery plan and all package, application, platform and CI gates still apply.

## Validation

The renamed WPF test assembly builds in Release. The two project-identity cases
and renamed Retina viewport case pass (3/3); they check project files, resource
assets, XAML/code-behind identity, launchers and package-cache paths. Four paired
ProGPU showcase ellipse/join fixtures pass. Shell syntax and documentation checks
also pass. Source scans find no retired delivery-stage application identifiers
or filenames in the active LibreWPF-owned tree.

The broader SDK source-contract test still fails at its pre-existing ProGPU
central-package-management expectation, before its application assertions. The
full native suite remains 16/19, with the same three rendering/input failures.
These results do not qualify package-mode startup or rename old build artifacts.
