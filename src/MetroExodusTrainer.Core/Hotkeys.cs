using System.Runtime.InteropServices;

namespace MetroExodusTrainer.Core;

/// <summary>A global hotkey: a label for the UI and a way to ask whether it is held right now.</summary>
public sealed record Hotkey(string Label, Func<bool> IsDown);

/// <summary>
/// Numpad hotkeys (the game uses the F keys). Feature slots run through three layers of nine keys:
/// Num1-Num9, Ctrl+Num1-Num9 and Alt+Num1-Num9. NumLock must be on. A layer only counts when exactly its own
/// modifier is held. Shift is not used: Windows turns Shift+numpad digits into navigation keys and fakes Shift
/// key events around them, which made those combinations unreliable.
/// </summary>
public static class Hotkeys
{
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkAlt = 0x12;
    private const int VkNumLock = 0x90;
    private const int VkNumpad0 = 0x60;
    private const int VkNumpad1 = 0x61;

    private enum Layer { None, Ctrl, Alt }

    private const int SlotsPerLayer = 9;

    private static readonly string[] LayerPrefix = ["", "Ctrl+", "Alt+"];

    // Windows briefly reports Shift as released while a Shift+numpad combination is pressed, so a bare numpad key
    // only counts once no modifier has been seen for this long.
    private const long ModifierQuietMs = 100;
    private static long _modifierSeen = long.MinValue / 2;

    /// <summary>Call once per tick, before reading hotkeys, so the modifier history is current.</summary>
    public static void Update()
    {
        if (Down(VkShift) || Down(VkControl) || Down(VkAlt))
            _modifierSeen = Environment.TickCount64;
    }

    public static int FeatureSlots => SlotsPerLayer * LayerPrefix.Length;

    public static readonly Hotkey Reset = new("Num0", () => LayerHeld(Layer.None) && Down(VkNumpad0));

    /// <summary>
    /// Turns a raw "is down" reading into a stable one: a key counts as released only after it has been up for a
    /// short time, so the key flicker Windows produces around Shift+numpad does not look like a second press.
    /// </summary>
    public sealed class Debouncer
    {
        private const long ReleaseMs = 50;
        private long _lastDown = long.MinValue / 2;

        public bool Held(bool rawDown)
        {
            long now = Environment.TickCount64;
            if (rawDown)
                _lastDown = now;
            return now - _lastDown < ReleaseMs;
        }
    }

    public static bool NumLockOn => (GetKeyState(VkNumLock) & 1) != 0;

    public static Hotkey ForFeature(int index)
    {
        if (index < 0 || index >= FeatureSlots)
            return new Hotkey("(none)", () => false);

        var layer = (Layer)(index / SlotsPerLayer);
        int slot = index % SlotsPerLayer;
        string label = $"{LayerPrefix[(int)layer]}Num{slot + 1}";

        return new Hotkey(label, () => LayerHeld(layer) && Down(VkNumpad1 + slot));
    }

    /// <summary>True when exactly the modifier of the layer is held (none for the base layer).</summary>
    private static bool LayerHeld(Layer layer) =>
        !Down(VkShift) &&
        Down(VkControl) == (layer == Layer.Ctrl) &&
        Down(VkAlt) == (layer == Layer.Alt) &&
        (layer != Layer.None || Environment.TickCount64 - _modifierSeen > ModifierQuietMs);

    private static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    /// <summary>True while the given virtual key is held (any window).</summary>
    public static bool KeyDown(int vk) => Down(vk);

    /// <summary>Process id that owns the foreground window, or 0.</summary>
    public static int ForegroundProcessId()
    {
        nint window = GetForegroundWindow();
        if (window == 0)
            return 0;
        GetWindowThreadProcessId(window, out uint pid);
        return (int)pid;
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int vKey);
}
