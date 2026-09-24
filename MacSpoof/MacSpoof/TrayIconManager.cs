using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using WinRT.Interop;

namespace MacSpoof
{
    /// <summary>
    /// Pure C# Win32 System Tray (Notification Area) Manager with interactive context menus.
    /// </summary>
    public sealed class TrayIconManager : IDisposable
    {
        private const int WM_USER = 0x0400;
        public const int WM_TRAYICON = WM_USER + 101;

        private const int NIM_ADD = 0x00000000;
        private const int NIM_MODIFY = 0x00000001;
        private const int NIM_DELETE = 0x00000002;

        private const int NIF_MESSAGE = 0x00000001;
        private const int NIF_ICON = 0x00000002;
        private const int NIF_TIP = 0x00000004;

        public const int WM_LBUTTONUP = 0x0202;
        public const int WM_LBUTTONDBLCLK = 0x0203;
        public const int WM_RBUTTONUP = 0x0205;

        private const uint TPM_RIGHTBUTTON = 0x0002;
        private const uint TPM_RETURNCMD = 0x0100;
        private const uint MF_STRING = 0x00000000;
        private const uint MF_SEPARATOR = 0x00000800;
        private const uint MF_GRAYED = 0x00000001;

        private const int SW_HIDE = 0;
        private const int SW_RESTORE = 9;

        private const int CMD_SPOOF_ONCE = 1001;
        private const int CMD_SHOW_HIDE = 1002;
        private const int CMD_EXIT = 1003;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct NOTIFYICONDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public int uID;
            public int uFlags;
            public int uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
            public int dwState;
            public int dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;
            public int uTimeoutOrVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;
            public int dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA lpData);

