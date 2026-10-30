namespace MetroExodusTrainer.Core;

/// <summary>
/// Build-specific addresses taken from the game's shipped symbols. See docs/research-notes.md.
/// RVAs are relative to the image base; the rest are struct member offsets.
/// </summary>
public static class Offsets
{
    public const string KnownFileVersion = "2.0.1.1";

    public const nint GLevel = 0x1691C40;

    /// <summary>RVA of the vtable for uobject_t&lt;player_new,0,25&gt;; used to confirm an entity is the player.</summary>
    public const long PlayerVtable = 0x11E2728;

    public static class Level
    {
        public const nint StartupEntity = 0x20;
        public const nint ControlEntity = 0x28;
        public const nint ViewEntity = 0x30;
    }

    public static class Player
    {
        public const nint Health = 0x3B8;
        public const nint Immortal = 0xA93;          // bit 7
        public const int ImmortalBit = 7;
        public const nint Invulnerable = 0xA94;      // bit 0
        public const int InvulnerableBit = 0;
        public const nint SpendFilters = 0x161A;
        public const nint MinFilterTime = 0x161C;
        public const nint StaminaFreeze = 0x2778;
        public const nint BaseSpeed = 0x27E0;
        public const nint BaseJump = 0x27E4;
        public const nint Stamina = 0x2A1C;
        public const nint CanUseStamina = 0x2A21;
    }
}
