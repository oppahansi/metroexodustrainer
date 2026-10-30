using System.Runtime.InteropServices;

/// <summary>A global hotkey: a label for the UI and a way to ask whether it is held right now.</summary>
sealed record Hotkey(string Label, Func<bool> IsDown);

/// <summary>
/// Numpad hotkeys (the game uses the F keys). Feature slots run through four layers of nine keys:
/// Num1-Num9, Shift+Num1-Num9, Ctrl+Num1-Num9 and Alt+Num1-Num9. NumLock must be on.
/// A layer only counts when exactly its own modifier is held. Windows turns Shift+numpad digits into
/// navigation keys while NumLock is on (Shift+Num1 reports End, Shift+Num2 reports Down, ...), so the Shift layer
/// listens for those as well.
/// </summary>
static class Hotkeys
{
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkAlt = 0x12;
    private const int VkNumLock = 0x90;
    private const int VkNumpad0 = 0x60;
    private const int VkNumpad1 = 0x61;

    private enum Layer { None, Shift, Ctrl, Alt }

    // What Shift + Num1..Num9 reports while NumLock is on: End, Down, PageDown, Left, Clear, Right, Home, Up, PageUp.
    private static readonly int[] ShiftedNumpad = [0x23, 0x28, 0x22, 0x25, 0x0C, 0x27, 0x24, 0x26, 0x21];

    private static readonly string[] LayerPrefix = ["", "Shift+", "Ctrl+", "Alt+"];

    public static int FeatureSlots => ShiftedNumpad.Length * LayerPrefix.Length;

    public static readonly Hotkey Reset = new("Num0", () => LayerHeld(Layer.None) && Down(VkNumpad0));

    public static bool NumLockOn => (GetKeyState(VkNumLock) & 1) != 0;

    public static Hotkey ForFeature(int index)
    {
        if (index >= FeatureSlots)
            return new Hotkey("(none)", () => false);

        var layer = (Layer)(index / ShiftedNumpad.Length);
        int slot = index % ShiftedNumpad.Length;
        string label = $"{LayerPrefix[(int)layer]}Num{slot + 1}";

        return layer == Layer.Shift
            ? new Hotkey(label, () => LayerHeld(layer) && (Down(VkNumpad1 + slot) || Down(ShiftedNumpad[slot])))
            : new Hotkey(label, () => LayerHeld(layer) && Down(VkNumpad1 + slot));
    }

    /// <summary>True when exactly the modifier of the layer is held (none for the base layer).</summary>
    private static bool LayerHeld(Layer layer) =>
        Down(VkShift) == (layer == Layer.Shift) &&
        Down(VkControl) == (layer == Layer.Ctrl) &&
        Down(VkAlt) == (layer == Layer.Alt);

    private static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int vKey);
}
