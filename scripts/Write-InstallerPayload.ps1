[CmdletBinding()]
param(
    [Parameter(Mandatory)][string[]]$PayloadDirectories,
    [Parameter(Mandatory)][string]$OutputFile
)
$ErrorActionPreference = 'Stop'
$ownedFiles = [System.Collections.Generic.SortedSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$ownedDirectories = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($payload in $PayloadDirectories) {
    $root = (Resolve-Path -LiteralPath $payload).Path
    foreach ($file in Get-ChildItem -LiteralPath $root -File -Recurse) {
        $relative = [IO.Path]::GetRelativePath($root, $file.FullName)
        if ($relative -match '[\x00-\x1f$"!;]' -or $relative.StartsWith('..') -or [IO.Path]::IsPathRooted($relative)) {
            throw "Unsafe installer payload path: $relative"
        }
        [void]$ownedFiles.Add($relative)
        $parent = Split-Path $relative -Parent
        while ($parent) { [void]$ownedDirectories.Add($parent); $parent = Split-Path $parent -Parent }
    }
}
if (-not $ownedFiles.Contains('MicWeave.exe') -or -not $ownedFiles.Contains('LICENSE')) { throw 'Incomplete application payload.' }
$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add('; Generated from the exact payload. Never delete unknown files or shared drivers.')
$lines.Add('!macro RemoveOwnedPayload')
foreach ($file in $ownedFiles) { $lines.Add('  Delete "$INSTDIR\' + $file + '"') }
$lines.Add('!macroend')
$lines.Add('!macro RemoveEmptyPayloadDirectories')
foreach ($directory in ($ownedDirectories | Sort-Object { $_.Length } -Descending)) {
    $lines.Add('  RMDir "$INSTDIR\' + $directory + '"')
}
$lines.Add('!macroend')
[IO.File]::WriteAllLines($OutputFile, $lines, [Text.UTF8Encoding]::new($false))
Write-Host "Uninstaller owns $($ownedFiles.Count) exact files; unrelated files and shared USBip are preserved."
