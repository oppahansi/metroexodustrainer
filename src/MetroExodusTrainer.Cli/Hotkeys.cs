using System.Runtime.InteropServices;

/// <summary>A global hotkey: a label for the UI and a way to ask whether it was pressed since the last call.</summary>
sealed record Hotkey(string Label, Func<bool> Consume);

/// <summary>
/// Numpad hotkeys (the game uses the F keys). Feature slots run through four layers of nine keys:
/// Num1-Num9, Shift+Num1-Num9, Ctrl+Num1-Num9 and Alt+Num1-Num9. NumLock must be on.
/// <para>
/// Keys come from a low-level keyboard hook instead of polling. With NumLock on, Windows fakes Shift key events
/// around Shift+numpad presses and reports the digit as a navigation key, which makes polled state unreliable.
/// The hook identifies numpad keys by scan code (the keypad cluster, not the extended navigation keys) and tracks
/// the physical modifiers itself, ignoring the fake extended Shift events. Every press is counted, so a press is
/// never lost between two ticks, and the order in which the keys go down does not matter as long as the
/// modifier is held when the numpad key goes down.
/// </para>
/// </summary>
static class Hotkeys
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x100, WmKeyUp = 0x101, WmSysKeyDown = 0x104, WmSysKeyUp = 0x105;
    private const uint FlagExtended = 0x01;
    private const int VkLShift = 0xA0, VkRShift = 0xA1, VkLControl = 0xA2, VkRControl = 0xA3, VkLAlt = 0xA4, VkRAlt = 0xA5;
    private const int VkNumLock = 0x90;

    private const int Layers = 4;
    private const int SlotsPerLayer = 9;
    private static readonly string[] LayerPrefix = ["", "Shift+", "Ctrl+", "Alt+"];

    // Keypad scan codes for Num1..Num9 and Num0 (the extended copies are the real navigation keys).
    private static readonly int[] DigitScanCodes = [0x4F, 0x50, 0x51, 0x4B, 0x4C, 0x4D, 0x47, 0x48, 0x49];
    private const int Num0ScanCode = 0x52;

    // Pending presses per feature slot, and for reset. Written by the hook thread, consumed by the main loop.
    private static readonly int[] Pending = new int[Layers * SlotsPerLayer];
    private static int _resetPending;

    // Physical modifier keys currently held, as tracked from the hook (bit per virtual key below).
    private static int _mods;
    private const int ModShift = 1, ModCtrl = 2, ModAlt = 4;
    private static readonly bool[] _held = new bool[256];
    private static readonly HashSet<int> _digitDown = [];

    private static HookProc? _proc; // keeps the delegate alive for the lifetime of the hook
    private static Thread? _thread;

    public static int FeatureSlots => Layers * SlotsPerLayer;

    public static readonly Hotkey Reset = new("Num0", () => Interlocked.Exchange(ref _resetPending, 0) > 0);

    public static bool NumLockOn => (GetKeyState(VkNumLock) & 1) != 0;

    /// <summary>Installs the keyboard hook on its own thread (it needs a message loop). Call once at startup.</summary>
    public static void Start()
    {
        if (_thread is not null)
            return;
        using var ready = new ManualResetEventSlim();
        _thread = new Thread(() =>
        {
            _proc = Callback;
            SetWindowsHookEx(WhKeyboardLl, _proc, GetModuleHandle(null), 0);
            ready.Set();
            while (GetMessage(out _, 0, 0, 0) > 0) { }
        })
        { IsBackground = true, Name = "keyboard hook" };
        _thread.Start();
        ready.Wait();
    }

    public static Hotkey ForFeature(int index)
    {
        if (index >= FeatureSlots)
            return new Hotkey("(none)", () => false);

        int layer = index / SlotsPerLayer;
        string label = $"{LayerPrefix[layer]}Num{index % SlotsPerLayer + 1}";
        return new Hotkey(label, () => Interlocked.Exchange(ref Pending[index], 0) > 0);
    }

    private static nint Callback(int code, nint wParam, nint lParam)
    {
        if (code >= 0)
        {
            var key = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
            int msg = (int)wParam;
            bool down = msg is WmKeyDown or WmSysKeyDown;
            bool up = msg is WmKeyUp or WmSysKeyUp;
            if (down || up)
                OnKey(key, down);
        }
        return CallNextHookEx(0, code, wParam, lParam);
    }

    private static void OnKey(KbdLlHookStruct key, bool down)
    {
        bool extended = (key.Flags & FlagExtended) != 0;

        // The fake Shift events Windows sends around numpad keys carry the extended flag; real Shift never does.
        int vk = (int)key.VkCode;
        if (vk is VkLShift or VkRShift && extended)
            return;

        int mod = vk switch
        {
            VkLShift or VkRShift => ModShift,
            VkLControl or VkRControl => ModCtrl,
            VkLAlt or VkRAlt => ModAlt,
            _ => 0,
        };
        if (mod != 0)
        {
            _held[vk] = down;
            int bits = 0;
            if (_held[VkLShift] || _held[VkRShift]) bits |= ModShift;
            if (_held[VkLControl] || _held[VkRControl]) bits |= ModCtrl;
            if (_held[VkLAlt] || _held[VkRAlt]) bits |= ModAlt;
            _mods = bits;
            return;
        }

        if (extended)
            return;

        int scan = (int)key.ScanCode;
        if (!down)
        {
            _digitDown.Remove(scan);
            return;
        }
        if (!_digitDown.Add(scan)) // auto-repeat
            return;

        if (scan == Num0ScanCode)
        {
            if (_mods == 0)
                Interlocked.Exchange(ref _resetPending, 1);
            return;
        }

        int slot = Array.IndexOf(DigitScanCodes, scan);
        if (slot < 0)
            return;
        int layer = _mods switch { 0 => 0, ModShift => 1, ModCtrl => 2, ModAlt => 3, _ => -1 };
        if (layer >= 0)
            Interlocked.Exchange(ref Pending[layer * SlotsPerLayer + slot], 1);
    }

    private delegate nint HookProc(int code, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint VkCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public nint Hwnd;
        public uint Message;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public int X, Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, HookProc proc, nint module, uint threadId);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out Msg msg, nint hwnd, uint min, uint max);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? name);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int vKey);
}
