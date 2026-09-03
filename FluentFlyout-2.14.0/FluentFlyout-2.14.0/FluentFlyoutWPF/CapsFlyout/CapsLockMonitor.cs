using System.ComponentModel;
using System.Runtime.InteropServices;

namespace FluentFlyoutWPF.CapsFlyout;

internal sealed class CapsLockMonitor : IDisposable
{
    private NativeMethods.LowLevelKeyboardProc? _callback;
    private IntPtr _hook;

    internal event Action<bool>? StateChanged;

    internal void Start()
    {
        if (_hook != IntPtr.Zero)
            return;

        _callback = HookCallback;
        _hook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL,
            _callback,
            NativeMethods.GetModuleHandle(null),
            0);

        if (_hook == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法安装 Caps Lock 键盘监听器。");
    }

    private IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 &&
            (wParam.ToInt32() == NativeMethods.WM_KEYUP || wParam.ToInt32() == NativeMethods.WM_SYSKEYUP))
        {
            var data = Marshal.PtrToStructure<NativeMethods.KeyboardHookData>(lParam);
            if (data.VirtualKeyCode == NativeMethods.VK_CAPITAL)
                StateChanged?.Invoke((NativeMethods.GetKeyState(NativeMethods.VK_CAPITAL) & 1) != 0);
        }

        return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            _ = NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
        _callback = null;
    }
}
