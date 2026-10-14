param([switch]$Publish, [switch]$UiOnly, [switch]$NoRestore, [string]$Version = '')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'The desktop build requires Windows. Core and native tests can run separately on other platforms.' }
if ($Publish -and $UiOnly) { throw 'UiOnly cannot publish a complete app. Use -UiOnly to build the interface, or -Publish with the native prerequisites installed.' }
if ($Version -and $Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z][0-9A-Za-z.-]*)?$') { throw 'Version must look like 0.1.0 or 0.1.0-beta.1.' }
[string[]]$desktopArguments = if ($Version) { @("-p:Version=$Version") } else { @() }
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
        Invoke-Checked { dotnet test tests/Cadenza.UnitTests.csproj -c Release -nodeReuse:false --filter "TestCategory!=Native" @restoreArguments }
        Invoke-Checked { dotnet build desktop/Cadenza.Desktop.csproj -c Release -p:Platform=x64 -p:BuildNativeAudio=false @restoreArguments @desktopArguments }
        Write-Host 'UI build complete. Playback requires the native audio engine.'
        Write-Host 'Launch: & .\desktop\bin\x64\Release\net10.0-windows10.0.19041.0\win-x64\Cadenza.Desktop.exe'
        return
    }
    Invoke-Checked { cargo fmt --manifest-path native/Cargo.toml --check }
    Invoke-Checked { cargo test --locked --features playback --manifest-path native/Cargo.toml }
    Invoke-Checked { cargo build --locked --release --features playback --manifest-path native/Cargo.toml }
    Invoke-Checked { dotnet build tests/Cadenza.UnitTests.csproj -c Release -nodeReuse:false @restoreArguments }
    Copy-Item native/target/release/cadenza_audio.dll tests/bin/Release/net10.0/ -Force
    Invoke-Checked { dotnet test tests/Cadenza.UnitTests.csproj -c Release -nodeReuse:false --no-build }
    Invoke-Checked { dotnet build desktop/Cadenza.Desktop.csproj -c Release -p:Platform=x64 @restoreArguments @desktopArguments }
    if ($Publish) {
        # Publish into a clean directory so removed files cannot leak into a release.
        $publishDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'artifacts/windows-x64'))
        $artifactsDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'artifacts')) + [IO.Path]::DirectorySeparatorChar
        if (-not $publishDirectory.StartsWith($artifactsDirectory, [StringComparison]::OrdinalIgnoreCase)) { throw 'Publish directory must stay inside artifacts.' }
        if (Test-Path -LiteralPath $publishDirectory) { Remove-Item -LiteralPath $publishDirectory -Recurse -Force }
        Invoke-Checked { dotnet publish desktop/Cadenza.Desktop.csproj -c Release -r win-x64 --self-contained true -p:Platform=x64 -o $publishDirectory @restoreArguments @desktopArguments }
        foreach ($requiredFile in @('Cadenza.Desktop.exe', 'Cadenza.Desktop.dll', 'cadenza_audio.dll', 'App.xbf', 'Views/MainWindow.xbf', 'Cadenza.Desktop.pri')) {
            if (-not (Test-Path -LiteralPath (Join-Path $publishDirectory $requiredFile))) { throw "Published app is incomplete: $requiredFile is missing." }
        }
        Copy-Item README.md $publishDirectory -Force
        Copy-Item docs/TESTER-START-HERE.txt $publishDirectory -Force
        $archivePath = Join-Path $PSScriptRoot 'artifacts/Cadenza-windows-x64.zip'
        Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $archivePath -Force
        $checksum = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
        Set-Content -LiteralPath "$archivePath.sha256" -Value "$checksum  Cadenza-windows-x64.zip" -Encoding ascii
        Write-Host "Portable package: $archivePath"
        $installerVersion = if ($Version) { $Version } else { '0.1.0-dev' }
        & (Join-Path $PSScriptRoot 'scripts/Build-Installer.ps1') -Version $installerVersion -PublishDirectory $publishDirectory -InstallCompiler

    }
} finally { Pop-Location }
