[CmdletBinding()]
param(
    [switch]$DevelopmentInstaller,
    [switch]$ReleaseInstaller,
    [string]$Go = 'go',
    [string]$MakeNsis = 'makensis'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = Split-Path $PSScriptRoot -Parent
if ($ReleaseInstaller) {
    throw 'Public installers are blocked: resolve docs/DEVICE-IDENTITY.md and complete docs/RELEASING.md first.'
}
function Invoke-Checked([string]$Executable, [string[]]$Arguments) {
    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Executable failed with exit code $LASTEXITCODE" }
}
Push-Location $repoRoot
try {
    $publish = Join-Path $repoRoot 'artifacts\publish'
    if (Test-Path -LiteralPath $publish) {
        throw 'artifacts/publish already exists. Move that previous build aside before building a fresh payload.'
    }
    $assets = Join-Path $repoRoot 'src\GameMusicShare.App\Assets'
    Push-Location (Join-Path $repoRoot 'src\MicWeave.VirtualUsb')
    try {
        Invoke-Checked $Go @('test', './...')
        Invoke-Checked $Go @('vet', './...')
        Invoke-Checked $Go @('build', '-trimpath', '-ldflags', '-s -w',
            '-o', (Join-Path $assets 'MicWeave.VirtualUsb.exe'), './cmd/micweavevirtualusb')
    } finally { Pop-Location }

    $transport = Join-Path $assets 'MicWeave.UsbTransport.Setup.exe'
    $expected = '81f426741f7ee2ed991febe24a22daca8400b6ae2f171054e3fb404897e15d39'
    if (-not (Test-Path -LiteralPath $transport)) {
        Invoke-WebRequest -Uri 'https://github.com/vadimgrn/usbip-win2/releases/download/v.0.9.8.0/USBip-0.9.8.0-x64.exe' -OutFile $transport
    }
    if ((Get-FileHash -LiteralPath $transport -Algorithm SHA256).Hash -ne $expected) {
        throw 'USB transport hash mismatch. Do not run or redistribute this file.'
    }
    if ((Get-AuthenticodeSignature -LiteralPath $transport).Status -ne 'Valid') {
        throw 'USB transport signature could not be validated on this machine.'
    }

    $project = 'src\GameMusicShare.App\GameMusicShare.App.csproj'
    # Restore the same runtime and self-contained payload that publish uses.
    # Otherwise a machine without cached runtime packs fails with NETSDK1112.
    Invoke-Checked 'dotnet' @('restore', $project, '--locked-mode', '-r', 'win-x64',
        '-p:Configuration=Release', '-p:SelfContained=true', '-p:PublishSingleFile=true')
    Invoke-Checked 'dotnet' @('publish', $project, '-c', 'Release', '-r', 'win-x64',
        '--no-restore', '--self-contained', 'true', '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true', '-o', $publish)
    $report = Join-Path $repoRoot 'artifacts\self-test.json'
    $test = Start-Process -FilePath (Join-Path $publish 'MicWeave.exe') -ArgumentList @('--self-test', '--output', ('"' + $report + '"')) -WindowStyle Hidden -Wait -PassThru
    if ($test.ExitCode -ne 0) { throw 'MicWeave self-tests failed. See artifacts/self-test.json.' }
    $checks = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
    if (-not $checks.passed -or $checks.tests.Count -lt 10) { throw 'Self-test results are incomplete.' }

    if ($DevelopmentInstaller) {
        $installer = Join-Path $repoRoot 'artifacts\MicWeave-Development-0.1.0.exe'
        Push-Location (Join-Path $repoRoot 'packaging')
        try {
            Invoke-Checked $MakeNsis @('/DDEVELOPMENT_BUILD', ('/DPUBLISH_DIR=' + $publish), ('/DOUTPUT_FILE=' + $installer), 'MicWeave.nsi')
        } finally { Pop-Location }
        Get-FileHash -LiteralPath $installer -Algorithm SHA256 | Format-List
    }
    Write-Host 'Build and ten self-tests passed. Development payload:' $publish
} finally { Pop-Location }
