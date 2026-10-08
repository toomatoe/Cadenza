param([switch]$Publish, [switch]$UiOnly, [switch]$NoRestore)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'The desktop build requires Windows. Core and native tests can run separately on other platforms.' }
if ($Publish -and $UiOnly) { throw 'UiOnly cannot publish a complete app. Use -UiOnly to build the interface, or -Publish with the native prerequisites installed.' }
[string[]]$restoreArguments = if ($NoRestore) { @('--no-restore') } else { @() }
$cargoBin = Join-Path $env:USERPROFILE '.cargo\bin'
if (-not (Get-Command cargo -ErrorAction SilentlyContinue) -and (Test-Path -LiteralPath (Join-Path $cargoBin 'cargo.exe'))) {
    $env:PATH = $cargoBin + ';' + $env:PATH
}
$requiredTools = if ($UiOnly) { @('dotnet') } else { @('cargo', 'dotnet') }
foreach ($tool in $requiredTools) {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { throw "Missing $tool. Install the prerequisites listed in README.md, then reopen PowerShell." }
}
Push-Location $PSScriptRoot
try {
    function Invoke-Checked([scriptblock]$Command) {
        & $Command
        if ($LASTEXITCODE -ne 0) { throw "Build command failed with exit code $LASTEXITCODE" }
    }
    if ($UiOnly) {
        Invoke-Checked { dotnet build desktop/Cadenza.Desktop.csproj -c Release -p:Platform=x64 -p:BuildNativeAudio=false @restoreArguments }
        Write-Host 'UI build complete. Playback requires the native audio engine.'
        Write-Host 'Launch: & .\desktop\bin\x64\Release\net10.0-windows10.0.19041.0\win-x64\Cadenza.Desktop.exe'
        return
    }
    Invoke-Checked { cargo fmt --manifest-path native/Cargo.toml --check }
    Invoke-Checked { cargo test --locked --features playback --manifest-path native/Cargo.toml }
    Invoke-Checked { cargo build --locked --release --features playback --manifest-path native/Cargo.toml }
    Invoke-Checked { dotnet build tests/Cadenza.Tests.csproj -c Release @restoreArguments }
    Copy-Item native/target/release/cadenza_audio.dll tests/bin/Release/net10.0/ -Force
    Invoke-Checked { dotnet run --project tests/Cadenza.Tests.csproj -c Release --no-build -- --native }
    Invoke-Checked { dotnet build desktop/Cadenza.Desktop.csproj -c Release -p:Platform=x64 @restoreArguments }
    if ($Publish) {
        Invoke-Checked { dotnet publish desktop/Cadenza.Desktop.csproj -c Release -r win-x64 --self-contained true -p:Platform=x64 -o artifacts/windows-x64 @restoreArguments }
        if (-not (Test-Path artifacts/windows-x64/cadenza_audio.dll)) { throw 'Native DLL missing from published output.' }
        Copy-Item README.md artifacts/windows-x64/README.md -Force
        Compress-Archive -Path artifacts/windows-x64/* -DestinationPath artifacts/Cadenza-windows-x64.zip -Force
    }
} finally { Pop-Location }
