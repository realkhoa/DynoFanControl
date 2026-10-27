using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace DynoFanControl.Services;

/// <summary>
/// Native Win32 System Tray implementation for WPF with modern WPF ContextMenu support.
/// </summary>
public sealed class TrayIconManager : IDisposable
{
    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_MODIFY = 0x00000001;
    private const uint NIM_DELETE = 0x00000002;
    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;

    private const int WM_USER = 0x0400;
    private const int WM_TRAYICON = WM_USER + 2048;

    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP = 0x0205;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr ExtractIcon(IntPtr hInst, string lpszExeFileName, int nIconIndex);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    private readonly Window _parentWindow;
    private readonly ContextMenu _contextMenu;
    private IntPtr _hwnd;
    private IntPtr _hIcon;
    private bool _isAdded;
    private bool _disposed;

    public event Action? OnOpenRequested;

    public TrayIconManager(Window parentWindow, ContextMenu contextMenu)
    {
        _parentWindow = parentWindow;
        _contextMenu = contextMenu;
    }

    public void Initialize()
    {
        try
        {
            var helper = new WindowInteropHelper(_parentWindow);
            _hwnd = helper.EnsureHandle();

            var source = HwndSource.FromHwnd(_hwnd);
            source?.AddHook(WndProc);

            // Load Icon from executable or fallback
            var exePath = Environment.ProcessPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DynoFanControl.exe");
            _hIcon = ExtractIcon(IntPtr.Zero, exePath, 0);
            if (_hIcon == IntPtr.Zero)
            {
                _hIcon = LoadIcon(IntPtr.Zero, (IntPtr)32512); // IDI_APPLICATION
            }

            var nid = new NOTIFYICONDATA
            {
                cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _hwnd,
                uID = 1001,
                uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
                uCallbackMessage = WM_TRAYICON,
                hIcon = _hIcon,
                szTip = "DynoFanControl — Acer Nitro 5"
            };

            _isAdded = Shell_NotifyIcon(NIM_ADD, ref nid);
        }
        catch
        {
            // Suppress tray initialization errors
        }
    }

    public void UpdateTooltip(string text)
    {
        if (!_isAdded || _hwnd == IntPtr.Zero) return;

        var tip = text.Length > 127 ? text.Substring(0, 127) : text;
        var nid = new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 1001,
            uFlags = NIF_TIP,
            szTip = tip
        };

        Shell_NotifyIcon(NIM_MODIFY, ref nid);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_TRAYICON)
        {
            int mouseMsg = lParam.ToInt32();
            if (mouseMsg is WM_LBUTTONUP or WM_LBUTTONDBLCLK)
            {
                OnOpenRequested?.Invoke();
                handled = true;
            }
            else if (mouseMsg == WM_RBUTTONUP)
            {
                SetForegroundWindow(_hwnd);
                _contextMenu.IsOpen = true;
                handled = true;
            }
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            if (_isAdded && _hwnd != IntPtr.Zero)
            {
                var nid = new NOTIFYICONDATA
                {
                    cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
                    hWnd = _hwnd,
                    uID = 1001
                };
                Shell_NotifyIcon(NIM_DELETE, ref nid);
                _isAdded = false;
            }

            if (_hIcon != IntPtr.Zero)
            {
                DestroyIcon(_hIcon);
                _hIcon = IntPtr.Zero;
            }

            _disposed = true;
        }
    }
}
