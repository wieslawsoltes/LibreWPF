# Agent Guidance

Portable FlowDocument layout must consume the typed `IPortableDocumentFlow`
service backed by ProGPU C++, using source-resolved block policy and actual
formatted paragraph lines. Keep original TextContainer/TextPointer ownership,
lists/markers, selection and content hit testing; do not clone the document into
fake TextBlocks or treat the empty portable FlowDocumentView as completion.
Width constraints precede formatting, placement follows it; an exhausted zero
width is not unbounded. Lazy pre-host document registration is only a prerequisite.
The scroll-view and source paginator consumers are connected but not runtime-qualified;
Windows SDK admission remains open.
`PortableFlowDocumentFormatter` owns source invalidation and retained layout
generations without a PTS context. Consume that live generation from the viewer;
do not rebuild a second tree or serve interaction from disposed/stale TextLines.
Keep caught formatting-time mutations invalid, source page/block policies shared,
and marker text separate from document indices. Symbol markers require the actual
symbol face, not ordinary-font fallback. `FlowDocumentView` selects its portable
visual/IContentHost/ITextView through frozen media ownership before PTS access on
every OS. Drawing and interaction borrow the same live layout; scrolling moves
retained content and translates viewport queries exactly once. Keep document-local
content rectangles distinct from viewport selection/caret geometry. Layout edits,
suspension and document replacement invalidate interaction before disposing lines.
Native logical caret boundaries must accept equivalent leading/trailing source
affinities without accepting positions inside shaped clusters. Compiled fixtures
do not qualify the actual MVP, symbol fonts or package-mode startup.
Paginated document consumers must use the shared native fragmentation service,
retain source page/column policy and original page-local text positions, and
explicitly resolve keep/widow/orphan behavior before admitting break boundaries.
Do not treat static paragraph-edge break flags as complete fragment-relative
widow/orphan policy. Impossible native fits must not silently clip a line or
switch the viewer to scrolling. `PortableFlowDocumentPaginator` selects before PTS
through frozen media ownership; its real DocumentPages and existing DocumentPageView
share original TextLines and page-local ITextView. Reserve native block insets at
fragment edges and preserve one translation for both drawing and interaction.
Continuation pages host their already-open source content ancestors. Disposed pages
release drawing, not the paginator-owned lines; edits/suspension invalidate the
whole generation. Nonzero widow/orphan constraints, decorated blocks crossing
fragments and empty decorated blocks remain explicit missing contracts, not parity.

Portable source text must preserve actual shaped content, clusters, styled runs
and caret/selection semantics. `SimpleTextLine.CreatePortableFallback` currently
manufactures an empty paragraph for unsupported cases; this is an outstanding
contract gap, never evidence of native/managed text parity or a permitted new
fallback. Reuse ProGPU's existing retained C++ paragraph pipeline for the typed
adapter, with leased context ownership; do not duplicate that composer in WPF
or indiscriminately replace Windows text-service OS guards.
`PortableTextLine` connects explicit styled physical faces, sizes, boolean features
and brushes through the registered typed text provider and the shared native
paragraph. Styles partition actual UTF-16 input; never split a scalar or infer the
render face from family names. Keep per-glyph font annotations, per-line source
ascent/descent and run-specific drawing brushes. Source composite/fallback fonts
must use `GlyphingCache.GetPortableFontRuns` and the existing `TypefaceMap` family
linking rules without invoking the DirectWrite itemizer. Preserve mapped em-scale
in native styles and actual GlyphRun size; source Typeface owns line metrics.
Do not substitute a first-family face for the mapped text ranges, bypass required
digit-substitution checks, or guess a render font by family name. Null-shape,
device-font and synthetic-font cases remain explicit unsupported contracts.
Keep remaining language, document and trimming behavior explicit;
do not convert its provider failure into the legacy empty paragraph. Preserve
source clusters, logical versus visual caret semantics and cloned continuation
ownership. Source GlyphRun native positions may be initialized only once before
publication and must feed both native and neutral exports. Its existing public
bidi/offset state must remain real WPF glyph state, not a nominal fake run.
Default incremental tabs must pass the source interval and text-start grid origin
through the typed paragraph request to ProGPU. Consume `PortableTextGlyph.IsTab`
as a non-ink item: retain source ranges, caret/selection and background bounds,
but never construct a GlyphRun containing its reserved native glyph id. Do not
replace tabs with spaces or a source-local width guess. Custom stop collections,
leaders and disabled-grid behavior remain explicit gaps until implemented.
Hidden document edges must retain source positions through `PortableTextSourceMap`
without entering shaping text. Preserve source run spans, glyph indices, logical
caret/selection affinities and wrapped-line lengths. Property modifiers use the
source-owned TextModifierScope inside-out evaluation, retain scope across explicit
line breaks, and end at EndOfParagraph. Do not silently discard directional scopes,
decorations or embedded objects while their native contracts are still missing.

`WindowChromeWorker` must select source ownership before HWND access: an active
portable window, a registered portable activation service, or frozen portable
media selection uses the typed portable chrome path even on Windows and before
source creation. A ProGPU HWND is not a WPF `HwndSource`; do not install WPF
chrome hooks, query WPF client areas or restore DWM frames on that assumption.
Preserve typed border updates and restoration when attached chrome is removed.
SystemCommands close/maximize/minimize/restore must select an active portable
Window before querying a handle, including on Windows. Use its existing source
Close/WindowState path so cancellation and typed host notifications are retained;
native WPF windows keep posted HWND system commands. System-menu display uses
the typed optional activation callback and shared ProGPU NativeWindowSystemMenu
provider, never a portable source handle passed to WPF Win32 helpers. Preserve
absolute desktop coordinates without framebuffer-DPI scaling. Win32 tracking,
advertised X11 requests and Cocoa native-action menus share that provider;
Wayland and rejected platform capabilities remain explicit, not successful no-ops.
Windows SDK package admission remains separate.

Decoder-backed core application images follow `BitmapSource.UsesPortablePixelStorage`
for backend selection on every OS. Existing portable format dispatch must not fall
through to WIC when a format is rejected. `BitmapImage` adopts cached/decoded owned
pixels by their actual storage, and rejects missing portable pixels before reading
a WIC handle. Preserve native Windows decoding when Windows MIL is selected;
actual Windows network cache services remain OS-specific. No source-local codec
rewrite or claims of qualified codec/worker/SIMD parity follow from this routing fix.

## Core Native MIL Delivery Priority

Standard portable run underlines use native paragraph range geometry and actual
source physical font metrics, with retained fill rectangles and baseline/top-edge
guidelines. Preserve brush identity, wrapped ownership, interior tabs, trailing
whitespace exclusion and ink bounds. Do not infer widths from codepoint counts or
reintroduce blanket rejection in TextBlock/Hyperlink. Custom/animated/paragraph
decorations and mixed-font averaging remain explicit, as does the separate rich
editor structural decoration-scope contract; do not erase those semantics.

RichTextBox render-scope selection follows frozen media ownership on every OS;
Windows portable media must not enter the PTS-backed FlowDocumentView. The source
TextBoxView editor retains real document symbols and per-run style ownership.
Never hide embedded objects, block layout, directional scopes or decorations as
successful non-ink edges when their layout contracts are absent. Registered text
providers must implement or reject justification, not receive silent left alignment.
Bounded source run copies must not split UTF-16 scalars. This editor route is not
full FlowDocument layout or Windows package admission evidence.
Retained editor line records own each source-formatted line advance and top.
Drawing, caret/selection, hit testing, scrolling and incremental updates must use
that same prefix map; do not restore a last-line-height-times-index approximation.
Rich editor selection uses document symbol offsets, including hidden source edges,
not flattened character offsets. The cache is not a replacement paragraph composer
or evidence of implemented block/page/decoration/inline-object layout.

Explicit native SDK/custom-host startup must call `ProGpuWpfNativeMediaServices.Initialize`
before source module initialization or construction of WPF media objects. Do not
wait for the first host to register the text provider: application constructors
can measure content. Keep registration lazy and preserve explicit provider priority;
ProGPU defaults survive temporary override disposal. Windows SDK admission and
remaining source consumer routing stay independent requirements.
TextFormatter must select from frozen media ownership, not OS or text complexity.
Under portable media, the registered provider owns simple and complex lines; retain
one provider reference per request and resolve immutable wrapped continuations
before registry lookup. Do not lose a shaped paragraph when its override is removed.
Classification and source control-string initialization follow the same frozen
selection before legacy MIL imports. Intrinsic min/max widths use ProGPU's native
logical scan through the optional typed measurement contract, never formatted-line
envelopes. Preserve hard-line modifier scope and source indentation. WrapWithOverflow
selects native whole-word wrapping; Wrap keeps emergency cluster breaking. Optimal
paragraph caches and forced-break reconstruction still fail explicitly until their
real native contracts exist.
Windows-MIL text keeps its native services; this routing is not SDK admission.

