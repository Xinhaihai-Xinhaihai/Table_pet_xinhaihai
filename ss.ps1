param(
    [int]$x, [int]$y, [int]$w, [int]$h,
    [string]$out = "D:\1\XinHaiHai\shot.png"
)
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class Dpi { [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr v); }
'@
try { [Dpi]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null } catch {}
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
if ($w -le 0) { # 全屏
    $b = [System.Windows.Forms.SystemInformation]::VirtualScreen
    $x = $b.X; $y = $b.Y; $w = $b.Width; $h = $b.Height
}
$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($x, $y, 0, 0, $bmp.Size)
$g.Dispose()
$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Output "saved $out ($x,$y ${w}x${h})"
