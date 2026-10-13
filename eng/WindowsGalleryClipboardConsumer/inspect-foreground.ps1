$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -cne 'true' -or $env:RUNNER_ENVIRONMENT -cne 'github-hosted' -or $env:RUNNER_OS -cne 'Windows' -or $env:GITHUB_REPOSITORY -cne 'wieslawsoltes/LibreWPF') { throw 'Disposable hosted LibreWPF runner required.' }
$receipt = Get-Content -Raw (Join-Path $env:RUNNER_TEMP 'caption-preflight.json') | ConvertFrom-Json
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$window = [System.Windows.Automation.AutomationElement]::FromHandle([IntPtr]$receipt.foreground.hwnd)
$queue = New-Object 'System.Collections.Generic.Queue[object]'
$queue.Enqueue(@($window, 0))
$nodes = New-Object 'System.Collections.Generic.List[object]'
$walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
$clock = [Diagnostics.Stopwatch]::StartNew()
while ($queue.Count -gt 0 -and $nodes.Count -lt 128 -and $clock.Elapsed.TotalSeconds -lt 15) {
    $item = $queue.Dequeue(); $element = $item[0]; $depth = [int]$item[1]
    $state = $element.Current
    $nodes.Add([ordered]@{ depth=$depth; name=$state.Name; controlType=$state.ControlType.ProgrammaticName; automationId=$state.AutomationId; enabled=$state.IsEnabled; offscreen=$state.IsOffscreen })
    if ($depth -lt 8) {
        $child = $walker.GetFirstChild($element)
        while ($null -ne $child -and $queue.Count -lt 128 -and $clock.Elapsed.TotalSeconds -lt 15) {
            $queue.Enqueue(@($child, $depth+1)); $child = $walker.GetNextSibling($child)
        }
    }
}
[ordered]@{ diagnosticOnly=$true; qualified=$false; inputSent=$false; clipboardAccessed=$false; hwnd=$receipt.foreground.hwnd; nodes=$nodes; truncated=($queue.Count -gt 0); elapsed=$clock.Elapsed.TotalSeconds } | ConvertTo-Json -Depth 8 | Set-Content -Encoding UTF8 (Join-Path $env:RUNNER_TEMP 'caption-foreground-ui.json')