The current delivery sequence is defined in `docs/native-mil-core-delivery.md`.
Prioritize the usable end-to-end native MIL LibreWPF application path and its
major integration blockers, then feature-freeze and qualify it. Do not continue
general Direct2D/Win2D API expansion or isolated geometry refinements unless a
concrete core application dependency makes them blocking. Keep those broader
requests explicitly deferred, not silently removed or reported complete. Keep
implementation/compilation before the final validation phase as requested.
Before each new implementation batch, name the acceptance application, user action,
blocking source path and bounded outcome. Presentation/3D consumer completion is
not automatically a core prerequisite: trace ordinary host resize/DPI/popup needs
first. Historical checkpoint next-step suggestions do not override the delivery
plan's finish-first rule. Preserve all existing SDK and final qualification gates.
Device-loss callbacks must only schedule host-thread recovery. Rebuild targets
outside active frames, retain source-built WPF roots/native windows, renew external
image leases, and resolve popup sharing from the live owner rather than a captured
context. Catch typed device-loss failures only; do not hide unrelated application,
allocation or validation errors in a generic recovery loop.
An explicit native SDK renderer selection must not fall through to Windows MIL
or managed portable rendering. Keep package activation limits explicit until
the source-built Application/Window services support that platform; direct-host
smoke success is not package-mode application evidence.
Typed portable host registration selects window-service routing on every OS;
existing portable window identity, not an OS test, owns its operations. Rejected
registered activation and missing active-host run callbacks must fail closed.
Window callbacks/render-wakeup registration do not by themselves select the
source-built MIL transport: establish that choice before media-system startup
and route popup/interop handles by ownership before enabling Windows native SDK
activation. Do not mechanically remove Windows platform-service checks.
Source-built MIL transport guards use the shared `PortableWpfRuntime` choice,
frozen before any composition lock, channel or media-system ownership. Preserve
that choice across shutdown/device recovery and reject late backend switches.
Extend this typed policy to remaining MIL resource consumers; do not equate a
portable transport selection with completed Windows package support.
InputManager must freeze this same choice before creating keyboard/mouse devices.
Portable raw reports require host-owned device state on Windows too; do not use
Win32 asynchronous state for those reports or promote their keys through WPF TSF
a second time. Portable focus does not enable WPF's TSF pump/IMM association, and
source editors must not queue or attach WPF TSF text stores for portable input.
Keep native Windows input and actual OS language/system services separate. Host
composition/candidate bounds and input-method preferences are explicit unfinished
contracts; committed character delivery is not full IME support or Windows SDK
admission. Do not replace unrelated text-service OS guards indiscriminately.
Hidden interop-source creation must use the explicit typed `CreateHidden` host
capability, preserve a detached visual tree until Show, and publish one stable
source identity before SourceInitialized. Missing capability or invalid handles
must not fall through to another renderer. Portable source handles are not native
HWNDs; keep third-party native-handle calls behind their platform adapters.
Popup creation selects by the owning presentation source and frozen media policy;
existing popup operations select by source identity, including disposed-source
cleanup. Never create an unhosted portable source after host rejection. Popup,
ComboBox, menu and tooltip capture/focus checks must share the source-aware
`PopupControlService` policy instead of treating portable identities as HWNDs.
ProGPU root hosts register their typed popup service on every OS; native popup
child hosts explicitly opt out. A supplied owner source is authoritative over
opaque handles when the shared router probes multiple windows. The popup bridge
owns its factory-created source and releases it on failed creation and teardown;
main-window source bindings remain borrowed. A native-popup factory returning
null selects owner-surface composition, but thrown native setup failures must
release partial ownership and propagate, never silently switch surface kind.
Popup placement must query its registered owner for actual surface kind before
Show: native surfaces use monitor/work-area bounds, owner-surface popups use the
real owner client rectangle. Missing monitor capability is an explicit failure,
not permission to silently constrain native popups to the owner. Preserve host
desktop coordinates; do not independently rescale monitor origins by content DPI.
Owner DPI changes must publish popup owner origin and device scale together,
parent before child. Never move a native popup with a new device origin and its
old scale, then correct it in a second pass. Retain scale-only handling for
unpositioned/legacy owners without moving already updated surfaces again.
Client-to-desktop conversion uses the ProGPU-owned `PortableDesktopTransform`
snapshot and optional `IPortableDesktopGeometryHost` source capability. Preserve
its scale on legacy origin-only updates; framebuffer DPI is independent. Complete
popup child/limit/offset/input conversion with host publication before selecting
nonidentity desktop scale automatically; changing only screen anchors mixes units.
Popup child extents, screen-edge nudging, size restrictions and absolute-placement
offsets use the shared desktop vector mapping, not framebuffer DPI. Keep monitor
bounds in desktop units and convert restricted sizes back to client DIPs; native
HWND placement retains its existing device-transform route.
Host desktop-scale publication and native pointer normalization must share the
actual native client-size/content-scale policy. Legacy popup device coordinates
remain desktop times transport DPI; decode that frame before mapping offsets to
owner DIPs. Owner-surface overlays and input share those DIPs. Preserve independent
native-popup client scale and parent-before-child geometry updates; framebuffer
changes alone must not alter desktop scale. Do not remove Windows SDK admission
guards before closing the remaining cross-monitor/native-source ownership path.
Native popup owner-DPI updates must use `SetOwnerTransportScale` solely to decode
legacy placement coordinates. Never forward them to the separately surfaced
popup's presentation-source DPI; its native host owns framebuffer geometry after
the initial seed. Owner-surface popups still inherit owner DPI. Keep single-move
ordering fixtures and independent native-source DPI fixtures paired.
Portable Windows popup hosts use ProGPU `NativePopupWindow` for owned,
nonactivating top-level configuration. Native WPF HWND routing remains separate.
Configure the real hidden native window before Show; reject and dispose it on
configuration failure, never silently show it unowned or fall back to owner-surface
placement after native selection. Keep native/managed renderers on the same host
path and the Windows SDK guard until package startup integration is complete.
Source-WPF geometry Combine must use the typed geometry operations provider and
bounds-free operand export. Do not restore bounds-only boolean results or request
CombinedGeometry.Bounds while exporting a combination. Groups preserve figure
fill semantics, not child union semantics. Both renderer modes share the native
topology utility; register before layout, fail explicitly when unavailable, and
keep remaining Windows bounds/hit-test utility gaps separate from this route.
Relative geometry-operation tolerance must use actual transformed curve extrema
through the shared bounds reader, not Bezier control-hull bounds. Preserve the
allocation-free materialized-path traversal rather than creating a second DTO
snapshot just to infer tolerance bounds.
Portable fill bounds and point queries must use the same typed geometry provider,
preserve hollow figures and empty-versus-zero-size bounds, and select by frozen
media backend rather than OS. Primitive MIL point/type arrays are source-owned
transport to decode, not permission to compute bounds from their control hulls.
Do not route contributing pens through these fill-only operations or broaden
unsupported stroke hit tests into rectangle containment.
Contributing-pen bounds and point hits route through the typed geometry provider
using complete ProGPU query figures, source gaps, incoming joins and pen snapshots.
Geometry-local transforms precede widening; the drawing/world transform follows
it. Bounds union actual fill and emitted stroke, not an inflated fill rectangle.
Primitive overrides must not bypass this route under portable media selection.
Keep dash conversion intrinsic and default provider registration shared by both
renderer modes; source-owned WPF transport remains decoding, not a second stroker.
Geometry-region hit traversal must use the typed CompareFill provider before
legacy MIL imports under portable media selection. Preserve first/second relation
direction, first-operand relative tolerance and exact fill/clip topology; do not
replace selection with envelope containment or swallow unsupported finite inputs.
Memory-bitmap constructors and decode-failure replacement use the frozen media
choice through `BitmapSource.UsesPortablePixelStorage`, not OS-based MIL storage.
Cached sources consume managed pixel ownership directly. Every portable
WriteableBitmap initialization/copy path must own pinned-heap storage before a
lock exposes it; do not reintroduce a per-lock GCHandle that can leak when a
locked bitmap is abandoned. Preserve typed pixel snapshots, source DPI/stride,
clone independence, outermost-unlock publication and frozen write rejection.
Palette analysis and BitmapFrame encoding consume actual source-owned pixels on
every OS; portable media must reject missing storage before WIC access. Palette
construction and encoder admission follow the same frozen media choice. Keep
native Windows-MIL encoding separate, and reject unsupported portable containers
and WIC codec-info/handle requests explicitly before activating a native codec.
Existing portable palette enumeration and BMP serialization are not qualified
WIC quantization/codec parity or evidence of complete SIMD implementation.

