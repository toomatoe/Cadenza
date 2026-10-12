param(
    [string]$Version = '0.1.0',
    [string]$PublishDirectory = '',
    [switch]$InstallCompiler
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$workspaceDirectory = Split-Path -Parent $PSScriptRoot
if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z][0-9A-Za-z.-]*)?$') { throw 'Use a version such as 0.1.0-beta.1.' }
if (-not $PublishDirectory) { $PublishDirectory = Join-Path $workspaceDirectory 'artifacts/windows-x64' }
$publishPath = (Resolve-Path -LiteralPath $PublishDirectory).Path
foreach ($requiredFile in @('Cadenza.Desktop.exe','Cadenza.Desktop.dll','cadenza_audio.dll','App.xbf','Views/MainWindow.xbf','Cadenza.Desktop.pri')) {
    if (-not (Test-Path -LiteralPath (Join-Path $publishPath $requiredFile))) { throw "Installer input is incomplete: $requiredFile" }
}
$toolDirectory = Join-Path $workspaceDirectory 'artifacts/tools/innosetup'
$compiler = Join-Path $toolDirectory 'ISCC.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $installedCompiler = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($installedCompiler) { $compiler = $installedCompiler.Source }
    elseif (Test-Path -LiteralPath 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe') { $compiler = 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe' }
    elseif ($InstallCompiler) {
        New-Item -ItemType Directory -Path (Split-Path -Parent $toolDirectory) -Force | Out-Null
        $compilerInstaller = Join-Path (Split-Path -Parent $toolDirectory) 'innosetup-6.7.3.exe'
        Invoke-WebRequest -Uri 'https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-6.7.3.exe' -OutFile $compilerInstaller
        $signature = Get-AuthenticodeSignature -LiteralPath $compilerInstaller
        if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notlike '*Pyrsys*') { throw 'Inno Setup signature verification failed.' }
        $installation = Start-Process -FilePath $compilerInstaller -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/CURRENTUSER','/NOICONS',('/DIR="' + $toolDirectory + '"')) -WindowStyle Hidden -PassThru -Wait
        if ($installation.ExitCode -ne 0) { throw "Inno Setup installation failed: $($installation.ExitCode)" }
    } else { throw 'Install Inno Setup 6, or add -InstallCompiler to download the verified compiler.' }
}
$outputDirectory = Join-Path $workspaceDirectory 'artifacts'
$fileVersion = ($Version -split '-')[0]
& $compiler "/DAppVersion=$Version" "/DFileVersion=$fileVersion" "/DSourceDir=$publishPath" "/DOutputDir=$outputDirectory" (Join-Path $workspaceDirectory 'installer/Cadenza.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
$installerFile = Join-Path $outputDirectory 'Cadenza-Setup.exe'
if (-not (Test-Path -LiteralPath $installerFile)) { throw 'Installer output missing.' }
$checksum = (Get-FileHash -LiteralPath $installerFile -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath "$installerFile.sha256" -Value "$checksum  Cadenza-Setup.exe" -Encoding ascii
Write-Host "Installer ready: $installerFile"
