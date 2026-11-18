param([switch]$Ui)
$ErrorActionPreference = 'Stop'
$dayflowRoot = Split-Path $PSScriptRoot -Parent
$dayflowExecutable = Join-Path $dayflowRoot 'build/日常日历.exe'
if (!(Test-Path -LiteralPath $dayflowExecutable)) { & (Join-Path $dayflowRoot 'build.ps1') }
$dayflowRun = Join-Path $dayflowRoot ('test-output/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $dayflowRun -Force | Out-Null
if ($Ui) {
    $dayflowArguments = @('--ui-check', '--data-dir', ('"' + (Join-Path $dayflowRun 'data') + '"'))
    $dayflowReport = Join-Path $dayflowRun 'ui-result.txt'
} else {
    $dayflowArguments = @('--self-test', '--test-dir', ('"' + $dayflowRun + '"'))
    $dayflowReport = Join-Path $dayflowRun 'test-result.txt'
}
$dayflowProcess = Start-Process -FilePath $dayflowExecutable -ArgumentList $dayflowArguments -Wait -PassThru
if (Test-Path -LiteralPath $dayflowReport) { Get-Content -LiteralPath $dayflowReport -Encoding UTF8 }
if ($dayflowProcess.ExitCode -ne 0 -or !(Test-Path -LiteralPath $dayflowReport)) { throw 'DayFlow checks failed; inspect test-output.' }
Write-Output ('Test output: ' + $dayflowRun)