        [DllImport("user32.dll")]
        private static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, uint uIDNewItem, string? lpNewItem);

        [DllImport("user32.dll")]
        private static extern uint TrackPopupMenuEx(IntPtr hMenu, uint uFlags, int x, int y, IntPtr hWnd, IntPtr lpTPMParams);

        [DllImport("user32.dll")]
        private static extern bool DestroyMenu(IntPtr hMenu);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", EntryPoint = "ShowWindow")]
        private static extern bool ShowWindowNative(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("comctl32.dll", SetLastError = true)]
        private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr uIdSubclass, IntPtr dwRefData);

        [DllImport("comctl32.dll", SetLastError = true)]
        private static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr uIdSubclass);

        [DllImport("comctl32.dll")]
        private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

        private delegate IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, IntPtr dwRefData);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        private readonly IntPtr _hWnd;
        private readonly MainWindow _mainWindow;
        private readonly SubclassProc _subclassProc;
        private bool _isAdded;
        private bool _subclassInstalled;
        private bool _disposed;
        private string? _lastTooltip;

        public TrayIconManager(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
            _hWnd = WindowNative.GetWindowHandle(_mainWindow);
            _subclassProc = WndProc;

            try
            {
                if (!SetWindowSubclass(_hWnd, _subclassProc, (UIntPtr)1, IntPtr.Zero))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not install the tray window message handler.");

                _subclassInstalled = true;
                AddTrayIcon();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void AddTrayIcon()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_isAdded)
                return;

            IntPtr hIcon = SendMessage(_hWnd, 0x007F /* WM_GETICON */, (IntPtr)1 /* ICON_BIG */, IntPtr.Zero);
            if (hIcon == IntPtr.Zero)
                hIcon = LoadIcon(IntPtr.Zero, (IntPtr)32512 /* IDI_APPLICATION */);

            var nid = new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _hWnd,
                uID = 1001,
                uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
                uCallbackMessage = WM_TRAYICON,
                hIcon = hIcon,
                szTip = "MacSpoof"
            };

            if (!Shell_NotifyIcon(NIM_ADD, ref nid))
                throw new InvalidOperationException("Could not create the system tray icon.");

            _isAdded = true;
            _lastTooltip = "MacSpoof";
        }

        public void UpdateTooltip(string tip)
        {
            if (!_isAdded || _disposed)
                return;

            string normalizedTip = tip.Length > 127 ? tip[..127] : tip;
            if (string.Equals(normalizedTip, _lastTooltip, StringComparison.Ordinal))
                return;

            var nid = new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _hWnd,
                uID = 1001,
                uFlags = NIF_TIP,
                szTip = normalizedTip
            };

            if (Shell_NotifyIcon(NIM_MODIFY, ref nid))
                _lastTooltip = normalizedTip;
        }

        public void RemoveTrayIcon()
        {
            if (!_isAdded)
                return;

            var nid = new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _hWnd,
                uID = 1001
            };

            Shell_NotifyIcon(NIM_DELETE, ref nid);
            _isAdded = false;
            _lastTooltip = null;
        }

        public void ToggleWindow()
        {
            if (IsWindowVisible(_hWnd))
                HideWindow();
            else
                ShowWindow();
        }

        public void ShowWindow()
        {
            ShowWindowNative(_hWnd, SW_RESTORE);
            SetForegroundWindow(_hWnd);
            _mainWindow.OnWindowShown();
        }

        public void HideWindow()
        {
            ShowWindowNative(_hWnd, SW_HIDE);
            _mainWindow.OnWindowHidden();
        }

        private IntPtr WndProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, IntPtr dwRefData)
        {
            if (uMsg == WM_TRAYICON)
            {
                int mouseMsg = (int)lParam;
                if (mouseMsg == WM_LBUTTONUP || mouseMsg == WM_LBUTTONDBLCLK)
                {
                    _mainWindow.DispatcherQueue.TryEnqueue(ToggleWindow);
                }
                else if (mouseMsg == WM_RBUTTONUP)
                {
                    _mainWindow.DispatcherQueue.TryEnqueue(() => _ = ShowContextMenuSafelyAsync());
                }
                return IntPtr.Zero;
            }

            if (uMsg == 0x0112 /* WM_SYSCOMMAND */ && (wParam.ToInt64() & 0xFFF0) == 0xF020 /* SC_MINIMIZE */)
            {
                _mainWindow.DispatcherQueue.TryEnqueue(HideWindow);
                return IntPtr.Zero;
            }

            return DefSubclassProc(hWnd, uMsg, wParam, lParam);
        }

        private async Task ShowContextMenuSafelyAsync()
        {
            try
            {
                await ShowContextMenuAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Tray menu action failed: {ex.Message}");
            }
        }

        private async Task ShowContextMenuAsync()
        {
            IntPtr hMenu = CreatePopupMenu();
            if (hMenu == IntPtr.Zero)
                return;

            uint cmd;
            try
            {
                string currentMac = _mainWindow.CurrentMacAddress;
                AppendMenu(hMenu, MF_STRING | MF_GRAYED, 0, $"MacSpoof ({currentMac})");
                AppendMenu(hMenu, MF_SEPARATOR, 0, null);
                AppendMenu(hMenu, MF_STRING, CMD_SPOOF_ONCE, "Spoof MAC (Once)");
                AppendMenu(hMenu, MF_SEPARATOR, 0, null);

                bool visible = IsWindowVisible(_hWnd);
                AppendMenu(hMenu, MF_STRING, CMD_SHOW_HIDE, visible ? "Hide Window" : "Open MacSpoof");
                AppendMenu(hMenu, MF_STRING, CMD_EXIT, _mainWindow.IsNetworkOperationRunning ? "Exit after current operation" : "Exit");

                GetCursorPos(out POINT pt);
                SetForegroundWindow(_hWnd);
                cmd = TrackPopupMenuEx(hMenu, TPM_RIGHTBUTTON | TPM_RETURNCMD, pt.X, pt.Y, _hWnd, IntPtr.Zero);
            }
            finally
            {
                DestroyMenu(hMenu);
            }

            if (cmd == CMD_SPOOF_ONCE)
            {
                await _mainWindow.TriggerSpoofOnceFromTrayAsync();
            }
            else if (cmd == CMD_SHOW_HIDE)
            {
                ToggleWindow();
            }
            else if (cmd == CMD_EXIT)
            {
                _mainWindow.RequestExit();
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            RemoveTrayIcon();
            if (_subclassInstalled)
            {
                if (!RemoveWindowSubclass(_hWnd, _subclassProc, (UIntPtr)1))
                    System.Diagnostics.Debug.WriteLine("Could not remove the tray window message handler cleanly.");
                _subclassInstalled = false;
            }

            _disposed = true;
        }
    }
}