## Reflection-Free Port Priority

System-menu host adapters resolve real native window/display ownership before
calling ProGPU's shared provider. X11 uses the owner's advertised WM capability
and native desktop coordinates, not an opaque WPF source handle or framebuffer
DPI conversion. Temporary XI2 negotiation must not alter the host input display.
Request submission is not menu-display evidence; keep Wayland and rejected X11
environments explicit until their required capabilities are implemented. Cocoa
selection must record an action without calling WPF during native tracking, then
revalidate retained window/view/delegate identity and current native capabilities
before invoking AppKit button-equivalent actions. Close must preserve delegate
cancellation. Map the top-left desktop through the actual primary screen and
AppKit view conversion, never the focus-dependent main screen or Retina pixels.

Native MIL popup composition must use canonical visual placement and local
geometry clips, never fake WPF roots or ScrollableAreaClip as a placement clip.
Separately surfaced popups inherit the owner renderer and are excluded from
owner-surface replay. Popup movement/content/visibility/removal must invalidate
the native source snapshot independently of the main root, and frame scratch
must release visual references after synchronous serialization.

The ProGPU WPF port must now prioritize a reflection-free, high-performance implementation. Runtime reflection in the WPF bridge or ProGPU is temporary scaffolding only: keep it limited to compatibility probes, diagnostics, or transitional adapters that are documented with an exit path, and replace product hot-path reflection with typed/source-integrated seams as soon as the local blocker is handled.

As the port approaches a workable MVP, performance and clean reflection-free code are release criteria, not polish. New WPF/progpu work should fail closed or add a typed portable seam when data is missing; do not keep samples alive by adding new duck-typed property probes, private-field scans, or reflection-based fake shapes.

The product bridge source is currently reflection-free by audit; keep that as an invariant while returning to broader SDK/sample work. New Xceed, SciChart, SDK smoke, input, clipping, hit-test, rendering, or platform fixes must use typed APIs, generated accessors, reusable ProGPU scene/vector/text primitives, or source-integrated WPF internals. Do not add managed WPF workarounds when the correct fix belongs in ProGPU rendering, shaders, layout/cache metadata, input, or DirectX/Silk.NET platform support.

## GPU-First Compute and SIMD Fallback Priority

Cached dashed linear path pens must use ProGPU's owned normalized dash spines and
emitted-outline material bounds, keeping source fill geometry independent. Do
not substitute ideal solid bounds, inflate fill bounds, silently discard terminal
point caps, or introduce a WPF-local dash stroker. Preserve partial/unsupported
reporting for representations that ProGPU cannot yet prepare exactly.
When ProGPU returns a complete filled-coverage outline, consume it instead of
stroking the spine again. Preserve one retained nonzero-fill mask, edge alias
state, outer transforms and source leases; do not blend terminal caps as separate
coverage draws or omit the optional payload through a legacy overload.

General cached linear paths must delegate contour preparation and stroke bounds
to ProGPU, preserve the original fill path independently from gap-split stroke
coverage, and retain typed pen/source identity. Primitive and strict single-line
shortcuts stay first; broader typed/local/native path consumers must not reduce
closed, multi-segment or gapped contours to an endpoint pair.

Cached ellipse and rounded-rectangle pens must use shared ProGPU smooth stroke
preparation, retain one analytic path for fill and stroke, and report rejected
pens even when fill succeeds. Direct commands preserve guideline snapping;
geometry-local transforms precede widening without direct-shape snapping.
Raw MIL must preserve typed pen brush identity and animation diagnostics.

Cached RectangleGeometry replay should consume each typed primitive descriptor
once, use ProGPU double corner mapping and shared closed-stroke preparation, and
reuse the resulting immutable native path for fill and stroke. Source brush fills
may use typed native-path clips directly; do not repack paths, create shim shapes,
drop perspective components, or broaden affine geometry clips to their bounds.

Cached rectangle pen consumers must preserve typed brush identity before material
adaptation, record fill before stroke, and use the shared ProGPU closed-rectangle
preparer for coverage and stroke-relative mapping. Preserve source dependencies,
outer transforms and alias state. Unsupported dashed/degenerate or other shape
pens must remain explicit partial/unsupported results, never successful fill-only
draws or bridge-local fill-bound inflation.

Cached line-geometry pens should consume `IPortablePrimitiveGeometrySource` before
requesting packed paths. Apply geometry-local transforms to endpoints before
shared stroke preparation, preserve outer transforms on completed coverage, and
do not capture a fill brush for zero-area open lines. Single-line lowering must
reject closed, curved, multi-segment and combined topology rather than treat it
as a single line; unavailable typed descriptors must not trigger shape probing.

Cached-brush pen replay must consume `IPortablePenStateSource` before color/gradient
pen adaptation can erase raw brush identity. Retained dependency traversal must
visit `PortablePenState.Brush`, including target and cache-policy resources. Keep
line/dash coverage and cap-derived bounds in ProGPU; do not add a WPF-local stroker,
silent epsilon dash replacement, reflected pen probes, or fill-bound inflation.

Compute-heavy ProGPU and LibreWPF work must use a typed, configurable execution policy whose default selects the fastest qualified path. Prefer native compute shaders first. When a kernel is data-parallel and expressible without compute-only workgroup memory, barriers, atomics, indirect-dispatch semantics, or storage-write requirements, the next fallback must stay on the GPU through an equivalent render/fragment (or other compatible shader-stage) implementation. Do not jump directly from a rejected compute profile to CPU work when a same-device GPU shader path can preserve semantics and avoid readback/upload.

GPU-stage fallbacks must share typed resources, algorithms, quality constants, and differential tests with the compute path, remain reusable across WPF, WinUI, and Avalonia, and expose the selected execution path through diagnostics. Configuration must support fastest/automatic, forced native-compute, forced compatible GPU-shader, forced intrinsic-SIMD CPU, and explicit scalar-reference modes. An incompatible forced path must fail closed; it must not silently select a slower or behaviorally reduced implementation. A GPU fallback must not introduce CPU pixel readback, CPU repacking, or per-item submissions merely to reuse a shader stage.

Every CPU fallback and other compute-heavy CPU hot path must use hardware intrinsics or runtime-intrinsic SIMD whenever the algorithm has independent lanes. Managed code should prefer `Vector128<T>`/`Vector256<T>`/`Vector512<T>`, `Vector<T>`, and platform intrinsics with a bounded scalar tail. Native code should use a shared SIMD abstraction or explicit architecture intrinsics with compile-time/runtime feature selection and a bounded scalar tail. Whole-buffer scalar loops are permitted only where data dependencies make SIMD inapplicable or in an explicitly forced scalar reference/diagnostic path; document that reason and test SIMD output against the scalar oracle. Keep vector paths allocation-free, alignment-safe, span-based, and bit/quality compatible, and benchmark representative sizes before making speed claims.

ProGPU PR #17 is merged to ProGPU `main`; continue ProGPU-side work from `main` instead of the old `fix/render-invalidation-and-leaks` branch. The WPF superproject submodule should track ProGPU `main`, and preview-release preparation should treat package production, package-mode SDK smoke, Xceed Toolkit/paid app fidelity, and ProGPU compositor quality/performance as current goals. ProGPU compositor APIs should remain reusable across WPF, WinUI, and Avalonia: host-specific layers may adapt input/windowing/surface ownership, but retained scene composition, clipping, effects, hit testing, text, and render-target lifetime should stay in ProGPU.

Public package and README branding for the WPF port is `LibreWPF`. Keep NuGet package IDs, SDK examples, preview bundle names, release workflow titles, and top-level README headings on `LibreWPF.*` names, while leaving source project names, namespaces, assembly names, target files, and runtime type identities unchanged unless a separate code migration is explicitly requested.

