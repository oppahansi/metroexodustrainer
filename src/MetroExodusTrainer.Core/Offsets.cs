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

    /// <summary>RVA of the float inside the g_player_speed_scale console variable; multiplied into movement speed.</summary>
    public const nint PlayerSpeedScale = 0x163DCE8;

    /// <summary>
    /// RVA of the instruction in cplayer::vs_actor_movement_allowed_set that stores the sprint-allowed byte
    /// (mov [rcx+0x252a], al = 88 81 2a 25 00 00).
    /// </summary>
    public const nint SprintAllowedStore = 0xA4CDA2;

    /// <summary>RVA of cplayer::vs_actor_movement_ex_reset(owner block in rdx), which removes script speed modifiers.</summary>
    public const nint MovementExReset = 0xA4C9A0;

    public static class Level
    {
        public const nint StartupEntity = 0x20;
        public const nint ControlEntity = 0x28;
        public const nint ViewEntity = 0x30;
    }

    public static class Player
    {
        public const nint Health = 0x3B8;
        public const nint StaticParams = 0x240;      // pointer to per-entity static params
        public const nint StaticBaseSpeed = 0xE04;   // float inside static params; base speed when the mod stack is empty
        public const nint SpeedModArray = 0x2548;    // pointer to vs_speed_mod entries (24 bytes: current, target, blend, ?, block*)
        public const nint SpeedModCapacity = 0x2550; // u16 allocated entries; the array is kept after the stack empties
        public const nint SpeedModCount = 0x2552;    // u16 size of the stack; the top entry is the active base speed
        public const uint SprintStateFlag = 0x400;   // set in StateReal while the sprint key is active
        public const nint SpeedModStride = 24;
        public const nint EvadeState = 0x1491;
        public const nint MoveSpeed = 0x14A0;
        public const nint AnimSpeedCoef = 0xFB0;
        public const nint StateTarget = 0x1D54;
        public const nint StateReal = 0x1D50;        // movement state bit flags
        public const nint TiredForSprint = 0x1D6E;
        public const nint DamagedForSprint = 0x1D6F;
        public const nint VsSpeed = 0x24F0;          // walk, run, sprint, crouch multipliers (floats)
        public const nint VsSpeedTarget = 0x250C;
        public const nint VsSpeedBlend = 0x2530;
        public const nint VsMoveAllowed = 0x2528;    // bytes: walk, run, sprint, crouch, jump; scripts clear them
        public const int VsMoveAllowedSprint = 2;
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
        public const nint StaminaState = 0x2A20;     // estamina_state: 0 idle, 1 recovering, 2 in use
        public const nint CanUseStamina = 0x2A21;    // cleared by use_stamina when exhausted; can_sprint/can_jump need it set
    }
}
