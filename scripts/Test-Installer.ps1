[CmdletBinding()]
param([string]$MakeNsis = 'makensis')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = Split-Path $PSScriptRoot -Parent
$payload = Join-Path $repoRoot 'artifacts\publish'
$armPayload = Join-Path $repoRoot 'artifacts\publish-arm64'
$testDirectory = Join-Path $repoRoot ('artifacts\installer-test-' + [Guid]::NewGuid().ToString('N'))
$installDirectory = Join-Path $testDirectory 'Installed MicWeave'
$installer = Join-Path $testDirectory 'MicWeave-InstallerTest.exe'
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\MicWeave.InstallerTest'
$appKey = 'HKCU:\Software\MicWeave.InstallerTest'
if ((Test-Path $uninstallKey) -or (Test-Path $appKey)) { throw 'An earlier isolated installer test is still registered. Review it before running another test.' }
if (-not (Test-Path (Join-Path $payload 'MicWeave.exe'))) { throw 'Run Build.ps1 first.' }
[void](New-Item -ItemType Directory -Path $testDirectory)
$manifest = Join-Path $testDirectory 'Payload.nsh'
$payloads = @($payload)
if (Test-Path $armPayload) { $payloads += $armPayload }
& (Join-Path $PSScriptRoot 'Write-InstallerPayload.ps1') -PayloadDirectories $payloads -OutputFile $manifest
$arguments = @('/V2', '/DDEVELOPMENT_BUILD', ('/DPUBLISH_DIR=' + $payload), ('/DOUTPUT_FILE=' + $installer),
    ('/DPAYLOAD_MANIFEST=' + $manifest), ('/DTEST_ROOT=' + $installDirectory))
if (Test-Path $armPayload) { $arguments += '/DARM64_PUBLISH_DIR=' + $armPayload }
Push-Location (Join-Path $repoRoot 'packaging')
try { & $MakeNsis @arguments MicWeave.nsi; if ($LASTEXITCODE) { throw 'Test installer compilation failed.' } }
finally { Pop-Location }
$checks = [Collections.Generic.List[object]]::new()
function Assert-Test([string]$Name, [bool]$Pass) {
    $checks.Add(@{name=$Name; passed=$Pass})
    if (-not $Pass) { throw "FAILED: $Name" }
    Write-Host "PASS: $Name"
}
function Run-Setup([string]$Executable, [string[]]$Arguments) {
    $process = Start-Process -FilePath $Executable -ArgumentList $Arguments -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(60000)) { throw 'Test setup is still running. It was NOT forcibly terminated; review the test installation.' }
    return $process.ExitCode
}
$created = $false
$mutex = [Threading.Mutex]::new($false, 'Local\GameMusicShare.Standalone', [ref]$created)
try {
    if (-not $created) { throw 'Close MicWeave before this test. It has not been stopped.' }
    Assert-Test 'Running app blocks install before any files are written' ((Run-Setup $installer @('/S')) -eq 1618 -and -not (Test-Path $installDirectory))
} finally { $mutex.Dispose() }
try {
    Assert-Test 'Fresh per-user installation succeeds without installing a driver' ((Run-Setup $installer @('/S')) -eq 0)
    Assert-Test 'Installed Apps registration points to the isolated test directory' ((Get-ItemProperty $uninstallKey).InstallLocation -eq $installDirectory)
    $files = @(Get-ChildItem -LiteralPath $payload -File -Recurse)
    foreach ($file in $files) {
        $installedFile = Join-Path $installDirectory ([IO.Path]::GetRelativePath($payload, $file.FullName))
        if ((Get-FileHash -LiteralPath $installedFile).Hash -ne (Get-FileHash -LiteralPath $file.FullName).Hash) { throw "Installed file differs: $($file.Name)" }
    }
    Assert-Test 'Every installed x64 file matches the tested payload, including icon and illustrated guide' $true
    $selfTest = Join-Path $testDirectory 'installed-self-test.json'
    $code = Run-Setup (Join-Path $installDirectory 'MicWeave.exe') @('--self-test', '--output', ('"' + $selfTest + '"'))
    $report = Get-Content -LiteralPath $selfTest -Raw | ConvertFrom-Json
    Assert-Test 'Installed executable passes all twelve non-recording checks' ($code -eq 0 -and $report.passed -and $report.tests.Count -eq 12)

    $sentinel = Join-Path $installDirectory 'user-file-do-not-delete.txt'
    [IO.File]::WriteAllText($sentinel, 'This unrelated file must survive upgrade and uninstall.')
    $oldVersion = (Get-ItemProperty $uninstallKey).DisplayVersion
    try {
        Set-ItemProperty $uninstallKey DisplayVersion '99.0.0'
        Assert-Test 'An older installer refuses to overwrite a newer version' ((Run-Setup $installer @('/S')) -eq 1638)
    } finally { Set-ItemProperty $uninstallKey DisplayVersion $oldVersion }
    Assert-Test 'Same-version repair succeeds and preserves unrelated files' ((Run-Setup $installer @('/S')) -eq 0 -and (Test-Path $sentinel))

    # Run a copy outside the target so NSIS can remove the installed uninstaller.
    $uninstaller = Join-Path $testDirectory 'Uninstall-under-test.exe'
    Copy-Item -LiteralPath (Join-Path $installDirectory 'Uninstall.exe') -Destination $uninstaller
    $uninstallArguments = @('/S', ('_?=' + $installDirectory))
    $mutex = [Threading.Mutex]::new($false, 'Local\GameMusicShare.Standalone', [ref]$created)
    try {
        if (-not $created) { throw 'MicWeave started during the test; stop testing without interrupting it.' }
        Assert-Test 'Running app blocks uninstall' ((Run-Setup $uninstaller $uninstallArguments) -eq 1618)
    } finally { $mutex.Dispose() }
    $locked = [IO.File]::Open((Join-Path $installDirectory 'LICENSE'), [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try {
        Assert-Test 'In-use file keeps the uninstall entry available for retry' ((Run-Setup $uninstaller $uninstallArguments) -eq 1603 -and (Test-Path $uninstallKey))
    } finally { $locked.Dispose() }
    Assert-Test 'Retry completes uninstall after the file is released' ((Run-Setup $uninstaller $uninstallArguments) -eq 0)
    Assert-Test 'Uninstall removes only owned files and its test registration' ((Test-Path $sentinel) -and -not (Test-Path $uninstallKey) -and -not (Test-Path $appKey) -and @(Get-ChildItem -LiteralPath $installDirectory -File -Recurse).Count -eq 1)
} finally {
    @{ tests=$checks; scope='Isolated x64 app installer only; no driver installation or microphone capture'; passed=($checks.Count -eq 11 -and @($checks | Where-Object { -not $_.passed }).Count -eq 0) } |
        ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $testDirectory 'results.json')
    Write-Host "Test results: $testDirectory"
}
if ($checks.Count -ne 11) { throw 'Installer test results are incomplete.' }
