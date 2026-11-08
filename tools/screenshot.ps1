<#
.SYNOPSIS
  KolayKur ana penceresinin ekran görüntüsünü alır (salt okur; hiçbir uygulama kurulmaz, güncellenmez, kaldırılmaz).

.DESCRIPTION
  - Geçici bir veri klasörü kullanır (KOLAYKUR_DATA_DIR); sahibinin gerçek %LocalAppData%\KolayKur klasörüne dokunmaz.
  - Yalnızca sol menü (RadioButton) düğmelerine, -Search verilirse arama kutusuna ile "Ara" düğmesine ve -OpenPane verilirse
    başlıktaki "İşlemler" geçiş düğmesine basar (yalnızca paneli açar).
    Kur, Güncelle, Kaldır, Listeden kur, Onar ve başka hiçbir düğmeye basılmaz.
  - Başlattığı süreci her durumda kapatır ve geçici klasörü siler.

.EXAMPLE
  .\tools\screenshot.ps1 -Theme Dark -Page Settings -Out out\screenshots\ayarlar-koyu.png
  .\tools\screenshot.ps1 -Search vlc -Out out\screenshots\arama.png
#>
[CmdletBinding()]
param(
    [ValidateSet('System', 'Light', 'Dark')] [string]$Theme = 'Light',
    [ValidateSet('tr', 'en')] [string]$Lang = 'tr',
    [ValidateSet('Search', 'Updates', 'Installed', 'Settings')] [string]$Page = 'Search',
    [int]$Width = 1100,
    [int]$Height = 720,
    [string]$Search = '',
    [switch]$OpenPane,
    [string]$Out = 'out\screenshots\screenshot.png',
    [string]$Exe = ''
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (-not $Exe) {
    $Exe = Join-Path $repo 'src\KolayKur.App\bin\Debug\net10.0-windows10.0.26100.0\win-x64\KolayKur.exe'
}
if (-not (Test-Path $Exe)) { throw "Program bulunamadi: $Exe (once dotnet build)" }
$outPath = if ([System.IO.Path]::IsPathRooted($Out)) { $Out } else { Join-Path (Get-Location) $Out }

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class KkNative {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hgt, uint flags);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
}
'@
[void][KkNative]::SetProcessDPIAware()

# Menü düğmelerinin ve Ara düğmesinin görünen adları (kısa menü metinleri).
$names = @{
    tr = @{ Search = 'Ara'; Updates = 'Güncelle'; Installed = 'Yüklü'; Settings = 'Ayarlar'; SearchAction = 'Ara'; Operations = 'İşlemler' }
    en = @{ Search = 'Search'; Updates = 'Updates'; Installed = 'Installed'; Settings = 'Settings'; SearchAction = 'Search'; Operations = 'Operations' }
}[$Lang]

function Find-Elements($root, $controlType) {
    $cond = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty), $controlType
    $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
}

function Find-Named($root, $controlType, $name) {
    foreach ($el in (Find-Elements $root $controlType)) {
        if ($el.Current.Name -eq $name) { return $el }
    }
    return $null
}

function Wait-Until([scriptblock]$test, [int]$seconds, [string]$what) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        $result = & $test
        if ($result) { return $result }
        Start-Sleep -Milliseconds 250
    }
    throw "Zaman asimi: $what"
}

$temp = Join-Path ([System.IO.Path]::GetTempPath()) ("kolaykur-shot-" + [guid]::NewGuid().ToString('N'))
$oldEnv = $env:KOLAYKUR_DATA_DIR
$proc = $null
try {
    New-Item -ItemType Directory -Path $temp | Out-Null
    $settings = [ordered]@{ Language = $Lang; LicenseNoticeShown = $true; Theme = $Theme } | ConvertTo-Json
    [System.IO.File]::WriteAllText((Join-Path $temp 'settings.json'), $settings, (New-Object System.Text.UTF8Encoding($false)))

    $env:KOLAYKUR_DATA_DIR = $temp
    $proc = Start-Process -FilePath $Exe -PassThru
    $env:KOLAYKUR_DATA_DIR = $oldEnv

    $hwnd = Wait-Until { $proc.Refresh(); if ($proc.HasExited) { throw 'Program beklenmedik sekilde kapandi.' }; $proc.MainWindowHandle -ne [IntPtr]::Zero } 30 'ana pencere'
    $hwnd = $proc.MainWindowHandle
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)

    # Menü etkinleşene kadar (winget denetimi bitene kadar) bekle.
    $menuButton = Wait-Until {
        $b = Find-Named $root ([System.Windows.Automation.ControlType]::RadioButton) $names[$Page]
        if ($b -and $b.Current.IsEnabled) { $b } else { $null }
    } 30 'menu etkinlesmesi (winget denetimi)'

    [void][KkNative]::ShowWindow($hwnd, 9)
    [void][KkNative]::SetWindowPos($hwnd, [IntPtr]::Zero, 40, 40, $Width, $Height, 0x0004)
    Start-Sleep -Milliseconds 600

    # Yalnızca menü düğmesi: RadioButton seçilir.
    $menuButton.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 600

    # Yalnızca başlıktaki "İşlemler" geçiş düğmesi: panel kapalıysa açılır (iş eklemez, başka düğmeye basmaz).
    if ($OpenPane) {
        $toggle = Find-Named $root ([System.Windows.Automation.ControlType]::Button) $names.Operations
        if (-not $toggle) { throw 'Islemler dugmesi bulunamadi.' }
        $tp = $toggle.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
        if ($tp.Current.ToggleState -ne [System.Windows.Automation.ToggleState]::On) { $tp.Toggle() }
        Start-Sleep -Milliseconds 600
    }

    if ($Search) {
        if ($Page -ne 'Search') { throw '-Search yalnizca -Page Search ile kullanilir.' }
        $box = (Find-Elements $root ([System.Windows.Automation.ControlType]::Edit)) | Select-Object -First 1
        if (-not $box) { throw 'Arama kutusu bulunamadi.' }
        $box.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($Search)
        $go = Find-Named $root ([System.Windows.Automation.ControlType]::Button) $names.SearchAction
        if (-not $go) { throw 'Ara dugmesi bulunamadi.' }
        $go.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        Start-Sleep -Seconds 8
    }

    [void][KkNative]::SetForegroundWindow($hwnd)
    Start-Sleep -Milliseconds 500

    $rect = New-Object KkNative+RECT
    [void][KkNative]::GetWindowRect($hwnd, [ref]$rect)
    $w = $rect.Right - $rect.Left
    $h = $rect.Bottom - $rect.Top
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    try {
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $hdc = $g.GetHdc()
        # 2 = PW_RENDERFULLCONTENT (DWM ile çizilen pencereler için)
        [void][KkNative]::PrintWindow($hwnd, $hdc, 2)
        $g.ReleaseHdc($hdc)
        $g.Dispose()
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $outPath) | Out-Null
        $bmp.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally { $bmp.Dispose() }
    Write-Host "Kaydedildi: $outPath (${w}x${h})"
}
finally {
    $env:KOLAYKUR_DATA_DIR = $oldEnv
    if ($proc) {
        try { if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force; [void]$proc.WaitForExit(10000) } } catch { }
    }
    if (Test-Path $temp) { Remove-Item -Recurse -Force $temp -ErrorAction SilentlyContinue }
}
