using System.Runtime.InteropServices;

/// <summary>A global hotkey: a label for the UI and a way to ask whether it is held right now.</summary>
sealed record Hotkey(string Label, Func<bool> IsDown);

/// <summary>
/// Numpad hotkeys (the game uses the F keys). Features 1-9 are Num1-Num9; features 10-18 are Shift+Num1-Num9.
/// NumLock must be on. Windows turns Shift+numpad digits into navigation keys while NumLock is on
/// (Shift+Num1 reports End, Shift+Num2 reports Down, ...), so the shifted layer listens for those as well.
/// </summary>
static class Hotkeys
{
    private const int VkShift = 0x10;
    private const int VkNumLock = 0x90;
    private const int VkNumpad0 = 0x60;
    private const int VkNumpad1 = 0x61;

    // What Shift + Num1..Num9 reports while NumLock is on: End, Down, PageDown, Left, Clear, Right, Home, Up, PageUp.
    private static readonly int[] ShiftedNumpad = [0x23, 0x28, 0x22, 0x25, 0x0C, 0x27, 0x24, 0x26, 0x21];

    public static int FeatureSlots => ShiftedNumpad.Length * 2;

    public static readonly Hotkey Reset = new("Num0", () => !ShiftDown && Down(VkNumpad0));

    public static bool NumLockOn => (GetKeyState(VkNumLock) & 1) != 0;

    public static Hotkey ForFeature(int index)
    {
        if (index >= FeatureSlots)
            return new Hotkey("(none)", () => false);

        int slot = index % ShiftedNumpad.Length;
        if (index < ShiftedNumpad.Length)
            return new Hotkey($"Num{slot + 1}", () => !ShiftDown && Down(VkNumpad1 + slot));

        return new Hotkey($"Shift+Num{slot + 1}", () => ShiftDown && (Down(VkNumpad1 + slot) || Down(ShiftedNumpad[slot])));
    }

    private static bool ShiftDown => Down(VkShift);

    private static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int vKey);
}