WinForms reuse should move to the `external/LibreWinForms` submodule and use `LibreWinForms.*` package branding. Do not keep expanding the LibreWPF-local WinForms compatibility shim as the product architecture. The shim may remain as a documented transitional bridge while source-built WinForms APIs, designers, resources, `WindowsFormsIntegration`, platform services, and drawing/windowing/input contracts move into LibreWinForms with the same reflection-free, typed-seam rules used by LibreWPF and ProGPU.

The preview SDK gate should keep third-party coverage in the same package-mode lane: free Toolkit/AvalonDock live validation is always part of `eng/progpu-wpf-sdk-ci.sh`, and paid Xceed validation runs when `XCEED_TOOLKIT_LICENSE_KEY` plus `XCEED_DATAGRID_LICENSE_KEY` are available or when `PROGPU_WPF_SDK_CI_INCLUDE_XCEED_PAID=1` forces it. License values must remain environment/deployment state and never source or reports content.

Bridge contracts used by package-mode apps must avoid shim-owned WPF structs/classes in public callback signatures. Prefer primitives, neutral DTOs, or source-integrated WPF interfaces/factories over runtime type lookup, reflected properties/events, or expression-built adapter delegates. Object render-data native callbacks must continue to use `PortablePoint`/`PortableRect` or replay-native structs, not local shim `Point`/`Rect` fallbacks.

GPU geometry hit-test candidate payloads must use the neutral `PortableGeometryHitTestCandidate` DTO from `ProGPU.Wpf.Interop`. `PortablePresentationSource` should consume that typed payload directly and must not scrape arbitrary callback result objects for `VisualHit` or `IntersectionDetail` properties; missing candidate data is a typed-contract gap to fix in ProGPU or source-built WPF, not a reason to add bridge-local reflection.

Portable point/input hit-test callbacks should use `PortableHitTestAllBufferOverride` when available. `WpfPortablePresentationSourceBridge` must keep the normal all-owner point path on caller-provided spans, with in-place transparent-overlay filtering, and `PortablePresentationSource` should rent/return its temporary owner buffer around callback and `InputHitTest` processing. The legacy array-returning callbacks are compatibility surfaces only; do not move hot pointer/input paths back to host-created owner arrays.

Portable launcher, message-box, and file-dialog service callbacks must use neutral interop DTOs (`PortableLaunchRequest`, `PortableMessageBoxRequest`, and `PortableFileDialogRequest`). `WpfPortableWindowActivation` must not reintroduce request-object `GetProperty(...)` scraping for `Uri`, `MessageBoxText`, `Caption`, `Button`, `Icon`, `DefaultResult`, `Options`, `FallbackResult`, `Kind`, `Title`, `SuggestedItemName`, or `Filter`; source-built WPF services own the conversion from WPF-specific request objects into the package-neutral DTOs.

ProGPU DirectX native resolver code must stay anchor-type based and must not expose or store `System.Reflection.Assembly` plumbing as an application-facing contract. `NativeLibrary.SetDllImportResolver(...)` may use `anchorType.Assembly` only at the runtime-loader boundary; resolver registrations should keep that anchor as an opaque object and tests must reject explicit `System.Reflection`, `BindingFlags`, `Dictionary<Assembly`, or `HashSet<Assembly>` usage in the resolver.

SciChart DirectX batch upload helpers should write directly from caller-owned or builder-owned spans into `ProGpuDirectXBuffer`. List-backed sprite, texture-vertex, and solid-color vertex batches should use `CollectionsMarshal.AsSpan(...)` for the final GPU upload and must not materialize `instanceData.ToArray()` or `vertexData.ToArray()` on the hot path.

Shader-effect sampler texture caches must read brush source bounds through `IPortableTileBrushSource`/`PortableTileBrush`, typed drawing state, and `IPortableVisualBoundsSource`/`PortableVisualBounds` only. Sampler render-to-texture replay should publish typed `IPortableGeometryDrawingStateSource` rectangle bounds directly instead of allocating synthetic local `RectangleGeometry` objects. Do not reintroduce sampler-local `DrawingBrush`/`VisualBrush` type-name checks, `Viewbox`/`Drawing`/`Visual` property probing, reflected visual `Bounds`/`DescendantBounds`/`VisualContentBounds`/`ContentBounds`, or `DesiredSize`/`Width`/`Height` fallback reflection; those are compatibility gaps to solve with typed WPF source seams or ProGPU-native metadata.

Built-in effect, legacy bitmap-effect emulation, bitmap-effect input checks, and shader-effect conversion must stay on `IPortableEffectSource`, `IPortableBitmapEffectInputSource`, `IPortablePixelShaderSource`, and `IPortableShaderEffectSource`. Shader sampler kind and image-source payloads must flow through `PortableShaderSamplerKind`/`PortableShaderSampler`, including typed implicit-input and image-source samplers. Do not reintroduce `BlurEffect`/`DropShadowEffect`/`BitmapEffect` type-name probing, property probing for effect parameters or bitmap inputs, `ShaderEffect` shape detection, `ImageBrush`/`ImplicitInputBrush` type-name checks, sampler `ImageSource` property probing, method invocation of `GetEmulatingEffect`/`ShouldSerializeInput`, or private `_floatRegisters`/`_samplerData`/pixel-shader bytecode field reads in `WpfEffectMapper`; source-built WPF owns those descriptors and ProGPU owns the native shader/effect execution.

Visual state, visual content, and render-data extraction must require typed portable state for normal retained WPF replay. Internal visual offset, transforms, clips, scrollable-area clips, opacity, opacity masks, effects, bitmap effects, bitmap-effect inputs, cache mode, render options, and snapping guidelines must flow through `IPortableVisualStateSource`/`PortableVisualState.HasOffset`/`PortableVisualState.HasTransform`/`PortableVisualState.HasClip`/`PortableVisualState.HasScrollableAreaClip`/`PortableVisualState.HasOpacity`/`PortableVisualState.HasOpacityMask`/`PortableVisualState.HasEffect`/`PortableVisualState.HasBitmapEffect`/`PortableVisualState.HasBitmapEffectInput`/`PortableVisualState.HasCacheMode`/render-option and guideline fields; do not reintroduce arbitrary `Offset`, `VisualOffset`, `_offset`, `Transform`, `VisualTransform`, `_transform`, `Clip`, `VisualClip`, `ScrollableAreaClip`, `VisualScrollableAreaClip`, `Opacity`, `OpacityMask`, `Effect`, `BitmapEffect`, `BitmapEffectInput`, `CacheMode`, `BitmapScalingMode`, `EdgeMode`, `ClearTypeHint`, `TextRenderingMode`, `TextHintingMode`, `XSnappingGuidelines`, `YSnappingGuidelines`, `VisualXSnappingGuidelines`, or `VisualYSnappingGuidelines` probing in `WpfVisualTreeRenderer`. Visual content and render-data must use `IPortableDrawingContentSource` and portable `IPortableRenderDataSource` snapshots. Do not reintroduce `_content`, `_drawingContent`, `_buffer`, `_curOffset`, or `_dependentResources` private-field probes in `WpfVisualContentBridge` or `WpfRenderDataBridge`; older compatibility gaps belong in explicit typed adapters or dedicated transitional bridge tests, not the product hot path.

Visual bounds and layout state in retained replay must come from `IPortableVisualBoundsSource`/`PortableVisualBounds` and `IPortableVisualLayoutStateSource`/`PortableVisualLayoutState`. Do not reintroduce reflected visual `Bounds`, `DescendantBounds`, `VisualContentBounds`, `ContentBounds`, `RenderSize`, `ActualWidth`, `ActualHeight`, `ClipToBounds`, or `GetLayoutClipInternal()` probing in `WpfVisualTreeRenderer`; source-built `Visual` owns content/descendant bounds, source-built `UIElement`/`FrameworkElement` owns render size, clip-to-bounds, and layout-clip snapshots, and third-party/Xceed cell-like controls used in tests should model those seams directly.

Rectangle clips that are already known through portable visual/layout state must stay as typed `WpfReplayRect`/native geometry data. Retained visual clip bounds for source-built WPF geometry should first use `IPortableGeometryPathSource` and accept only simple identity-transformed axis-aligned rectangle paths; arbitrary path bounds must not become broad rectangle clips. If an explicit visual/layout clip cannot be reduced to retained rectangle bounds, retained-owner replay must fail closed so normal command-scope geometry clipping can run, and that command-scope path should push `IPortableGeometryPathSource` clips plus intersected portable clip pairs through `IWpfNativeGeometryCommandSink.PushNativeGeometryClip(...)`/portable combined geometry before constructing local `MediaGeometry` fallbacks. Mixed portable path plus typed rectangle clip intersections must also become portable combined geometry by wrapping the rectangle as a portable path, not by adapting both operands into managed `CombinedGeometry`. Adapted local visual `MediaGeometry` clips should use native rectangle clip bounds or `IWpfNativeGeometryCommandSink.PushNativeGeometryClip(MediaGeometry)` before managed `PushClip(...)` fallback. Do not reintroduce fake rectangle objects such as `ReflectedRectangleClip` or any object-shape round trip that then reads `X`, `Y`, `Width`, or `Height` through reflection for `ClipToBounds`, layout clips, scrollable-area clips, or retained visual owner clips.

