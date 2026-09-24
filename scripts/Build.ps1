[CmdletBinding()]
param(
    [switch]$DevelopmentInstaller,
    [switch]$ReleaseInstaller,
    [switch]$IncludeArm64,
    [string]$Go = 'go',
    [string]$MakeNsis = 'makensis'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = Split-Path $PSScriptRoot -Parent
if ([Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne [Runtime.InteropServices.Architecture]::X64) {
    throw 'This build script currently requires an x64 Windows build machine. It can produce both x64 and ARM64 packages.'
}
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
    Invoke-Checked 'dotnet' @('restore', $project, '--locked-mode', '-p:RuntimeIdentifier=win-x64',
        '-p:Configuration=Release', '-p:SelfContained=true', '-p:PublishSingleFile=true')
    Invoke-Checked 'dotnet' @('publish', $project, '-c', 'Release', '-r', 'win-x64',
        '--no-restore', '--self-contained', 'true', '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true', '-o', $publish)
    $report = Join-Path $repoRoot 'artifacts\self-test.json'
    $test = Start-Process -FilePath (Join-Path $publish 'MicWeave.exe') -ArgumentList @('--self-test', '--output', ('"' + $report + '"')) -WindowStyle Hidden -Wait -PassThru
    if ($test.ExitCode -ne 0) { throw 'MicWeave self-tests failed. See artifacts/self-test.json.' }
    $checks = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
    if (-not $checks.passed -or $checks.tests.Count -lt 12) { throw 'Self-test results are incomplete.' }

    $payloads = @($publish)
    if ($IncludeArm64) {
        $armPublish = Join-Path $repoRoot 'artifacts\publish-arm64'
        if (Test-Path -LiteralPath $armPublish) { throw 'artifacts/publish-arm64 already exists. Move that previous build aside first.' }
        $armAssets = Join-Path $repoRoot 'artifacts\build-assets-arm64'
        [void](New-Item -ItemType Directory -Path $armAssets -Force)
        $armTransport = Join-Path $armAssets 'MicWeave.UsbTransport.Setup.exe'
        if (-not (Test-Path -LiteralPath $armTransport)) {
            Invoke-WebRequest -Uri 'https://github.com/vadimgrn/usbip-win2/releases/download/v.0.9.8.0/USBip-0.9.8.0-arm64.exe' -OutFile $armTransport
        }
        if ((Get-FileHash -LiteralPath $armTransport -Algorithm SHA256).Hash -ne 'fa5dab657380ed99d7ed953010aae79ad9be478eddfeb2d72c22309fb48bc35e' -or
            (Get-AuthenticodeSignature -LiteralPath $armTransport).Status -ne 'Valid') {
            throw 'ARM64 transport did not pass its pinned hash and signature checks.'
        }
        $previousGoArch = $env:GOARCH
        $previousGoOs = $env:GOOS
        Push-Location (Join-Path $repoRoot 'src\MicWeave.VirtualUsb')
        try {
            $env:GOARCH = 'arm64'; $env:GOOS = 'windows'
            Invoke-Checked $Go @('build', '-trimpath', '-ldflags', '-s -w', '-o', (Join-Path $armAssets 'MicWeave.VirtualUsb.exe'), './cmd/micweavevirtualusb')
        } finally { $env:GOARCH = $previousGoArch; $env:GOOS = $previousGoOs; Pop-Location }
        Invoke-Checked 'dotnet' @('restore', $project, '--locked-mode', '-p:RuntimeIdentifier=win-arm64',
            '-p:Configuration=Release', '-p:SelfContained=true', '-p:PublishSingleFile=true', ('-p:TransportAssetsDirectory=' + $armAssets))
        Invoke-Checked 'dotnet' @('publish', $project, '-c', 'Release', '-r', 'win-arm64',
            '--no-restore', '--self-contained', 'true', '-p:PublishSingleFile=true',
            '-p:IncludeNativeLibrariesForSelfExtract=true', ('-p:TransportAssetsDirectory=' + $armAssets), '-o', $armPublish)
        $payloads += $armPublish
        Write-Warning 'ARM64 was cross-compiled only. Native ARM64 installation, audio and lifecycle tests are still required before release.'
    }

    if ($DevelopmentInstaller) {
        $installer = Join-Path $repoRoot 'artifacts\MicWeave-Development-0.1.0.exe'
        $ownership = Join-Path $repoRoot 'artifacts\InstallerPayload.nsh'
        & (Join-Path $PSScriptRoot 'Write-InstallerPayload.ps1') -PayloadDirectories $payloads -OutputFile $ownership
        $installerArguments = @('/DDEVELOPMENT_BUILD', ('/DPUBLISH_DIR=' + $publish), ('/DOUTPUT_FILE=' + $installer), ('/DPAYLOAD_MANIFEST=' + $ownership))
        if ($IncludeArm64) { $installerArguments += '/DARM64_PUBLISH_DIR=' + $armPublish }
        Push-Location (Join-Path $repoRoot 'packaging')
        try {
            Invoke-Checked $MakeNsis ($installerArguments + 'MicWeave.nsi')
        } finally { Pop-Location }
        Get-FileHash -LiteralPath $installer -Algorithm SHA256 | Format-List
    }
    Write-Host 'Build and twelve x64 self-tests passed. Development payload:' $publish
} finally { Pop-Location }
