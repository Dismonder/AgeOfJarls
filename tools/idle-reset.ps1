# Keeps the screensaver away during autonomous tests: every 50 s a zero-length mouse move (the cursor does not move,
# nothing is clicked) resets Windows' user-idle timer. SetThreadExecutionState alone does not stop screensavers.
param([double]$Hours = 12)
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class IdleReset {
  [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
  [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public MOUSEINPUT mi; }
  [DllImport("user32.dll")] static extern uint SendInput(uint n, INPUT[] inputs, int size);
  public static void Nudge() {
    var move = new INPUT { type = 0, mi = new MOUSEINPUT { dx = 0, dy = 0, dwFlags = 0x0001 } };
    SendInput(1, new[] { move }, Marshal.SizeOf(typeof(INPUT)));
  }
}
"@
$timer = [Diagnostics.Stopwatch]::StartNew()
while ($timer.Elapsed.TotalHours -lt $Hours) {
    [IdleReset]::Nudge()
    Start-Sleep -Seconds 50
}
