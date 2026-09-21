# LibreWPF Cross-Platform Roadmap

LibreWPF keeps the managed WPF application, XAML, control, layout, binding,
document, resource, and theme model while replacing the Windows-only
presentation stack with ProGPU and typed portable platform services.

## Current compatibility milestone

The current preview line prioritizes unchanged application and library sources:

- `LibreWPF.Sdk` redirects normal SDK-style WPF projects to the portable
  transport, ProGPU renderer, and Silk.NET platform host.
- Aero, Aero2, AeroLite, Classic, Fluent, Luna, and Royale themes are built,
  packaged, and switched at runtime by the release sample.
- Menus, context menus, ComboBox dropdowns, tooltips, dialogs, clipboard,
  drag/drop, text editing, keyboard commands, native title bars, resizing, and
  retained rendering are release-gated.
- LibreWinForms supplies source-built WinForms APIs, designers, resources, and
  `WindowsFormsIntegration` rather than growing a WPF-local compatibility shim.
- SharpDevelop, AvalonEdit, WpfDesigner, AvalonDock, Xceed Toolkit/DataGrid,
  SciChart, WPFGallery, ResourceToolkit, HexEditor, and reporting workbenches
  provide unchanged or package-only acceptance workloads.

## Platform policy

| Platform | Supported windowing path | Release contract |
| --- | --- | --- |
| Windows | Win32 window through Silk.NET | Native positioning, move, title bar, popups, input, and portable ProGPU rendering |
| macOS | Cocoa window through Silk.NET | Native positioning, move, title bar, transient popups, input, and portable ProGPU rendering |
| Linux X11/XWayland | X11 window through Silk.NET | Native positioning, `_NET_WM_MOVERESIZE`, transient popups, global coordinates, and docking semantics |
| Linux native Wayland | Explicit opt-in | Owner-composited popups; no false absolute positioning or interactive-move claim while GLFW lacks `xdg_toplevel`, seat, and input-serial access |

On a Wayland desktop with XWayland available, LibreWPF selects X11 before the
first GLFW window. This is the supported compatibility path for unchanged WPF
docking applications. The package-built Linux gate advertises a Wayland session,
injects a real native drag into the XWayland window, and requires dispatcher,
menu, ComboBox, direct Popup, theme, rendering, and shutdown validation to
complete.

`ProGpuWpfDiagnostics.TryGetWindowingCapabilities(...)` exposes the actual
backend and capabilities through a typed, reflection-free snapshot so docking
libraries can fail closed or select a preview strategy on explicit native
Wayland.

## Engineering priorities

1. Preserve source and XAML compatibility while removing reflection from
   rendering, input, windowing, resource, and invalidation hot paths.
2. Keep retained scene composition, GPU hit testing, clipping, effects, text,
   and render-target lifetime reusable across LibreWPF, LibreWinForms,
   Avalonia, WinUI-shaped ProGPU controls, and browser/mobile hosts.
3. Maintain bounded memory, stable glyph/path atlases, event-driven idle
   rendering, and measurable large-control scrolling performance.
4. Expand designers and tooling through SharpDevelop/OpenDevelop, AvalonEdit,
   WpfDesigner, XAML language services, Hot Reload, inspection, profiling, and
   package-mode templates.
5. Pursue trimming and NativeAOT only through generated or typed metadata—not
   by preserving runtime reflection scaffolding.

## Ecosystem status

- DevFlow, AvalonEdit, WpfDesigner, SharpDevelop, and VS Code/OpenDevelop
  integration have working LibreWPF paths.
- AvalonDock and third-party control suites are continuously validated; Linux
  X11/XWayland is the compatibility path for floating docking windows.
- LibreWinForms and `WindowsFormsIntegration` are released in coordinated
  previews and exercised by the SharpDevelop Forms designer.
- Remaining work is tracked as focused issues with reproduction and acceptance
  criteria rather than as unchecked items in this roadmap.

---

# Historical Upstream WPF Roadmap

Last year, we have made efforts in improving the testing infrastructure, merging community PRs to address long-standing issues, adding new control (OpenFolderDialog) and enabling new capability (hardware acceleration for RDP connections). The efforts to open-source our tests and automate the testing process through CI/CD pipelines, adding unit tests, tackle persistent issues, and addition of newer controls, all came together in bringing WPF on .NET 8.  

