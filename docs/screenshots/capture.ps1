param(
    [string]$Exe = "$PSScriptRoot\..\..\publish\v0.1.0\BG3LocTool.App.exe",
    [string]$OutDir = "$PSScriptRoot"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

Add-Type @"
using System;
using System.Runtime.InteropServices;
public class W {
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
}
"@

$exePath = Resolve-Path $Exe
Write-Host "Launching $exePath"
$p = Start-Process $exePath -PassThru
Start-Sleep -Seconds 6

# Refresh to find main window
$p.Refresh()
$timeout = 15
while ($p.MainWindowHandle -eq 0 -and $timeout -gt 0) {
    Start-Sleep -Seconds 1
    $p.Refresh()
    $timeout--
}

if ($p.MainWindowHandle -eq 0) {
    Write-Host "ERROR: no window handle"
    $p.Kill()
    exit 1
}

$h = $p.MainWindowHandle
[W]::ShowWindow($h, 9) | Out-Null  # SW_RESTORE
[W]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Seconds 2

$rect = New-Object W+RECT
[W]::GetWindowRect($h, [ref]$rect) | Out-Null
$w = $rect.R - $rect.L
$h2 = $rect.B - $rect.T
Write-Host "Window: ${w}x${h2} at ($($rect.L),$($rect.T))"

$bmp = New-Object System.Drawing.Bitmap $w, $h2
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
$ok = [W]::PrintWindow($h, $hdc, 2)  # PW_RENDERFULLCONTENT
$g.ReleaseHdc($hdc)

if (-not $ok -or ($bmp.GetPixel(10,10).R -eq 0 -and $bmp.GetPixel(10,10).G -eq 0 -and $bmp.GetPixel(10,10).B -eq 0)) {
    Write-Host "PrintWindow failed or black, fallback to CopyFromScreen"
    $g.Dispose(); $bmp.Dispose()
    $bmp = New-Object System.Drawing.Bitmap $w, $h2
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($rect.L, $rect.T, 0, 0, (New-Object System.Drawing.Size($w,$h2)))
}

$out = Join-Path $OutDir "main.png"
$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
Write-Host "Saved: $out"
$g.Dispose(); $bmp.Dispose()

Start-Sleep -Seconds 1
$p.Kill()
$p.WaitForExit(5000)
Write-Host "Done."