Viewport3D replay for source-built WPF must use `IPortableViewport3DSceneSource`/`PortableViewport3DScene`. Source-built `Viewport3DVisual` owns flattening camera, viewport, lights, visual/model transforms, mesh positions/normals/indices, material colors, and back-face entries into the portable DTO. `WpfVisualTreeRenderer` must route `IPortableViewport3DSceneSource` instances to the viewport sink without requiring a `Viewport3DVisual` type-name match. `WpfViewport3DSceneBridge` must fail closed for objects that do not publish the portable scene contract. Do not reintroduce WPF-shaped 3D reflection fallback or probes for `Viewport`, `Camera`, `Children`, `Content`, `Transform`, `Geometry`, `Positions`, `Normals`, `TriangleIndices`, `Material`, `Brush`, `Color`, or light/camera properties.

Visual child traversal in retained replay must come from `IPortableVisualChildrenSource`. Do not reintroduce `Children` property probing, `VisualChildrenCount` probing, protected `GetVisualChild` method invocation, or direct reflected `Children` collection dependency registration in `WpfVisualTreeRenderer`; source-built `Visual` owns child enumeration, and retained invalidation should snapshot typed child topology through the same portable seam.

WPF visual child replay and retained content-bounds inference should index `IPortableVisualChildrenSource` directly after reading `TryGetPortableVisualChildCount(...)`. Do not reintroduce `ExtractChildren(visual)` foreach traversal or `PortableVisualChildrenEnumerable`/`PortableVisualChildrenEnumerator` wrappers in `WpfVisualTreeRenderer`; large Xceed/DataGrid visual trees should avoid child enumerator dispatch while preserving the typed source-built WPF child seam.

Portable service registration must go through `PortableWpfServiceRegistry` typed registrars only. Do not reintroduce reflected `PortableWindowActivationService`, `PortableClipboardService`, `PortableLauncherService`, `PortableMessageBoxService`, `PortableFileDialogService`, or `PortableMediaContextRenderService` `Register(...)` method discovery in `WpfPortableWindowActivation`; missing registration is a source-built WPF interop gap, not a reason to probe internal service types.

Portable window activation operations must use `IPortableWindowActivationServiceRegistrar` typed methods for main-window queries, native-host close/cancel, activation-state changes, dispatcher flushing, and WPF drag/drop event routing. Do not reintroduce reflected `Close()`, `HandleActivate(bool)`, `Application.Current`/`MainWindow`, static `PortableWindowActivationService.SetActivationState(...)`, `FlushDispatcherOperations(...)`, `ProcessDragDropEvent(...)`, `ProcessDragDrop(...)`, `OnPortableDrop`, `OnPortableFileDrop`, or `DropFiles` lookup in `WpfPortableWindowActivation`; older source assemblies that do not publish typed operation support must fail closed until the source-built WPF seam is implemented.

Portable host option and initial window-state reads must come from `IPortableWindowStateSource`/`PortableWindowState`. Source-built WPF `Window` owns the title, logical width/height, actual width/height fallback, left/top, window state, topmost, resize mode, and window style snapshot; `WpfPortableWindowActivation` must not reintroduce duck-typed `Title`, `Width`, `Height`, `ActualWidth`, `ActualHeight`, `Left`, `Top`, `WindowState`, `Topmost`, `ResizeMode`, or `WindowStyle` property probing to build `ProGpuWpfWindowOptions` or synchronize the native host.

Portable host input routing must use `IPortableWindowActivationServiceRegistrar.TryBeginInvokeInput(...)` and `TryProcessInputEvent(...)`. Do not reintroduce reflected `Dispatcher`, `CheckAccess`, `BeginInvoke`, `DispatcherPriority`, `OnPortableInput`, `HandlePortableInput`, compatible input-args constructor mapping, enum-name conversion, or reflected `Handled` propagation in `WpfPortableWindowActivation`; input support for real WPF belongs in source-built `PortableWindowActivationService`, not bridge-local duck typing.

Retained invalidation dependency traversal and visual-state polling must also follow typed seams. Do not reintroduce `_content`, `_drawingContent`, `_floatRegisters`, `_samplerData`, `_shaderBytecode`, `_changeVersion`, `_internalVersion`, or `_version` field-name scanning in `WpfVisualInvalidationTracker`; source-built `DrawingVisual` and `UIElement` retained content must be reached through `IPortableDrawingContentSource`, render-data resources through `IPortableRenderDataSource`, visual child topology through `IPortableVisualChildrenSource`, visual state through `IPortableVisualStateSource`, and layout state through `IPortableVisualLayoutStateSource`. Drawing/resource graph dependencies must flow through `IPortableGeometryDrawingStateSource`, `IPortableImageDrawingStateSource`, `IPortableGlyphRunDrawingStateSource`, `IPortableDrawingGroupStateSource`, `IPortableDrawingGroupChildrenSource`, `IPortableTileBrushSource`, and `IPortableShaderEffectSource`. Do not reintroduce visual-state fallback polling of `Offset`, `VisualOffset`, `_offset`, `Clip`, `VisualClip`, `ScrollableAreaClip`, `VisualScrollableAreaClip`, `Opacity`, `OpacityMask`, `VisualOpacityMask`, `Transform`, `VisualTransform`, `_transform`, `RenderSize`, `ActualWidth`, `ActualHeight`, `ClipToBounds`, or `GetLayoutClipInternal()` in the tracker. Generic reflected reference-property traversal is removed and must not return for fake or compatibility objects; missing dependencies are now typed-contract gaps to fix in source-built WPF or ProGPU interop. Do not reintroduce reflected `ChangeVersion`/`InternalVersion`/`Version` property polling; retained source changes must flow through `IPortableInvalidationSource`, typed visual/content/render-data/drawing-state snapshots, `INotifyPropertyChanged`, or `INotifyCollectionChanged`. Retained invalidation subscriptions must use `IPortableInvalidationSource`, `INotifyPropertyChanged`, or `INotifyCollectionChanged`; do not reintroduce reflected `Changed`/`Invalidated` event discovery with `GetEvent`, `AddEventHandler`, or event-name scans. Frame/version polling must compare live `IPortableVisualChildrenSource` topology directly against tracker-owned baseline snapshots and walk typed child dependencies without rebuilding a temporary current child-snapshot dictionary; retained baseline arrays may allocate only because the tracker owns their lifetime. Portable dependency traversal should use `VisitPortableDependencies(...)` with struct visitors so subscribe, snapshot, graph change, and retained-registration paths stream dependencies without temporary `List<object?>` materialization. Retained dependency registration should use `WpfVisualInvalidationTracker.RegisterTrackedDependencies(...)` to stream registrations into `IWpfRetainedVisualBranchSink`; do not rebuild a temporary tracked-dependency list in `WpfRetainedVisualDependencyRegistrar`.

Portable drawing-state contracts are authoritative. `WpfDrawingReplay` must replay source-built `GeometryDrawing`, `ImageDrawing`, and `GlyphRunDrawing` only through `IPortableGeometryDrawingStateSource`, `IPortableImageDrawingStateSource`, and `IPortableGlyphRunDrawingStateSource`; do not reintroduce leaf drawing type-name dispatch or reflected `Geometry`, `Brush`, `Pen`, `ImageSource`, `Rect`, `GlyphRun`, `ForegroundBrush`, or `Bounds` probing. `DrawingGroup` replay is now typed-only through `IPortableDrawingGroupStateSource` for group state and `IPortableDrawingGroupChildrenSource` for child count/index traversal; source-built WPF `DrawingGroup` must not copy children into `PortableDrawingGroupState.Children` for the ProGPU bridge hot path. Source-built `DrawingGroup.Transform` should prefer `IPortableTransformMatrixSource`/native `Matrix4x4` pushes on ProGPU native transform sinks and drawing-bounds inference before constructing local `MediaTransform` fallbacks, and `DrawingGroup.ClipGeometry` should prefer exact portable rectangle clips as native `WpfReplayRect` clip scopes, then native `PortableGeometryPath` clip pushes on ProGPU native geometry sinks, before constructing local `MediaGeometry` fallbacks. If a drawing-group clip is already a local `MediaGeometry`, exact rectangle clips should use native clip bounds and rounded/arbitrary media clips should try `IWpfNativeGeometryCommandSink.PushNativeGeometryClip(MediaGeometry)` before managed `PushClip(...)`. Do not reintroduce `DrawingGroup` type-name dispatch, `Children` property/indexer scraping, child-array snapshot copies for source-built WPF groups, or reflected group property reads for bounds, transform, clip, opacity, opacity mask, effect/cache, bitmap-effect, guideline, or render-option state. Drawing replay must not import `System.Reflection`, carry generic `BindingFlags` helpers, read arbitrary properties, find indexers, or dispatch by object type name; missing replay data is a typed DTO/source-built WPF contract gap.