The following sections highlight the main areas we'll be prioritizing for .NET 9.0 in 2024, including the unfinished items from the previous roadmap. 
Along these items, we will continue to support updates and maintenance releases of .NET.

# Modernizing WPF

Long-term vision for modernization of WPF contains investments like support for nullability annotations, trimming and NativeAOT support, DirectX upgrades and integration of newer .NET features and abstractions. In the short-term, we have shortlisted the below items for 2023-24.  

The look and feel of WPF controls has not changed in years. We believe that updating our styles to match those used in Windows 11 will help WPF developers create more consistent Windows experiences.


| Goal | Description | Rationale |
| ------------- |:--------------|:-------------|
| Windows 11 Theming | Bringing Windows 11 look and feel for majority of WPF controls. <br/> Support for Win11 features such as snap layout, rounded corners for controls and newer color schemes would bring enhanced experience for WPF applications. <br/><br/> You can find the breakdown [here](https://github.com/dotnet/wpf/issues/8538). | For all consumer applications that are built on WPF running on Win11, this feature would ensure that applications can take advantage of modern design elements and behaviors. | 
| Nullability annotations | Enable nullability annotations in WPF | This increases the quality of the code base, as well as the quality for all WPF apps consuming it and reduces time spent debugging `ArgumentNullException`s and `NullReferenceException`s. <br/>Rest of the dotnet (eg. winforms) repo is already moving in the direction and this goal would bring WPF to latest standards as well. <br/><br/> We are thankful to community contributors who have helped us get started with this. We will continue to review and merge PRs in this area.  |

---
# Fundamentals

Enhancing the performance of WPF apps both in terms of reduced memory usage, improved startup times and better rendering will ensure that WPF customers continue to derive great value. Accessibility is a key aspect of our work, and we aim to provide better support for our users with different abilities. 

| Goal | Description | Rationale |
| ------------- |:--------------|:-------------|
| Accessibility Improvements | Improving accessibility support for WPF controls | This enables WPF applications to be more inclusive & would be able to better serve needs of users of all abilities. This involves addressing high priority accessibility bugs. |
| Performance | Benchmarking and optimizing WPF for enhanced support on all devices | Improving fundamentals of WPF. 

---

# Open Issues / PRs

The current backlog of issues and PRs in WPF stands in higher numbers and we intend to reduce this by addressing high priority issues and impactful PRs which improve the quality of the product and encourages community involvement. Similar to previous effort where we asked community to [help us prioritize issues and PRs](https://github.com/dotnet/wpf/discussions/6556). We intend to continue such initative and seek input on prioritization of issues/PRs from community.

| Goal | Description | Rationale |
| ------------- |:--------------|:-------------|
| Addressing high priority issues | Reduce the total number of issues/PRs. <br/> Use community feedback for prioritization. | Improving quality of WPF as a product. <br/> Enabling better developer experience for WPF repo contributors. |
---


# Testing Infrastructure Upgrades

| Goal | Description | Rationale |
| ------------- |:--------------|:-------------|
| Functional and Unit Tests | Addition of more unit tests along with bringing the testing infrastructure up-to-date. | Better reporting, predictability and reduced turnaround time for PRs. | 


Note - Some servicing issues (eg. Security issues) and high priority issues are not tracked in GitHub.

---
# SLA

We will continue to adhere to 3 business days timeline for first triage/response to new issues and non-code PRs, including documentation enhancements or general questions not related to source code issues, that are filed on GitHub. We will prioritize these issues according to their urgency and our available resources. We also evaluate code PRs based on factors such as recency, complexity, and test coverage and aim to merge 5-6 community contributions per month after testing. 

---
# Feedback welcome

We understand the excitement and passion surrounding the WPF community's desire for new features such as hyphen ligatures, SVG support, and colorful emoji, as well as fixing long standing bugs and issues. While we strive to bring these to life, we had to prioritize above issues, considering available resources, existing test coverage, feature complexity, and compatibility risks. 

Our aim is to deliver valuable updates to the WPF product, and we welcome and appreciate any support from the community in achieving these aspirations. 
