# Builds the package for an AI agent that writes templates, in artifacts/PokePic-AgentPackage (and a zip of it).
# Needs the .NET 10 SDK; the package itself needs nothing. The package is assembled from:
#   agent-package/          AGENTS.md (instructions for the agent), README.md, validate.ps1, tools/, examples/
#   resources/              CREDITS.md and the data packs (data/)
#   docs/                   the guide and the generated references, English only
# and generated here: bin/pokepic.exe (the CLI, self-contained), schema/ (build/generate-reference.cs, which also
# regenerates docs/template-reference*.md and docs/data-keys*.md), examples/*/expected/ (the CLI output and the card
# of every example template with every data pack).
# The build trusts the renderer (the tests check it); it fails only if an example does not behave as intended:
# a template that must work fails with a data pack or is rejected by the schema, an error example stops failing.
# Usage: pwsh build/package-agent.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$source = Join-Path $root 'agent-package'
$artifacts = Join-Path $root 'artifacts'
$package = Join-Path $artifacts 'PokePic-AgentPackage'

# The error examples and the command that must fail; every other example template must check and render.
$mustFail = @{
    '05-error-and-fix/template.json'              = 'check'
    '05-error-and-fix/template.round2.json'       = 'check'
    '05-error-and-fix/template.render-error.json' = 'render'
}

Remove-Item -Recurse -Force $package -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $package | Out-Null

# 1. The CLI.
$cli = Join-Path ([IO.Path]::GetTempPath()) 'pokepic-agent-cli'
Remove-Item -Recurse -Force $cli -ErrorAction SilentlyContinue
dotnet publish (Join-Path $root 'src/PokePic.Cli') -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o $cli
if ($LASTEXITCODE -ne 0) { throw 'CLI publish failed.' }
New-Item -ItemType Directory -Force (Join-Path $package 'bin') | Out-Null
Copy-Item (Join-Path $cli 'pokepic.exe') (Join-Path $package 'bin/pokepic.exe')
Remove-Item -Recurse -Force $cli

# 2. The references (docs/) and the JSON schema, checked against the code.
dotnet run (Join-Path $PSScriptRoot 'generate-reference.cs') -- (Join-Path $root 'docs') (Join-Path $package 'schema')
if ($LASTEXITCODE -ne 0) { throw 'Reference generation failed: the reference no longer matches the PokePic code.' }

# 3. Sources and resources.
foreach ($item in 'AGENTS.md', 'README.md', 'validate.ps1', 'tools', 'examples') {
    Copy-Item -Recurse (Join-Path $source $item) $package
}
Copy-Item (Join-Path $root 'resources/CREDITS.md') $package
Copy-Item -Recurse (Join-Path $root 'resources/data') $package
New-Item -ItemType Directory -Force (Join-Path $package 'docs') | Out-Null
foreach ($doc in 'guide.md', 'template-reference.md', 'data-keys.md') {
    # Only the English documents: the line that links the Italian translation goes.
    $text = [IO.File]::ReadAllText((Join-Path $root "docs/$doc"))
    $text = [regex]::Replace($text, '(?m)^\*\*English\*\* \| \[Italiano\]\([^)]*\)\r?\n\r?\n', '')
    [IO.File]::WriteAllText((Join-Path $package "docs/$doc"), $text)
}

# 4. The expected output of the examples, with every data pack.
. (Join-Path $package 'tools/run-cli.ps1')
$schemaPath = Join-Path $package 'schema/template.schema.json'
$canTestSchema = $null -ne (Get-Command Test-Json -ErrorAction SilentlyContinue)
if (-not $canTestSchema) { Write-Output 'Note: Test-Json is not available (PowerShell 7+ needed): schema checks skipped.' }
$packs = @(Get-ChildItem (Join-Path $package 'data') -Filter *.json | ForEach-Object { $_.Name })
$failures = New-Object System.Collections.Generic.List[string]

# Paths in messages depend on where the package is: they become relative to the example folder.
function Normalize([string]$text, [string]$folder) {
    $text.Replace($folder + '\', '').Replace($folder.Replace('\', '/') + '/', '').Replace("`r`n", "`n")
}
function Transcript($command, $result, $folder) {
    "> pokepic $command`nexit code: $($result.ExitCode)`nstdout:`n$(Normalize $result.StdOut $folder)`nstderr:`n$(Normalize $result.StdErr $folder)`n"
}

foreach ($example in Get-ChildItem (Join-Path $package 'examples') -Directory) {
    $folder = $example.FullName
    $expected = Join-Path $folder 'expected'
    New-Item -ItemType Directory -Force $expected | Out-Null
    foreach ($file in Get-ChildItem $folder -Filter 'template*.json') {
        $label = "$($example.Name)/$($file.Name)"
        $mustFailAt = $mustFail[$label]
        $summary = @()
        foreach ($pack in $packs) {
            $packName = [IO.Path]::GetFileNameWithoutExtension($pack)
            $data = "../../data/$pack"
            $out = "expected/$($file.BaseName).$packName.png"
            $check = Invoke-PokePic $folder @('check', '--template', $file.Name, '--data', $data)
            $text = Transcript "check --template $($file.Name) --data $data" $check $folder
            $rendered = $false
            if ($check.ExitCode -eq 0) {
                $render = Invoke-PokePic $folder @('render', '--template', $file.Name, '--data', $data, '--out', $out)
                $text += "`n" + (Transcript "render --template $($file.Name) --data $data --out $out" $render $folder)
                $rendered = $render.ExitCode -eq 0
            }
            [IO.File]::WriteAllText((Join-Path $expected "$($file.BaseName).$packName.txt"), $text)
            $summary += "$packName " + $(if ($rendered) { 'ok' } elseif ($check.ExitCode -ne 0) { 'check failed' } else { 'render failed' })

            if (-not $mustFailAt -and -not $rendered) { $failures.Add("${label} with ${pack}:`n$text") }
            if ($mustFailAt -eq 'check' -and $check.ExitCode -eq 0) { $failures.Add("${label} with ${pack}: check should fail, it succeeds") }
        }
        if ($mustFailAt -eq 'render' -and -not ($summary -match 'render failed')) { $failures.Add("${label}: render should fail with at least one data pack") }
        if (-not $mustFailAt -and $canTestSchema) {
            $valid = Test-Json -Json (Get-Content -Raw $file.FullName) -SchemaFile $schemaPath -ErrorAction SilentlyContinue
            if (-not $valid) { $failures.Add("${label}: valid for PokePic but rejected by schema/template.schema.json") }
        }
        Write-Output ("{0,-48} {1}" -f $label, ($summary -join ', '))
    }
}
if ($failures.Count -gt 0) {
    Write-Output ''
    $failures | ForEach-Object { Write-Output "FAIL $_" }
    exit 1
}

# 5. The zip, next to the folder.
$zip = Join-Path $artifacts 'PokePic-AgentPackage.zip'
Remove-Item -Force $zip -ErrorAction SilentlyContinue
Compress-Archive -Path $package -DestinationPath $zip
Write-Output "Package: $package"
Write-Output "Zip:     $zip"
