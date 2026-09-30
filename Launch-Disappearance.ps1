[CmdletBinding()]
param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$SteamCommand
)

$ErrorActionPreference = 'Stop'
$gameRoot = $PSScriptRoot

if (-not (Test-Path -LiteralPath (Join-Path $gameRoot 'Disappearance.exe') -PathType Leaf)) {
    throw "Game executable not found: $gameRoot"
}

$letter = @('W', 'X', 'Y', 'Z') |
    Where-Object { -not (Get-PSDrive -Name $_ -PSProvider FileSystem -ErrorAction SilentlyContinue) } |
    Select-Object -First 1
if (-not $letter) { throw 'No free drive letter among W:, X:, Y:, and Z:.' }

$drive = "${letter}:"
$aliasRoot = "$drive\"
& subst $drive $gameRoot
if ($LASTEXITCODE -ne 0) { throw "Could not assign $drive to the game directory." }

try {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class DisappearanceWindowFocus
{
    private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, System.Text.StringBuilder className, int maxCount);
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out RECT bounds);
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr window);
    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr window);
    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint sourceThread, uint targetThread, bool attach);
    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    public static uint ForegroundProcessId()
    {
        uint processId;
        GetWindowThreadProcessId(GetForegroundWindow(), out processId);
        return processId;
    }

    public static bool IsForegroundWindow(IntPtr window)
    {
        return window != IntPtr.Zero && GetForegroundWindow() == window;
    }

    public static IntPtr ForegroundWindow()
    {
        return GetForegroundWindow();
    }

    public static string WindowClass(IntPtr window)
    {
        if (window == IntPtr.Zero) return "";
        var name = new System.Text.StringBuilder(256);
        return GetClassName(window, name, name.Capacity) > 0 ? name.ToString() : "";
    }

    public static IntPtr FindVisibleWindow(uint gameProcessId)
    {
        IntPtr found = IntPtr.Zero;
        long largestArea = 0;
        bool foundUnityWindow = false;
        EnumWindows(delegate(IntPtr window, IntPtr parameter)
        {
            uint processId;
            GetWindowThreadProcessId(window, out processId);
            if (processId == gameProcessId && IsWindowVisible(window))
            {
                RECT bounds;
                if (GetWindowRect(window, out bounds))
                {
                    long width = Math.Max(0, bounds.Right - bounds.Left);
                    long height = Math.Max(0, bounds.Bottom - bounds.Top);
                    long area = width * height;
                    bool isUnityWindow = WindowClass(window) == "UnityWndClass";
                    if (area > 0 && (isUnityWindow && !foundUnityWindow ||
                        isUnityWindow == foundUnityWindow && area > largestArea))
                    {
                        largestArea = area;
                        found = window;
                        foundUnityWindow = isUnityWindow;
                    }
                }
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public static bool Activate(IntPtr window)
    {
        if (window == IntPtr.Zero || !IsWindowVisible(window)) return false;
        if (GetForegroundWindow() != window)
        {
            BringWindowToTop(window);
            SetForegroundWindow(window);
        }

        IntPtr foreground = GetForegroundWindow();
        uint ignored;
        uint foregroundThread = GetWindowThreadProcessId(foreground, out ignored);
        uint gameThread = GetWindowThreadProcessId(window, out ignored);
        uint currentThread = GetCurrentThreadId();
        if (gameThread == 0 || gameThread == currentThread) return false;
        bool attachedForeground = foregroundThread != 0 && foregroundThread != currentThread &&
            foregroundThread != gameThread && AttachThreadInput(currentThread, foregroundThread, true);
        if (!AttachThreadInput(currentThread, gameThread, true))
        {
            if (attachedForeground) AttachThreadInput(currentThread, foregroundThread, false);
            return false;
        }
        try
        {
            BringWindowToTop(window);
            SetForegroundWindow(window);
            if (GetForegroundWindow() == window)
                SetFocus(window);
        }
        finally
        {
            AttachThreadInput(currentThread, gameThread, false);
            if (attachedForeground) AttachThreadInput(currentThread, foregroundThread, false);
        }
        return GetForegroundWindow() == window;
    }
}
'@ -ErrorAction Stop
    $focusAvailable = $true
    $startingForegroundProcess = [DisappearanceWindowFocus]::ForegroundProcessId()
}
catch {
    $focusAvailable = $false
    Write-Warning "Window activation is unavailable; launching normally: $_"
}

try {
    Write-Host "Launching Disappearance through $drive. The launcher will exit after the game closes."
    $game = Start-Process -FilePath (Join-Path $aliasRoot 'Disappearance.exe') -WorkingDirectory $aliasRoot -PassThru
    if ($focusAvailable) {
        try {
            # Unity can replace its startup window when it enters fullscreen.
            # Keep this bounded so the launcher does not steal focus during gameplay.
            $focusDeadline = [DateTime]::UtcNow.AddSeconds(20)
            $activatedGameWindow = $false
            $lastAttempt = [DateTime]::MinValue
            while ([DateTime]::UtcNow -lt $focusDeadline) {
                $game.Refresh()
                if ($game.HasExited) { break }
                $window = [DisappearanceWindowFocus]::FindVisibleWindow([uint32]$game.Id)
                if ($window -ne [IntPtr]::Zero) {
                    $foregroundProcess = [DisappearanceWindowFocus]::ForegroundProcessId()
                    if ([DisappearanceWindowFocus]::IsForegroundWindow($window)) {
                        if (-not $activatedGameWindow) {
                            # Foreground ownership alone does not ensure keyboard focus.
                            [void][DisappearanceWindowFocus]::Activate($window)
                        }
                        $activatedGameWindow = $true
                    }
                    else {
                        # After confirmed focus, respect a deliberate switch to another app.
                        if ($activatedGameWindow -and $foregroundProcess -ne 0 -and
                            $foregroundProcess -ne $startingForegroundProcess -and
                            $foregroundProcess -ne [uint32]$PID) { break }
                        if (([DateTime]::UtcNow - $lastAttempt).TotalMilliseconds -ge 750) {
                            [void][DisappearanceWindowFocus]::Activate($window)
                            $lastAttempt = [DateTime]::UtcNow
                            if ([DisappearanceWindowFocus]::IsForegroundWindow($window)) {
                                $activatedGameWindow = $true
                            }
                        }
                    }
                }
                Start-Sleep -Milliseconds 200
            }
            # The hidden Steam launcher cannot show diagnostic output. Keep one
            # bounded record so a failed foreground request can be inspected.
            try {
                $focusLog = Join-Path $gameRoot 'BepInEx\cache\launcher-focus.log'
                $focusLogDir = Split-Path -Parent $focusLog
                New-Item -ItemType Directory -Force -Path $focusLogDir | Out-Null
                $foregroundAtEnd = [DisappearanceWindowFocus]::ForegroundProcessId()
                $foregroundWindow = [DisappearanceWindowFocus]::ForegroundWindow()
                $focusSucceeded = $window -ne [IntPtr]::Zero -and
                    [DisappearanceWindowFocus]::IsForegroundWindow($window)
                Set-Content -LiteralPath $focusLog -Encoding UTF8 -Value (
                    "{0:o} gamePid={1} window={2} class={3} acquired={4} foregroundPid={5} foregroundWindow={6} foregroundClass={7}" -f
                    [DateTime]::Now, $game.Id, $window,
                    [DisappearanceWindowFocus]::WindowClass($window), $focusSucceeded,
                    $foregroundAtEnd, $foregroundWindow,
                    [DisappearanceWindowFocus]::WindowClass($foregroundWindow))
            }
            catch { Write-Warning "Could not record focus result: $_" }
        }
        catch {
            Write-Warning "Window activation failed; the game will keep running: $_"
        }
    }
    Wait-Process -Id $game.Id
}
finally {
    & subst $drive /D | Out-Null
}