Portable glyph-run contracts are authoritative. Source-built WPF `GlyphRun` must publish hot native replay data through `IPortableNativeGlyphRunSource`/`PortableNativeGlyphRun` when glyph positions can be emitted as `Vector2[]`, with `IPortableGlyphRunSource`/`PortableGlyphRun` retained as the neutral compatibility fallback for positions or advances/offsets, baseline origin, font size, font identity, style simulations, and optional affine transform. Initialized source-built glyph runs should cache those portable/native DTOs on the `GlyphRun` instance because WPF glyph-run mutation is initialization-only; do not rebuild glyph index, advance, offset, family-name, or native position arrays on every render-data export. The ProGPU bridge may annotate those cached DTOs with the resolved backend `TtfFont` through `NativeFont`, so repeated Xceed/AvalonDock/DataGrid text replay must not resolve the same family URI/name every frame. `RenderData` export and ProGPU native replay should prefer the native-vector DTO so Xceed/AvalonDock/DataGrid text cells avoid round-tripping positions through portable point/advance arrays; resolver/native replay code must consume those DTOs directly and must not fall back to shim-specific glyph APIs or reflected `GlyphIndices`, `GlyphTypeface`, `Font`, `FontRenderingEmSize`, `AdvanceWidths`, `GlyphOffsets`, or `BaselineOrigin` probing. Local `GlyphRun` construction is only a non-native managed fallback.

Portable geometry path contracts are authoritative. `WpfResourceResolver.AdaptGeometry(...)` may consume local `MediaGeometry` instances directly and source-built `IPortableGeometryPathSource` DTOs, but it must not probe reflected `LineGeometry`, `RectangleGeometry`, `EllipseGeometry`, `CombinedGeometry`, `GeometryGroup`, `PathGeometry`, figure, segment, or `Transform` properties. If `TryGetPortableGeometryPath(...)` returns false, that object must fail closed. The resolver must not import `System.Reflection`, carry generic property/indexer helpers, or parse arbitrary `ToString()` output as path data. Render-data decoders, generated/object render-data sinks, drawing replay, retained visual bounds inference, drawing-bounds inference, and command sinks should prefer primitive rectangle/ellipse commands, `IPortableGeometryPathSource` DTOs, and native ProGPU geometry/clip commands before constructing local `MediaGeometry` fallbacks. Generated render-data `DrawGeometry(...)` must route `IPortableGeometryPathSource` media geometries to `IWpfNativeGeometryCommandSink.DrawNativeGeometry(...)` after tile-brush and primitive lowering and before managed `_sink.DrawGeometry(...)` fallback. Generated and object render-data `DrawGeometry(...)` plus `GeometryDrawing` replay should lower identity-transformed local `RectangleGeometry` values, finite axis-aligned scale/translate non-rounded local rectangle geometry, finite affine local line geometry and line-only path/polyline geometry, rounded identity rectangles, and finite axis-preserving local `EllipseGeometry` values to `IWpfNativePrimitiveCommandSink` primitive commands or compatibility primitive commands before generic geometry replay; arbitrary local media geometry draws should then try `IWpfNativeGeometryCommandSink.DrawNativeGeometry(MediaGeometry)` before managed `DrawGeometry(...)` fallback. Generated and object render-data `PushClip(...)` should lower only non-rounded identity or finite axis-aligned scale/translate local `RectangleGeometry` clips, exact axis-aligned local `PathGeometry` rectangle clips with identity or finite axis-aligned scale/translate transforms, direct rectangle DTO carriers, and exact axis-aligned portable rectangle path clips to `IWpfNativeClipCommandSink.PushNativeClip(...)`; rounded rectangles and arbitrary local media paths should use `IWpfNativeGeometryCommandSink.PushNativeGeometryClip(MediaGeometry)` when the sink can convert them, then managed `PushClip(...)` fallback, and they must not be broadened to rectangular clips. Retained visual bounds inference from MIL render data must also consume native primitive, transform, rectangle-clip, and geometry commands through `IWpfNativePrimitiveCommandSink`, `IWpfNativeTransformCommandSink`, `IWpfNativeClipCommandSink`, and `IWpfNativeGeometryCommandSink` before falling back to managed `Point`/`Rect`/`MediaGeometry` decode. Drawing and retained visual bounds inference should derive exact bounds from identity and axis-aligned scale/translate portable rectangle, line, quadratic, cubic, and arc path commands, conservative bounds from portable combined-geometry operands, and general transformed portable path bounds from the shared native `WpfPortablePathGeometryConverter`, before trusting `PortableGeometryPath.Bounds` or constructing local `MediaGeometry`; ProGPU native command sinks must prefer those exact portable bounds for brush/pen mapping when available and otherwise use the converted native `VectorPathGeometry` bounds after local and active transforms are applied, never `PortableGeometryPath.Bounds` directly. All portable geometry bounds consumers should use `WpfPortableGeometryBoundsReader` for that exact-reader/native-conversion/metadata-only ordering instead of duplicating local fallback logic. Drawing replay, object render-data drawing, and drawing-bounds inference should also accept direct `Rect`, `WpfReplayRect`, and `PortableRect` geometry-state carriers as terminal typed rectangle primitives.

MIL render-data clip decoding must keep exact rectangle clips on `IWpfNativeClipCommandSink.PushNativeClip(...)`, then try `IWpfNativeGeometryCommandSink.PushNativeGeometryClip(MediaGeometry)` for rounded rectangles and arbitrary local media paths before managed `PushClip(...)` fallback. Do not broaden those local media clips to rectangle bounds, because clipping correctness for Xceed/AvalonDock/DataGrid scroll surfaces depends on exact native clip geometry.

Bitmap-source adaptation should not manufacture shim `WriteableBitmap` instances or invoke `CopyPixels` through reflection. Normal WPF bitmap sources must provide pixels through `IPortableBitmapSourcePixelsSource` and/or backend textures through `IProGpuTextureSource`; arbitrary non-media object shapes should not be converted into shim imaging types or probed for `PixelWidth`, `PixelHeight`, `Format`, `Palette`, or `CopyPixels` on the product path.

Portable brush, pen, transform, and tile-brush resource contracts are authoritative. If an object implements `IPortableBrushSource`, `IPortablePenSource`, `IPortableTransformMatrixSource`, or `IPortableTileBrushSource` but reports that no portable DTO is available, `WpfResourceResolver`/`WpfDrawingReplay` must not fall through to reflected `SolidColorBrush`/gradient/`Pen`/`MatrixTransform`/`ImageBrush`/`DrawingBrush`/`VisualBrush`/transform property shape probing for that same object. For brush and pen resources, `WpfResourceResolver` may accept local `MediaBrush`/`MediaPen` instances for managed replay and `IPortableBrushSource`/`IPortablePenSource` DTOs for native adaptation; replay-created linear/radial gradients should be real ProGPU PresentationCore shim brushes that preserve `PortableBrush` state through typed `Transform`/`RelativeTransform` support rather than bridge-local carriers. Arbitrary objects shaped like `SolidColorBrush`, linear/radial gradients, or `Pen` must fail closed instead of being read through type-name or property reflection. Tile-brush replay must recognize only `IPortableTileBrushSource`; fill geometry from source-built `IPortableGeometryPathSource` should derive placement bounds through the shared portable geometry bounds reader, push exact portable rectangle fills through native `IWpfNativeClipCommandSink` bounds clips, then use native `IWpfNativeGeometryCommandSink.PushNativeGeometryClip(...)` before constructing local `MediaGeometry`, and exact local media rectangle fills, simple tile clips, and source rectangle clips should use native bounds clips before allocating local rectangle geometries. Generated and object render-data tile-brush draws with a pen must keep the follow-up stroke on native rectangle or native media-geometry commands when the sink supports them. Non-rectangle local media fill clips should call the native media-geometry clip path when the sink can convert them to exact backend geometry, then fall back to managed `PushClip(...)` only when native conversion is unavailable. Do not broaden arbitrary local media path bounds into rectangle clips. Do not reintroduce `ImageBrush`/`DrawingBrush`/`VisualBrush` type-name dispatch or reflected `ImageSource`, `Drawing`, `Visual`, `Viewport`, `Viewbox`, `TileMode`, `Stretch`, alignment, opacity, transform, mapping-mode, or image-source `PixelWidth`/`PixelHeight` probes in `WpfDrawingReplay`. Source-built `Transform` owns matrix composition for matrix, translate, scale, rotate, skew, and transform-group cases through `IPortableTransformMatrixSource`; do not reconstruct transform matrices in the bridge by probing reflected `Value`, `Children`, `X`, `Y`, `ScaleX`, `ScaleY`, `CenterX`, `CenterY`, `Angle`, `AngleX`, or `AngleY` properties or by matching transform subtype names.

