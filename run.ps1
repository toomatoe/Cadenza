param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [switch]$NoRestore,
    [switch]$UiOnly
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Install the .NET 10 SDK, then reopen PowerShell.' }
$projectPath = Join-Path $PSScriptRoot 'desktop\Cadenza.Desktop.csproj'
[string[]]$properties = @("-p:Configuration=$Configuration", '-p:Platform=x64')
if ($UiOnly) { $properties += '-p:BuildNativeAudio=false' }

# Ask MSBuild for the actual output rather than hard-coding a bin directory.
$targetOutput = & dotnet msbuild $projectPath @properties -getProperty:TargetPath -nologo
if ($LASTEXITCODE -ne 0) { throw 'Could not resolve the desktop output path.' }
$assemblyPath = ($targetOutput -join [Environment]::NewLine).Trim()
$executablePath = [IO.Path]::ChangeExtension($assemblyPath, '.exe')
$desktopBin = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'desktop\bin')) + [IO.Path]::DirectorySeparatorChar
if (-not [IO.Path]::GetFullPath($executablePath).StartsWith($desktopBin, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Unexpected desktop output path: $executablePath"
}

# Restart only instances using this project's selected build output.
foreach ($instance in @(Get-Process -Name Cadenza.Desktop -ErrorAction SilentlyContinue)) {
    if ($instance.Path -and [IO.Path]::GetFullPath($instance.Path).Equals([IO.Path]::GetFullPath($executablePath), [StringComparison]::OrdinalIgnoreCase)) {
        Write-Host "Closing Cadenza process $($instance.Id) so its build can be updated."
        if (-not $instance.CloseMainWindow() -or -not $instance.WaitForExit(3000)) {
            Stop-Process -Id $instance.Id -ErrorAction SilentlyContinue
        }
    }
}
[string[]]$restoreArguments = if ($NoRestore) { @('--no-restore') } else { @() }
& dotnet build $projectPath @properties @restoreArguments
if ($LASTEXITCODE -ne 0) { throw 'Build failed. Cadenza was not launched; the old output will not be used.' }
if (-not (Test-Path -LiteralPath $executablePath)) { throw "Built executable not found: $executablePath" }
Write-Host "Launching $executablePath"
$started = Start-Process -FilePath $executablePath -WorkingDirectory (Split-Path -Parent $executablePath) -PassThru
Write-Host "Cadenza process: $($started.Id)"
