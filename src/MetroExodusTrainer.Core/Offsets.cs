namespace MetroExodusTrainer.Core;

/// <summary>
/// Build-specific addresses taken from the game's shipped symbols. See docs/research-notes.md.
/// RVAs are relative to the image base; the rest are struct member offsets.
/// </summary>
public static class Offsets
{
    public const string KnownFileVersion = "2.0.1.1";

    public const nint GLevel = 0x1691C40;

    /// <summary>
    /// RVA of ps_actor_flags, a u32 of developer cheat flags that game code tests directly:
    /// bit 0 god mode, bit 3 unlimited ammo / medkits / throwables, bit 7 gas mask filters and suffocation.
    /// </summary>
    public const nint PsActorFlags = 0x15A8824;

    public static class ActorFlags
    {
        public const uint UnlimitedItems = 0x08;
        public const uint FreeGasMask = 0x80;
    }

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

    /// <summary>Default value of g_player_speed_scale observed in game (the engine initializer, not 1.0).</summary>
    public const float DefaultPlayerSpeedScale = 1.25f;

    /// <summary>Inventory object and item layouts (entity = item pointer - ItemOffsetInEntity).</summary>
    public static class Inventory
    {
        public const nint AllItems = 0x278;          // u_vector<inventory_item*> inside inventory_player
        public const nint ItemOffsetInEntity = 0x620; // inventory_item subobject follows the 0x620-byte centity
        public const nint WeaponAmmoLoaded = 0x1940; // weapon_magazined::_ammo_loaded (u32)
        public const nint AmmoBoxValue = 0x690;      // weapon_ammo units = reserve rounds of that ammo type (u16)
        public const nint EntityStaticParams = 0x240; // pointer into the entity's static params block
        public const nint AmmoKeyBias = 0x2F8;       // ammo type key (u32) = *(params - 0x2F8)
        public const nint CraftResBias = 0x2EC;      // is_craft_res (byte) = *(params - 0x2EC); materials/chemicals
        public const nint MedkitCount = 0x9D0;       // decremented by medkit::apply_medkit (u32)
        public const nint MissileUnits = 0x190;      // missile_weapon units (u16), decremented by throw_me
    }

    /// <summary>Fields of weapon_magazined-derived firearms (offsets from the weapon entity).</summary>
    public static class Weapon
    {
        /// <summary>RVA of weapon_magazined::recoil_curve_evaluate, which turns the recoil level into kick.</summary>
        public const nint RecoilCurveEvaluate = 0x8F2F90;

        public const nint CurrentDispersion = 0x9E0; // float, spread added by movement and firing
        public const nint ShotInterval = 0xA64;      // float seconds between shots
        public const nint RecoilLevel = 0x1970;      // float
        public const nint RecoilVertical = 0x1974;   // float
        public const nint RecoilHorizontal = 0x1978; // float
        public const nint RecoilShake = 0x197C;      // float
        public const nint RecoilHorizRandom = 0x198C; // float
        public const nint Heat = 0x1990;             // float 0..1, overheats at the weapon's critical heat
        public const nint JamState = 0x19A0;         // e_jam byte, 0 = not jammed
    }

    public static class Level
    {
        // Per-tier carry limits: entry = *(level + LimitTable) + tier * LimitEntryStride; max for an ammo key =
        // u32 at *(entry + LimitByAmmoKey) + key * 4. 0xFFFFFFFF means no limit.
        public const nint LimitTable = 0x2A580;
        public const nint LimitEntryStride = 0x130;
        public const nint LimitByAmmoKey = 0x70;
        // Per-suit parameter table that serial_update_suit copies speed and jump from.
        public const nint SuitTable = 0x2A4E0;       // pointer to entries of SuitStride bytes
        public const nint SuitCount = 0x2A4EA;       // u16
        public const nint SuitStride = 0x6C;
        public const nint SuitSpeed = 0x60;
        public const nint SuitJump = 0x64;

        public const nint StartupEntity = 0x20;
        public const nint ControlEntity = 0x28;
        public const nint ViewEntity = 0x30;
    }

    public static class Player
    {
        public const nint Health = 0x3B8;
        public const nint Inventory = 0x10F0;        // pointer to the player's inventory object
        public const nint SpeedModArray = 0x2548;    // pointer to vs_speed_mod entries (24 bytes: current, target, blend, ?, block*)
        public const nint SpeedModCount = 0x2552;    // u16 size of the stack; the top entry is the active base speed
        public const uint SprintStateFlag = 0x400;   // set in StateReal while the sprint key is active
        public const nint SpeedModStride = 24;
        public const nint StateReal = 0x1D50;        // movement state bit flags
        public const nint VsMoveAllowed = 0x2528;    // bytes: walk, run, sprint, crouch, jump; scripts clear them
        public const int VsMoveAllowedSprint = 2;
        public const nint Immortal = 0xA93;          // bit 7
        public const int ImmortalBit = 7;
        public const nint Invulnerable = 0xA94;      // bit 0
        public const int InvulnerableBit = 0;
        public const nint SpendFilters = 0x161A;
        public const nint MinFilterTime = 0x161C;
        public const nint StaminaFreeze = 0x2778;
        public const nint LimitTier = 0x2A40;        // index into the level's carry-limit table
        public const nint SuitIndex = 0x27D8;        // which suit table entry applies
        public const nint BaseSpeed = 0x27E0;
        public const nint BaseJump = 0x27E4;
        public const nint Stamina = 0x2A1C;
        public const nint CanUseStamina = 0x2A21;    // cleared by use_stamina when exhausted; can_sprint/can_jump need it set
    }
}
