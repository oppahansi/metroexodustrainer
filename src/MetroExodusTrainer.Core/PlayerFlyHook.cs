namespace MetroExodusTrainer.Core;

/// <summary>
/// Hook at the entry of <c>mov_control_nx_player::move_by_vel_ex</c>, the function the game calls every frame to move a
/// character. For calls whose mover belongs to the player the stub records what the game passes (for diagnostics) and,
/// while fly mode is enabled, skips the game's own movement and instead calls <c>move_no_collision</c> with a
/// displacement of (fly velocity x frame time). Every other call runs the original code. The 18 bytes it replaces are
/// copied into the stub and the hook only installs when they are exactly the expected ones.
/// </summary>
public sealed class PlayerFlyHook
{
    private const int StubSize = 256;
    private const int DataSize = 0x50;

    // Data cells (offsets from the block start): player base object, enabled flag, fly velocity, diagnostics, constant 1.0.
    private const int OffPlayerBase = 0x00;
    private const int OffEnabled = 0x08;
    private const int OffVelocity = 0x10;
    private const int OffDiagDtRaw = 0x20;
    private const int OffDiagXmm3 = 0x24;
    private const int OffDiagVelocity = 0x28;
    private const int OffDiagCount = 0x34;
    private const int OffOne = 0x3C;
    private const int OffMover = 0x40;

    private static readonly byte[] Original =
    [
        0x48, 0x89, 0x5C, 0x24, 0x08,       // mov [rsp+8],rbx
        0x48, 0x89, 0x74, 0x24, 0x10,       // mov [rsp+10h],rsi
        0x57,                               // push rdi
        0x48, 0x81, 0xEC, 0x80, 0x00, 0x00, 0x00, // sub rsp,80h
    ];

    // Assembled with keystone (see docs/research-notes.md). Fixups: lea displacement at 3, move_no_collision address
    // at 156, address after the replaced bytes at 195 + 6.
    private static readonly byte[] Template =
    [
        0x4C, 0x8D, 0x1D, 0x11, 0x11, 0x11, 0x11, 0x48, 0x8B, 0x41, 0x28, 0x48, 0x85, 0xC0, 0x0F, 0x84, 0x9B, 0x00, 0x00, 0x00, 0x48, 0x8B, 0x00, 0x49, 0x3B, 0x03, 0x0F, 0x85, 0x8F, 0x00, 0x00, 0x00, 0x49, 0x89, 0x4B, 0x40, 0x8B, 0x44, 0x24, 0x28, 0x41, 0x89, 0x43, 0x20, 0xF3, 0x41, 0x0F, 0x11, 0x5B, 0x24, 0x48, 0x85, 0xD2, 0x74, 0x14, 0x8B, 0x02, 0x41, 0x89, 0x43, 0x28, 0x8B, 0x42, 0x04, 0x41, 0x89, 0x43, 0x2C, 0x8B, 0x42, 0x08, 0x41, 0x89, 0x43, 0x30, 0x41, 0xFF, 0x43, 0x34, 0x41, 0x83, 0x7B, 0x08, 0x00, 0x74, 0x59, 0x48, 0x83, 0xEC, 0x38, 0xF3, 0x0F, 0x10, 0x44, 0x24, 0x60, 0xF3, 0x41, 0x0F, 0x10, 0x4B, 0x10, 0xF3, 0x0F, 0x59, 0xC8, 0xF3, 0x0F, 0x11, 0x4C, 0x24, 0x20, 0xF3, 0x41, 0x0F, 0x10, 0x4B, 0x14, 0xF3, 0x0F, 0x59, 0xC8, 0xF3, 0x0F, 0x11, 0x4C, 0x24, 0x24, 0xF3, 0x41, 0x0F, 0x10, 0x4B, 0x18, 0xF3, 0x0F, 0x59, 0xC8, 0xF3, 0x0F, 0x11, 0x4C, 0x24, 0x28, 0x48, 0x8D, 0x54, 0x24, 0x20, 0xF3, 0x41, 0x0F, 0x10, 0x53, 0x3C, 0x0F, 0x28, 0xDA, 0x48, 0xB8, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22, 0x22, 0xFF, 0xD0, 0x48, 0x83, 0xC4, 0x38, 0xC3, 0x48, 0x89, 0x5C, 0x24, 0x08, 0x48, 0x89, 0x74, 0x24, 0x10, 0x57, 0x48, 0x81, 0xEC, 0x80, 0x00, 0x00, 0x00, 0xFF, 0x25, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    ];

