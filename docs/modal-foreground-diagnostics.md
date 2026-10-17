# Hosted modal foreground diagnostics

The original paired run `38985359824` completed all seven Microsoft scenarios,
then rejected portable input at the MessageBox capture because another process
owned the foreground. Original native records retain a disabled owner and its
enabled, correctly owned MessageBox; source snapshots then show both inactive.
Those observations do not identify the foreground recipient or establish cause.

The separate `DIAGNOSTIC ONLY — unchanged original modal pair` job, selected by
`foreground_diagnostic: true` in the existing modal workflow, stages the original pair
from an exact whole-qualified producer. Its automation checkout must match that
producer, retaining the existing package, source and output-DLL checks. A separate
checkout supplies only a read-only foreground observer. The original controller,
application, input helper, seven scenarios, 60-second process deadlines and
64-action bounds remain unchanged. Input tracing stays disabled.

The observer records foreground handle, process/thread, title and class changes,
including a second handle read that exposes non-atomic observations. It never
sends input, activates windows, changes DPI or repairs focus. Its independent
150-second observation limit and 256-change/512-KiB receipt budgets do not extend
the original controller's deadlines. Original failures and owned cleanup remain
authoritative; observer failures are recorded separately.

Every observer receipt is explicitly diagnostic and unqualified, even if the
original workload happens to complete. Sampling may affect timing and can miss
short transitions. Diagnose the retained original failure; do not use this
workflow as release acceptance or retry until a passing result. Final acceptance
still uses the ordinary matching-head original modal workflow without this
observer, followed by raw-capture review.
