param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'build'))
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'src/DayFlow/build.ps1') -OutputDirectory $OutputDirectory