Brush transform support and diagnostics must also consume typed matrix data. `WpfResourceResolver` should apply gradient brush transforms through portable brush DTO matrix data and native mapped-brush coordinate transforms, and count unadaptable non-null transform values explicitly; do not infer transform presence or identity with `ToString()`, type full-name comparisons, or string-shaped transform values.

Portable gradient brush DTO transforms are supported native state. Direct `AdaptNativeBrush(...)` paths for `IPortableBrushSource` linear/radial gradients and replay-created ProGPU shim gradients must route through the same portable DTO/native mapped-brush path so `PortableBrush.Transform`, `PortableBrush.RelativeTransform`, mapping mode, bounds mapping, and non-invertible-transform diagnostics stay identical. Do not count `HasTransform` or `HasRelativeTransform` flags as unsupported by themselves, and do not pre-map relative gradient points in a way that bypasses typed transform application.

Use the existing `IPortableGeometryPathSource`, `IPortableGuidelineSetSource`, `IPortableTransformMatrixSource`, `IPortableBrushSource`, `IPortableTileBrushSource`, `IPortablePenSource`, `IPortableEffectSource`, `IPortableBitmapEffectInputSource`, `IPortablePixelShaderSource`, `IPortableShaderEffectSource`, `IPortableDrawingContentSource`, `IPortableGeometryDrawingStateSource`, `IPortableImageDrawingStateSource`, `IPortableGlyphRunDrawingStateSource`, `IPortableGlyphRunSource`, `IPortableDrawingGroupStateSource`, `IPortableRenderDataSource`, `IPortableInvalidationSource`, `IPortableVisualChildrenSource`, `IPortableVisualStateSource`, `IPortableVisualBoundsSource`, `IPortableVisualLayoutStateSource`, `IPortableBitmapSourcePixelsSource`, and backend-owned `IProGpuTextureSource` seams as the pattern for future cleanup: expose narrow typed contracts from source-built WPF internals, keep DTOs package-neutral unless they intentionally carry ProGPU backend resources, and update tests to assert that hot-path readers do not use `System.Reflection`, `BindingFlags`, property probing, or duck-typed fake shapes. Visual and drawing replay plus retained invalidation should snapshot source-built offset, transform, clip, scroll clip, opacity, opacity-mask, content/descendant bounds, render size, clip-to-bounds, layout-clip state, geometry-drawing geometry/brush/pen, image-drawing source/rect, glyph-run drawing brush/run, glyph-run font/metrics/positions, drawing-group bounds/children/effects/render options, media-resource invalidation subscriptions, built-in and legacy bitmap-effect parameters/input state, shader-effect pixel bytecode/register/sampler kind/image-source state, shader-effect brush-sampler source bounds, and tile/image/drawing/visual brush state through those typed DTOs before any transitional fallback. Brush/resource cleanup must keep solid, gradient, tile brush, geometry-drawing, image-drawing, glyph-run drawing, glyph-run resource adaptation, drawing-group, and shader-effect sampler state on typed DTO/native ProGPU paths first, then remove the remaining transitional reflection fallbacks.

Normal WPF managed code reuse remains the goal. Modify upstream WPF managed code only where necessary to expose portable seams, replace Windows-only calls, or route native rendering/platform work into ProGPU and Silk.NET. Retained dependency registration should remain typed, streamed, scratch-backed, and hash-backed; do not reintroduce per-registration dependency lists, reflection probes, fresh visited-set allocation, or per-source visual-list scans on the normal branch registration path. Retained branch source-to-visual maps should store the common one-visual mapping in compact value-type storage and promote to a list only when a source genuinely owns multiple visuals; do not allocate `List<ProGpuVisual>` for normal one-visual Xceed/DataGrid cell branches. Retained branch source/source-owner/dependency maps should store the common one-owner visual mapping in compact value-type reference-set storage and promote to hash-backed storage only when a visual is genuinely shared; do not allocate per-visual `HashSet<object>` owners or wrapper owner-set objects for normal Xceed/DataGrid cell branches. Retained branch unregister cleanup should direct-remove one-visual source mappings instead of scanning a source visual list; reserve list removal for multi-visual mappings. Retained branch replay target filtering should use map-owned target-visual scratch plus parent-chain checks, not nested candidate scans, so Xceed/DataGrid scroll invalidations stay proportional to dirty branch count and visual depth. WPF hosts should prepare dirty retained-branch replay targets once per frame with the active frame image-source adapter and pass that stable target list into replay; do not re-walk dirty sources or rebuild shader-sampler image-source adapters during the replay call. Single dirty-branch replay should reuse map-owned single-target storage and index through `IReadOnlyList<WpfRetainedVisualBranchReplayTarget>` consumers instead of allocating a one-element target array or enumerator per scroll invalidation. Retained branch invalidation should skip source-owner enumeration for one-owner branches and reserve shared-owner/conflict scans for multi-owner branches. Retained version polling should capture portable visual state and visual-child topology in one scratch-backed traversal when the tracker is not already dirty; do not restore separate full graph walks for visual-state and child-topology change detection. Diagnostic tracked-dependency enumeration should follow the same scratch-backed traversal rule and only allocate the durable result list it returns. Tile-brush replay should stream value-type indexed tile ranges into native clip/transform/draw commands instead of materializing per-fill tile arrays/lists or using a custom tile enumerator/`foreach`, and visual-brush tile replay should reuse one `WpfVisualTreeRenderer` plus image-source adapter per fill instead of allocating them for every tile. MIL render-data decode should keep push/pop tracking on stack-local or pooled scratch storage rather than allocating a `Stack<bool>` per replay. ProGPU composition command sinks should keep push, transform, guideline, hit-test-owner, bitmap-scaling, edge-mode, and text-mode scopes on inline or pooled storage instead of allocating eager `Stack<T>` objects per retained visual scope; direct Y1/Y2 guideline pushes should store Y coordinates inline rather than allocating one/two-element arrays. Retained composition command sinks should use that same inline/pool-backed storage for delegate/effect/cache and visual-scope stacks instead of allocating eager `Stack<T>` objects per retained replay sink. Retained visual bounds accumulation should use inline/pool-backed scope storage for push, transform, and clip state, and must keep managed clip pops scope-balanced even when bounds cannot be reduced to a retained rectangle. ProGPU GPU hit-test index construction should pass spans over collected primitive/path-segment lists into the durable index builder instead of allocating temporary arrays first, and BVH split scratch should allocate retained/child primitive lists lazily instead of creating a retained list plus four child lists for every split candidate. Quadtree child-slot state should stay fixed-size in locals rather than allocating a per-node tuple array, the root primitive range should stay implicit instead of materializing a root index list, and ProGPU.Vector cleanup buffers such as PathAtlas temporary bind-group release lists, active-path repack storage, and atlas clear uploads should use vector-owned pooled buffers. ProGPU static-DXF compilation should rent temporary draw-call lists from the compositor pool and only materialize durable arrays for `DxfStaticBuffer` ownership. Dynamic GPU line/scatter series fallback uploads should flow through `ReadOnlySpan<float>` into `GpuSeriesBuffer`, with pooled scratch only for 2D scatter expansion. Scene-extension compile paths should use empty spans for missing local command data instead of allocating empty lists. WPF shader-effect, image-effect, ShaderToy, CAD/3D, grid, line, and hatch pipeline creation should pass span-backed vertex layouts into `RenderPipelineCache` and use stack-backed vertex descriptors instead of per-pipeline managed layout arrays or unmanaged `HGlobal` attribute buffers. DirectX, SciChart, diagnostics, and compute/texture readback paths should prefer ProGPU backend span/caller-buffer read APIs; array-returning read APIs are compatibility wrappers and should not be the default product path. DirectX texture shadow synchronization should read backend subresources directly into shadow spans, including individual array slices via readback origin layers, rather than allocating temporary full-mip pixel arrays. `WgpuContext` pending-resource cleanup should use context-owned dedupe scratch and pooled pointer snapshots, because it is the shared WebGPU lifetime drain for WPF, WinUI, Avalonia, DirectX, SciChart, texture readback, and compositor caches.

