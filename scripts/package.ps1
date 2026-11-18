param([string]$Version = '1.3.0')
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Use a semantic version such as 1.3.0.' }
$dayflowRoot = Split-Path $PSScriptRoot -Parent
& (Join-Path $dayflowRoot 'build.ps1')
$dayflowPackageRoot = Join-Path $dayflowRoot ('dist/staging-' + [Guid]::NewGuid().ToString('N'))
$dayflowPackage = Join-Path $dayflowPackageRoot 'DayFlow'
New-Item -ItemType Directory -Path $dayflowPackage -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $dayflowRoot 'build/日常日历.exe') -Destination $dayflowPackage
Copy-Item -LiteralPath (Join-Path $dayflowRoot 'build/DayFlow.ico') -Destination $dayflowPackage
Copy-Item -LiteralPath (Join-Path $dayflowRoot 'LICENSE') -Destination $dayflowPackage
Copy-Item -LiteralPath (Join-Path $dayflowRoot 'docs/USER_GUIDE.zh-CN.txt') -Destination (Join-Path $dayflowPackage '使用说明.txt')
$dayflowArchive = Join-Path $dayflowRoot ('dist/DayFlow-v' + $Version + '-windows.zip')
Compress-Archive -LiteralPath $dayflowPackage -DestinationPath $dayflowArchive -Force
Write-Output $dayflowArchive
