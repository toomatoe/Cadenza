param([switch]$Publish)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'The desktop build requires Windows. Core and native tests can run separately on other platforms.' }
foreach ($tool in @('cargo', 'dotnet')) {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { throw "Missing $tool. Install the prerequisites listed in README.md, then reopen PowerShell." }
}
Push-Location $PSScriptRoot
try {
    function Invoke-Checked([scriptblock]$Command) {
        & $Command
        if ($LASTEXITCODE -ne 0) { throw "Build command failed with exit code $LASTEXITCODE" }
    }
    Invoke-Checked { cargo fmt --manifest-path native/Cargo.toml --check }
    Invoke-Checked { cargo test --locked --features playback --manifest-path native/Cargo.toml }
    Invoke-Checked { cargo build --locked --release --features playback --manifest-path native/Cargo.toml }
    Invoke-Checked { dotnet build tests/Cadenza.Tests.csproj -c Release }
    Copy-Item native/target/release/cadenza_audio.dll tests/bin/Release/net10.0/ -Force
    Invoke-Checked { dotnet run --project tests/Cadenza.Tests.csproj -c Release --no-build -- --native }
    Invoke-Checked { dotnet build desktop/Cadenza.Desktop.csproj -c Release -p:Platform=x64 }
    if ($Publish) {
        Invoke-Checked { dotnet publish desktop/Cadenza.Desktop.csproj -c Release -r win-x64 --self-contained true -p:Platform=x64 -o artifacts/windows-x64 }
        if (-not (Test-Path artifacts/windows-x64/cadenza_audio.dll)) { throw 'Native DLL missing from published output.' }
        Copy-Item README.md artifacts/windows-x64/README.md -Force
        Compress-Archive -Path artifacts/windows-x64/* -DestinationPath artifacts/Cadenza-windows-x64.zip -Force
    }
} finally { Pop-Location }
