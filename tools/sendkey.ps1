<#
.SYNOPSIS
  Test helper: sends real input to the running Valheim window (scan-code keys, clicks, relative mouse moves).
  Unity's Input System ignores the unicode letters and clicks that screen-automation tools inject.
.EXAMPLE
  ./tools/sendkey.ps1 G                 # open the radial menu
  ./tools/sendkey.ps1 E -Shift          # Shift+E
  ./tools/sendkey.ps1 'MOVE:-300,0'     # turn the camera left
  ./tools/sendkey.ps1 W -HoldMs 900     # walk forward
  ./tools/sendkey.ps1 LCLICK
#>
param(
    [Parameter(Mandatory)][string[]]$Keys,   # e.g. G, T, E, F5, ESC; prefix "hold:" not supported
    [int]$HoldMs = 60,
    [int]$GapMs = 150,
    [switch]$Shift
)
# Sends real key presses (scan codes) to the foreground window, which Unity's Input System reads (unlike unicode chars).
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class ScanKeys {
    [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Explicit)] struct UNION { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public UNION u; }
    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint n, INPUT[] inputs, int size);
    const uint KEYUP = 0x2, SCANCODE = 0x8;
    public static void Move(int dx, int dy) {
        var move = new INPUT { type = 0 }; move.u.mi = new MOUSEINPUT { dx = dx, dy = dy, dwFlags = 0x0001u };
        SendInput(1, new[] { move }, Marshal.SizeOf(typeof(INPUT)));
    }
    public static void Click(bool right) {
        var down = new INPUT { type = 0 }; down.u.mi = new MOUSEINPUT { dwFlags = right ? 0x0008u : 0x0002u };
        var up = new INPUT { type = 0 }; up.u.mi = new MOUSEINPUT { dwFlags = right ? 0x0010u : 0x0004u };
        SendInput(1, new[] { down }, Marshal.SizeOf(typeof(INPUT)));
        System.Threading.Thread.Sleep(60);
        SendInput(1, new[] { up }, Marshal.SizeOf(typeof(INPUT)));
    }
    public static void Send(ushort scan, bool up) {
        var input = new INPUT { type = 1 };
        input.u.ki = new KEYBDINPUT { wScan = scan, dwFlags = SCANCODE | (up ? KEYUP : 0) };
        SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT)));
    }
}
"@
$scan = @{ ESC=0x01; '1'=0x02; '2'=0x03; '3'=0x04; '4'=0x05; Q=0x10; W=0x11; E=0x12; R=0x13; T=0x14; Y=0x15; U=0x16; I=0x17; O=0x18; P=0x19;
           A=0x1E; S=0x1F; D=0x20; F=0x21; G=0x22; H=0x23; J=0x24; K=0x25; L=0x26; Z=0x2C; X=0x2D; C=0x2E; V=0x2F; B=0x30; N=0x31; M=0x32;
           TAB=0x0F; ENTER=0x1C; SPACE=0x39; LSHIFT=0x2A; F5=0x3F }
(New-Object -ComObject WScript.Shell).AppActivate((Get-Process valheim).Id) | Out-Null
Start-Sleep -Milliseconds 400
if ($Shift) { [ScanKeys]::Send($scan.LSHIFT, $false) }
foreach ($k in $Keys) {
    if ($k -eq 'LCLICK' -or $k -eq 'RCLICK') { [ScanKeys]::Click($k -eq 'RCLICK'); Start-Sleep -Milliseconds $GapMs; continue }
    if ($k -match '^MOVE:(-?\d+),(-?\d+)$') {
        # Relative mouse movement in small steps, the way a real mouse reports it (camera turn).
        $dx = [int]$Matches[1]; $dy = [int]$Matches[2]; $steps = 10
        for ($i = 0; $i -lt $steps; $i++) { [ScanKeys]::Move([int]($dx / $steps), [int]($dy / $steps)); Start-Sleep -Milliseconds 15 }
        Start-Sleep -Milliseconds $GapMs; continue
    }
    $code = $scan[$k.ToUpper()]
    if (-not $code) { throw "unknown key $k" }
    [ScanKeys]::Send([uint16]$code, $false); Start-Sleep -Milliseconds $HoldMs
    [ScanKeys]::Send([uint16]$code, $true);  Start-Sleep -Milliseconds $GapMs
}
if ($Shift) { [ScanKeys]::Send($scan.LSHIFT, $true) }
"sent: $($Keys -join ' ')"

