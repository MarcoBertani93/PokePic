# Builds the PKHeX plugin package: a "plugins" folder to copy next to PKHeX.exe.
#
#   plugins/
#     PokePic.Plugin.dll          the plugin, a single DLL (its .NET and native dependencies are embedded)
#     PokePic/
#       PokePic.Editor.exe        the editor, as a single file: PKHeX loads every .dll under plugins, not .exe files
#       templates/<template>/     one folder per template, with its template.json
#
# Usage: pwsh build/package-plugin.ps1 [-Configuration Release] [-Output artifacts]
param(
    [string]$Configuration = 'Release',
    [string]$Output = 'artifacts'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root $Output
$work = Join-Path $out 'work'
$package = Join-Path $out 'PokePic-PKHeX'
$plugins = Join-Path $package 'plugins'
$pokepic = Join-Path $plugins 'PokePic'

Remove-Item -Recurse -Force $work, $package -ErrorAction SilentlyContinue

dotnet build (Join-Path $root 'src/PokePic.Plugin') -c $Configuration -o (Join-Path $work 'plugin')
if ($LASTEXITCODE -ne 0) { throw 'Plugin build failed.' }

dotnet publish (Join-Path $root 'src/PokePic.Editor') -c $Configuration -r win-x64 --self-contained false `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none `
    -o (Join-Path $work 'editor')
if ($LASTEXITCODE -ne 0) { throw 'Editor publish failed.' }

New-Item -ItemType Directory -Force (Join-Path $pokepic 'templates') | Out-Null
Copy-Item (Join-Path $work 'plugin/PokePic.Plugin.dll') $plugins
Copy-Item (Join-Path $work 'editor/PokePic.Editor.exe') $pokepic
Get-ChildItem (Join-Path $root 'resources/templates') -Directory | Copy-Item -Recurse -Destination (Join-Path $pokepic 'templates')

Copy-Item (Join-Path $root 'README.md'), (Join-Path $root 'README.it.md'), (Join-Path $root 'resources/CREDITS.md') $package
$zip = Join-Path $out 'PokePic-PKHeX.zip'
Remove-Item -Force $zip -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $package '*') -DestinationPath $zip
Remove-Item -Recurse -Force $work

Write-Host "Package: $package"
Write-Host "Zip:     $zip"
