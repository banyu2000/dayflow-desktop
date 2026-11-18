param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'build'))
$ErrorActionPreference = 'Stop'
$calendarFramework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (!(Test-Path -LiteralPath (Join-Path $calendarFramework 'csc.exe'))) {
    $calendarFramework = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
}
$calendarWpf = Join-Path $calendarFramework 'WPF'
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$calendarReferences = @('System.dll','System.Core.dll','System.Web.Extensions.dll','System.Drawing.dll','System.Windows.Forms.dll') | ForEach-Object { '/reference:' + (Join-Path $calendarFramework $_) }
$calendarReferences += @('PresentationFramework.dll','PresentationCore.dll','WindowsBase.dll','System.Xaml.dll') | ForEach-Object {
    $calendarReferencePath = Join-Path $calendarWpf $_
    if (!(Test-Path -LiteralPath $calendarReferencePath)) { $calendarReferencePath = Join-Path $calendarFramework $_ }
    '/reference:' + $calendarReferencePath
}
$calendarSources = @('Core.cs','App.cs','Dialogs.cs','GoalsUI.cs','Glass.cs','Floating.cs','SelfTests.cs','UiChecks.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
$calendarArgs = @('/nologo','/target:winexe','/platform:anycpu','/optimize+','/codepage:65001',('/out:' + (Join-Path $OutputDirectory '日常日历.exe')))
$calendarArgs += '/resource:' + (Join-Path $PSScriptRoot 'Theme.xaml') + ',DayFlow.Theme.xaml'
$calendarIcon = Join-Path $PSScriptRoot 'DayFlow.ico'
if (Test-Path -LiteralPath $calendarIcon) { $calendarArgs += '/win32icon:' + $calendarIcon; Copy-Item -LiteralPath $calendarIcon -Destination $OutputDirectory -Force }
$calendarTemp = Join-Path $OutputDirectory '.build-temp'
New-Item -ItemType Directory -Force -Path $calendarTemp | Out-Null
$calendarPreviousTemp = $env:TEMP
$calendarPreviousTmp = $env:TMP
try {
    $env:TEMP = (Resolve-Path -LiteralPath $calendarTemp).Path
    $env:TMP = $env:TEMP
    & (Join-Path $calendarFramework 'csc.exe') @calendarArgs @calendarReferences @calendarSources
    if ($LASTEXITCODE -ne 0) { throw '编译失败' }
} finally {
    $env:TEMP = $calendarPreviousTemp
    $env:TMP = $calendarPreviousTmp
}
Remove-Item -LiteralPath $calendarTemp -ErrorAction SilentlyContinue
Write-Output (Join-Path $OutputDirectory '日常日历.exe')
