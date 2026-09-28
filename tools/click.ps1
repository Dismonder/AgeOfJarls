# Focus Valheim, then left-click at (x, y) given in the 1456x819 screenshot frame, with real mouse input.
param([Parameter(Mandatory)][double]$X, [Parameter(Mandatory)][double]$Y)
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class ClickWin {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extra);
}
"@
& "$PSScriptRoot\focus-game.ps1" | Out-Null
[ClickWin]::SetProcessDPIAware() | Out-Null
$w = [ClickWin]::GetSystemMetrics(0); $h = [ClickWin]::GetSystemMetrics(1)
[ClickWin]::SetCursorPos([int]($X * $w / 1456), [int]($Y * $h / 819)) | Out-Null
Start-Sleep -Milliseconds 120
[ClickWin]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 60; [ClickWin]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
"clicked $X,$Y"