Retained invalidation events should request a subscription refresh and coalesce the actual graph rebuild until the dirty pass is consumed. Do not clear and resubscribe the full WPF graph immediately for every Xceed/DataGrid property, collection, or portable invalidation event.

Retained invalidation version-change batches should keep `_changedSources` on a list-owned indexed path. Bulk-add every changed source to the dirty-source set before raising `Invalidated`, so the composition target sees the complete dirty batch when it prepares retained replay targets. Do not call the public per-source `MarkDirty(...)` helper from the indexed batch loop, do not pass `_changedSources` through the generic `IEnumerable<object>` dirty-marking overload, and do not reintroduce enumerable dispatch for this Xceed/DataGrid retained replay path.

Retained invalidation state comparison should keep `_visualStateSnapshots`, `_currentVisualStateSnapshots`, and `_visualChildrenSnapshots` on concrete `Dictionary<...>` helper signatures. Do not widen these hot version-change comparison helpers back to `IReadOnlyDictionary<...>` or other interface-typed dictionary traversal.

Geometry bounds inference in WPF replay should use indexed loops over known list/array shapes. Do not reintroduce enumerator-based `foreach` traversal for direct line/polyline segment bounds in Xceed/DataGrid geometry replay paths.

Local media rectangle path classification is a clip/primitive hot path for Xceed/DataGrid scrolling. `WpfMediaRectangleClipReader` should keep exact rectangle-path detection on direct point locals plus explicit corner/edge checks; do not reintroduce `Point[4]` allocation or array-index wraparound traversal just to classify a four-corner rectangle path.

Portable rectangle path classification should follow the same shape. `WpfPortableRectangleClipReader` feeds retained visual/layout clips, portable geometry bounds, DrawingGroup clips, generated/object clips, and MIL decode, so keep it on direct `PortablePoint` locals with explicit unique-corner and edge checks; do not allocate `PortablePoint[4]` or accept retraced/repeated-corner paths as broad rectangle clips.

Portable path geometry traversal should stay indexed over `PortableGeometryPath.Figures` and `PortablePathFigure.Segments`. Do not reintroduce `foreach` in `WpfPortablePathBoundsReader`, `WpfPortablePathGeometryConverter`, or the portable geometry adaptation path in `WpfResourceResolver`; those array-backed loops sit on retained replay, native clip, tile-brush, and DataGrid grid-line geometry paths.

WPF command-sink guideline snapping should walk `_guidelineStack` through `SmallValueStack<T>.PeekAtDepth(...)` with indexed loops. Do not reintroduce `foreach (var guideline in _guidelineStack)` in the text/grid guideline snapping hot path.

WPF shader-effect sampler hot paths should index the source-built `PortableShaderEffect.Samplers` arrays directly. Keep `WpfEffectMapper` validation/adaptation and `WpfVisualInvalidationTracker` dependency visiting on cached local sampler arrays with `for` loops, not `foreach`; command-sink guideline arrays should use the same indexed-loop shape.

Retained invalidation graph traversal should branch on `source is IEnumerable collection` only when the source is actually enumerable. Do not route ordinary visual/resource nodes through `Array.Empty` enumerable helpers, and do not reintroduce `EnumerateCollection(...)` in subscription, version-polling, tracked-dependency, or dependency-registration traversal loops.

Retained dirty-branch replay target selection should build directly into map-owned reusable single-target and multi-target result storage. Do not allocate `WpfRetainedVisualBranchReplayTarget[]` arrays for normal Xceed/DataGrid multi-branch scroll invalidations, and do not copy scratch targets into a second replay-target snapshot after filtering.

Reference-equality dirty-source sets from `WpfVisualInvalidationTracker` should hit direct `HashSet<object>` replay/invalidation helpers before generic `IReadOnlyCollection<object>` handling. Keep one-source tracker frames on the `LastDirtySource` hint plus concrete `HashSet<object>.Contains(...)` and the single-target/single-invalidation fast paths, falling back to set enumeration only when the hint is absent or stale instead of routing them through generic collection enumeration.

ProGPU WPF primitive command sinks should skip null-material primitives before enqueueing native draw commands. Keep null brush/pen conversion on local fast paths so Xceed/DataGrid fill-only, line-only, and no-op chrome records do not call the resolver or add empty draw commands.

ProGPU WPF command sinks should keep identity scopes out of the native command stream. Opacity scopes that are effectively `1.0`, native identity transforms, and successfully adapted WPF identity transforms should push `PushKind.NoOp` instead of enqueueing native push/pop commands; unsupported transforms must keep the fallback path.

Retained branch-map shared-owner checks should stay centralized on `ReferenceOwnerSet.ClassifyAgainst(...)`: use the O(1) hash/count path for a single dirty source and one tight early-exit pass for multi-source dirty sets. Do not reintroduce ad hoc `foreach (var sourceOwner in sourceOwners)` loops in invalidation, and keep top-level replay target filtering indexed over the scratch target list.

WPF visual-state replay should cache per-visual bounds inside `PushVisualState`. Opacity masks, shader effects, bitmap effects, and cache scopes may all need the same retained bounds, so use a single lazy `TryGetVisualStateBounds(...)` helper instead of calling `TryReadOpacityMaskBounds(visual, ...)` separately for each scope.

WPF drawing-group replay should do the same for scope bounds inside `TryReplayDrawingGroup`. Opacity masks, shader effects, bitmap effects, and cache scopes should share one lazy `TryGetDrawingGroupScopeBounds(...)` result instead of separate effect/cache/mask helpers that each infer child content bounds.

WPF drawing-group child replay and drawing-group content-bounds inference should index `IPortableDrawingGroupChildrenSource`/`PortableDrawingGroupState.Children` directly. Do not reintroduce `ExtractChildren(drawingGroup, ...)` foreach traversal or `PortableDrawingGroupChildrenEnumerable`/`PortableDrawingGroupChildrenEnumerator` wrappers in `WpfDrawingReplay`; large Xceed/DataGrid drawing trees should avoid child enumerator dispatch while preserving the typed source-built WPF drawing-group seam.

Render-data dependent-resource snapshots and MIL resource token setup should stay indexed and allocation-light. Use `Array.Empty<object?>()` for zero dependent resources, pass `IReadOnlyList<object?>` through render replay, retained dependency registration, invalidation dependency visiting, and resolver/registry construction, and avoid `IEnumerable<object?>`/`foreach` dependent-resource traversal on Xceed/DataGrid cell render-data hot paths.

MIL render-data decode should avoid tiny per-record helper arrays and segment enumerators. Keep unsupported-animation-handle counting on fixed-arity overloads instead of `params int[]`, and replay primitive polyline segments through indexed `IReadOnlyList<WpfReplayLineSegment>` loops instead of `foreach`.

Glyph-run bounds inference is a text-heavy Xceed/DataGrid replay hot path. Cache glyph origin, font size, and `GlyphPositions` arrays, prefer exact-sized cached `PortableNativeGlyphRun.GlyphPositions` arrays without copying, then use indexed loops in ProGPU command sinks, retained visual bounds accumulation, and drawing replay bounds inference. Do not reintroduce `foreach (var position in glyphRun.GlyphPositions)` in these paths.

Cached-brush glyph coverage and relative material mapping must use `PortableGlyphRun.HasInkBounds`/`InkBounds` or `PortableNativeGlyphRun.HasInkBounds`/`InkBounds`. These bounds include baseline origin and precede the glyph transform. Source-built GlyphRun owns computing and caching them; adapters must include bounds availability/value in their cache key. Do not clip cached glyph coverage with font-size/advance boxes or silently invent bounds when the typed descriptor lacks ink metadata. Keep coverage as a retained glyph-run command and preserve caller-owned glyph arrays; ProGPU owns coverage masks, source leases and GPU composition.
