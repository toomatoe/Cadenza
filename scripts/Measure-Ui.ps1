param(
    [Parameter(Mandatory)][string]$Executable,
    [Parameter(Mandatory)][string]$Output,
    [ValidateSet('idle', 'scroll')][string]$Scenario = 'idle',
    [Alias('Seconds', 'Samples')][int]$SampleCount = 20,
    [switch]$IncludeGpu
)
$ErrorActionPreference = 'Stop'
$Executable = (Resolve-Path -LiteralPath $Executable).Path
if (Get-Process Cadenza.Desktop -ErrorAction SilentlyContinue) {
    throw 'Close Cadenza before measuring so each run starts a fresh process.'
}
$clock = [Diagnostics.Stopwatch]::StartNew()
$app = Start-Process -FilePath $Executable -WorkingDirectory (Split-Path $Executable) -WindowStyle Hidden -PassThru
$samples = [Collections.Generic.List[object]]::new()
$previousCpu = 0.0
$previousTime = 0.0
$windowReadyMs = $null
$scroller = $null
if ($Scenario -ne 'idle') { Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes }
try {
    for ($i = 0; $i -lt $SampleCount; $i++) {
        Start-Sleep -Seconds 1
        $app.Refresh()
        if ($app.HasExited) { throw "App exited with code $($app.ExitCode)." }
        $elapsed = $clock.Elapsed.TotalSeconds
        if ($null -eq $windowReadyMs -and $app.MainWindowHandle -ne 0) { $windowReadyMs = $clock.ElapsedMilliseconds }
        if ($Scenario -eq 'scroll' -and $i -ge 5) {
            if ($null -eq $scroller) {
                $window = [Windows.Automation.AutomationElement]::FromHandle($app.MainWindowHandle)
                $library = $window.FindFirst([Windows.Automation.TreeScope]::Descendants,
                    (New-Object Windows.Automation.PropertyCondition ([Windows.Automation.AutomationElement]::AutomationIdProperty), 'LibraryCards'))
                if ($null -eq $library) {
                    $library = $window.FindFirst([Windows.Automation.TreeScope]::Descendants,
                        (New-Object Windows.Automation.PropertyCondition ([Windows.Automation.AutomationElement]::NameProperty), 'Library album covers'))
                }
                if ($null -eq $library) { throw "Library is unavailable in window '$($window.Current.Name)' (handle $($app.MainWindowHandle))." }
                $scroller = $library.GetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern)
                if (-not $scroller.Current.VerticallyScrollable) { throw 'Load enough library content to scroll before measuring.' }
            }
            $direction = if ($i % 10 -lt 5) { [Windows.Automation.ScrollAmount]::LargeDecrement } else { [Windows.Automation.ScrollAmount]::LargeIncrement }
            $scroller.Scroll([Windows.Automation.ScrollAmount]::NoAmount, $direction)
        }
        $gpu = $null
        if ($IncludeGpu) {
            try {
                $counters = Get-Counter '\GPU Engine(*)\Utilization Percentage' -ErrorAction Stop
                $matching = @($counters.CounterSamples | Where-Object { $_.InstanceName -like "pid_$($app.Id)_*" })
                if ($matching.Count) { $gpu = ($matching | Measure-Object CookedValue -Sum).Sum }
            } catch { $gpu = $null }
        }
        $elapsed = $clock.Elapsed.TotalSeconds
        $app.Refresh()
        $cpu = $app.TotalProcessorTime.TotalSeconds
        $samples.Add([pscustomobject]@{
            Scenario = $Scenario
            ElapsedSeconds = [Math]::Round($elapsed, 3)
            PrivateBytes = $app.PrivateMemorySize64
            WorkingSet = $app.WorkingSet64
            CpuSeconds = $cpu
            CpuPercentOneCore = [Math]::Round(100 * ($cpu - $previousCpu) / ($elapsed - $previousTime), 3)
            WindowDetectedMs = $windowReadyMs
            GpuEnginePercentSum = $gpu
        })
        $previousCpu = $cpu
        $previousTime = $elapsed
    }
    $samples | Export-Csv -LiteralPath $Output -NoTypeInformation
    $samples | Select-Object -Last 1 | ConvertTo-Json
} finally {
    if (-not $app.HasExited) {
        if (-not $app.CloseMainWindow() -or -not $app.WaitForExit(3000)) { $app.Kill() }
    }
}