    private const int FixupLeaDisp = 3;
    private const int FixupMoveNoCollision = 160;   // immediate of "mov rax, imm64" (after the 48 B8 opcode)
    private const int FixupReturn = 199;            // 8-byte address after "jmp qword ptr [rip+0]"

    private nint _block;
    private bool _installed;

    public bool Applied => _installed;
    public string? Error { get; private set; }

    public bool TryApply(GameProcess game, nint player)
    {
        if (_installed)
        {
            SetPlayer(game, player);
            return true;
        }

        nint site = game.Base + Offsets.Fly.MoveByVelEx;
        var current = game.ReadBytes(site, Original.Length);
        if (current is null)
            return Fail("could not read game code");
        if (!current.AsSpan().SequenceEqual(Original))
            return Fail("game code differs from the expected build");

        if (_block == 0)
        {
            _block = game.AllocExecutable(DataSize + StubSize);
            if (_block == 0)
                return Fail("could not allocate memory in the game");
        }

        nint stub = _block + DataSize;
        var code = (byte[])Template.Clone();
        BitConverter.GetBytes((int)(_block - (stub + FixupLeaDisp + 4))).CopyTo(code, FixupLeaDisp);
        BitConverter.GetBytes((long)(game.Base + Offsets.Fly.MoveNoCollision)).CopyTo(code, FixupMoveNoCollision);
        BitConverter.GetBytes((long)(site + Original.Length)).CopyTo(code, FixupReturn);

        game.Write(_block + OffEnabled, 0);
        game.Write(_block + OffOne, 1f);
        SetVelocity(game, 0, 0, 0);
        SetPlayer(game, player);

        if (!game.WriteCode(stub, code))
            return Fail("could not write the hook stub");

        var detour = new List<byte> { 0xFF, 0x25, 0, 0, 0, 0 };
        detour.AddRange(BitConverter.GetBytes((long)stub));
        while (detour.Count < Original.Length)
            detour.Add(0x90);
        if (!game.WriteCode(site, [.. detour]))
            return Fail("could not install the hook");

        _installed = true;
        Error = null;
        return true;
    }

    public void SetPlayer(GameProcess game, nint player)
    {
        if (_block != 0)
            game.Write(_block + OffPlayerBase, player == 0 ? 1L : (long)(player + Offsets.Sound.SharedBaseInEntity));
    }

    public void SetEnabled(GameProcess game, bool enabled)
    {
        if (_block != 0)
            game.Write(_block + OffEnabled, enabled ? 1 : 0);
    }

    /// <summary>Fly velocity in game units per second (x, y, z; y is up).</summary>
    public void SetVelocity(GameProcess game, float x, float y, float z)
    {
        if (_block == 0)
            return;
        game.Write(_block + OffVelocity, x);
        game.Write(_block + OffVelocity + 4, y);
        game.Write(_block + OffVelocity + 8, z);
    }

    /// <summary>What the game last passed to the hooked function for the player, and how many calls were seen.</summary>
    public (int Count, float DtRaw, float Xmm3, float Vx, float Vy, float Vz) ReadDiagnostics(GameProcess game)
    {
        if (_block == 0)
            return default;
        return (game.Read<int>(_block + OffDiagCount), game.Read<float>(_block + OffDiagDtRaw),
            game.Read<float>(_block + OffDiagXmm3), game.Read<float>(_block + OffDiagVelocity),
            game.Read<float>(_block + OffDiagVelocity + 4), game.Read<float>(_block + OffDiagVelocity + 8));
    }

    /// <summary>The player's mov_control_nx_player as last seen by the hook, or 0.</summary>
    public nint ReadMover(GameProcess game) => _block == 0 ? 0 : game.ReadPointer(_block + OffMover);

    public void Revert(GameProcess game)
    {
        if (_installed)
            game.WriteCode(game.Base + Offsets.Fly.MoveByVelEx, Original);
        if (_block != 0)
        {
            game.Write(_block + OffEnabled, 0);
            game.Write(_block + OffPlayerBase, 1L);
        }
        _installed = false;
    }

    private bool Fail(string reason)
    {
        Error = $"fly hook: {reason}";
        return false;
    }
}
