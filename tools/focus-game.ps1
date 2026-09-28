# Test helper: restores the Valheim window and gives it the keyboard focus (Alt tap + SetForegroundWindow), prints 'focused'.
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class FocusWin {
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern int GetWindowThreadProcessId(IntPtr h, out int pid);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
  [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
}
"@
$p = Get-Process valheim -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) { "no game"; exit 1 }
$h = $p.MainWindowHandle
for ($i = 0; $i -lt 5; $i++) {
  if ([FocusWin]::IsIconic($h)) { [FocusWin]::ShowWindow($h, 9) | Out-Null }
  # An Alt tap lets this process take the foreground (Windows foreground lock).
  [FocusWin]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero); [FocusWin]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)
  [FocusWin]::SetForegroundWindow($h) | Out-Null
  Start-Sleep -Milliseconds 300
  $fgPid = 0; [FocusWin]::GetWindowThreadProcessId([FocusWin]::GetForegroundWindow(), [ref]$fgPid) | Out-Null
  if ($fgPid -eq $p.Id) { "focused"; exit 0 }
}
"foreground is " + (Get-Process -Id $fgPid).ProcessName
exit 1
