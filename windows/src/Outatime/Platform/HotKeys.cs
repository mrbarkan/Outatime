using System.Runtime.InteropServices;

namespace Outatime.App.Platform;

/// Ctrl+Alt+Shift+W starts or stops Work and Ctrl+Alt+Shift+B Break, from any app. Windows only: a message-only
/// window receives WM_HOTKEY through the app's own message loop.
public static class HotKeys
{
    static readonly (Activity Activity, uint Key)[] Keys = [(Activity.Work, 'W'), (Activity.Break, 'B')];
    static Action<Activity> action = _ => { };
    static IntPtr window;
    static WndProc? proc;  // kept alive: the native side calls it
    static bool registered;

    public static void Install(Action<Activity> onPress)
    {
        action = onPress;
        if (!OperatingSystem.IsWindows() || window != IntPtr.Zero) return;
        try
        {
            proc = Proc;
            var cls = new WNDCLASSEX { cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(), lpfnWndProc = Marshal.GetFunctionPointerForDelegate(proc),
                                       hInstance = GetModuleHandle(null), lpszClassName = "OutatimeHotKeys" };
            RegisterClassEx(ref cls);
            window = CreateWindowEx(0, "OutatimeHotKeys", "", 0, 0, 0, 0, 0, new IntPtr(-3) /* HWND_MESSAGE */, IntPtr.Zero, cls.hInstance, IntPtr.Zero);
        }
        catch (Exception) { window = IntPtr.Zero; }
    }

    public static void SetEnabled(bool on)
    {
        if (window == IntPtr.Zero) return;
        if (registered)
            for (var i = 0; i < Keys.Length; i++) UnregisterHotKey(window, i + 1);
        registered = false;
        if (!on) return;
        const uint alt = 0x1, control = 0x2, shift = 0x4, noRepeat = 0x4000;
        for (var i = 0; i < Keys.Length; i++) RegisterHotKey(window, i + 1, control | alt | shift | noRepeat, Keys[i].Key);
        registered = true;
    }

    static IntPtr Proc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        const uint WM_HOTKEY = 0x0312;
        if (msg == WM_HOTKEY && wParam.ToInt32() is var id && id >= 1 && id <= Keys.Length)
        {
            action(Keys[id - 1].Activity);
            return IntPtr.Zero;
        }
        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WNDCLASSEX
    {
        public uint cbSize, style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern ushort RegisterClassEx(ref WNDCLASSEX cls);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr CreateWindowEx(uint exStyle, string cls, string name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")] static extern IntPtr DefWindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string? name);
}
