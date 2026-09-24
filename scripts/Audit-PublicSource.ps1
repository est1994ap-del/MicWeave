[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
Push-Location $repoRoot
try {
    $files = @(git ls-files)
    if ($LASTEXITCODE -ne 0 -or $files.Count -eq 0) { throw 'Initialize Git and stage the reviewed source first.' }
    $failures = @()
    foreach ($file in $files) {
        if ($file -match '(^|/)(bin|obj|publish|design|work|GameMusicShare.Driver|artifacts)/' -or
            $file -match '\.(exe|dll|pdb|zip|cab|pfx|p12|pem|key|log|dmp)$' -or
            $file -match '(^|/)(settings|diagnostics|.*-test)\.json$') {
            $failures += "Non-source/private artifact tracked: $file"
        }
        if ($file -match '\.(cs|go|xaml|csproj|json|md|ps1|yml|nsi|txt)$') {
            $content = Get-Content -LiteralPath $file -Raw
            if ($content -match '(?i)([A-Z]:\\Users\\|[A-Z]:\\AI-Data\\|gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{20,})' -or
                $content -match '(?m)^-----BEGIN [A-Z ]*PRIVATE KEY-----') {
                $failures += "Private path or credential pattern in $file"
            }
        }
    }
    foreach ($required in @('LICENSE', 'README.md', 'SECURITY.md', 'THIRD_PARTY_NOTICES.md',
        'docs/DEVICE-IDENTITY.md', 'docs/RELEASING.md', 'src/GameMusicShare.App/packages.lock.json')) {
        if ($required -notin $files) { $failures += "Missing release source file: $required" }
    }
    if ($failures.Count) { throw ($failures -join [Environment]::NewLine) }
    Write-Host "Public source audit passed for $($files.Count) tracked files."
    Write-Host 'This pattern scan is a guardrail, not proof that all sensitive content is absent. Review git diff --cached too.'
} finally { Pop-Location }
