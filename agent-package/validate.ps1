# Validates a template the way PokePic will use it: "check" and "render" with every data pack in data/
# (and with -Data, if given). A template is valid only when every run ends with exit code 0.
# Usage: powershell -ExecutionPolicy Bypass -File validate.ps1 -Template <folder>\template.json [-Data <pack.json>] [-Language de] [-Out <folder>]
# -Language replaces the data packs' language, as for a PKHeX user with another language: "de" or "fr" have longer texts.
# The rendered cards go to -Out (default: <template folder>\out), one PNG per data pack.
param(
    [Parameter(Mandatory = $true)][string]$Template,
    [string]$Data,
    [string]$Language,
    [string]$Out
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'tools\run-cli.ps1')

$templatePath = (Resolve-Path $Template).Path
$folder = Split-Path -Parent $templatePath
if (-not $Out) { $Out = Join-Path $folder 'out' }
New-Item -ItemType Directory -Force $Out | Out-Null
$Out = (Resolve-Path $Out).Path

$packs = @(Get-ChildItem (Join-Path $PSScriptRoot 'data') -Filter *.json | ForEach-Object { $_.FullName })
if ($Data) { $packs = @((Resolve-Path $Data).Path) + $packs }

$failed = 0
foreach ($pack in $packs) {
    $name = [IO.Path]::GetFileNameWithoutExtension($pack)
    Write-Output "== $name ($pack)"
    $check = Invoke-PokePic $folder @('check', '--template', $templatePath, '--data', $pack)
    if ($check.StdOut) { Write-Output $check.StdOut }
    if ($check.StdErr) { Write-Output $check.StdErr }
    if ($check.ExitCode -ne 0) { $failed++; Write-Output "check: FAILED (exit code $($check.ExitCode))"; continue }
    $png = Join-Path $Out "$name.png"
    $arguments = @('render', '--template', $templatePath, '--data', $pack, '--out', $png)
    if ($Language) { $arguments += @('--language', $Language) }
    $render = Invoke-PokePic $folder $arguments
    if ($render.StdOut) { Write-Output $render.StdOut }
    if ($render.StdErr) { Write-Output $render.StdErr }
    if ($render.ExitCode -ne 0) { $failed++; Write-Output "render: FAILED (exit code $($render.ExitCode))" }
}

if ($failed -gt 0) {
    Write-Output "INVALID: $failed of $($packs.Count) data packs failed."
    exit 1
}
Write-Output "VALID: check and render succeeded with all $($packs.Count) data packs. Cards in $Out"
exit 0
