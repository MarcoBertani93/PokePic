# Runs bin/pokepic.exe and returns exit code, stdout and stderr (dot-sourced by validate.ps1, and by build/package-agent.ps1 in the PokePic repository).
function Invoke-PokePic {
    param([string]$WorkingDirectory, [string[]]$Arguments)
    $exe = Join-Path (Split-Path -Parent $PSScriptRoot) 'bin\pokepic.exe'
    if (-not (Test-Path $exe)) { throw "The PokePic CLI is missing: $exe" }
    $info = New-Object System.Diagnostics.ProcessStartInfo
    $info.FileName = $exe
    $info.WorkingDirectory = $WorkingDirectory
    $info.UseShellExecute = $false
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.StandardOutputEncoding = [Text.Encoding]::UTF8
    $info.StandardErrorEncoding = [Text.Encoding]::UTF8
    $info.Arguments = ($Arguments | ForEach-Object { '"' + $_ + '"' }) -join ' '
    $process = [System.Diagnostics.Process]::Start($info)
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $stdout = $process.StandardOutput.ReadToEnd()
    $process.WaitForExit()
    [pscustomobject]@{ ExitCode = $process.ExitCode; StdOut = $stdout.TrimEnd(); StdErr = $stderrTask.Result.TrimEnd() }
}
