namespace MetroExodusTrainer.Core;

/// <summary>
/// Hook at the end of weapon_magazined::calc_shot_interval that multiplies the returned interval (xmm6, copied to
/// xmm0 on return) by a factor kept in the stub's data area. Factor 1.0 changes nothing. The 16 bytes it replaces
/// are copied into the stub, and the hook only applies when those bytes are exactly the expected ones.
/// </summary>
public sealed class ShotIntervalHook
{
    private const int StubSize = 96;

    // mov rbx,[rsp+90h]; movaps xmm0,xmm6; movaps xmm6,[rsp+40h]  (16 bytes, instruction-aligned, no relative operands)
    private static readonly byte[] Original =
    [
        0x48, 0x8B, 0x9C, 0x24, 0x90, 0x00, 0x00, 0x00,
        0x0F, 0x28, 0xC6,
        0x0F, 0x28, 0x74, 0x24, 0x40,
    ];

    private nint _stub;
    private int _dataOffset;

    public bool Applied { get; private set; }
    public string? Error { get; private set; }

    public bool TryApply(GameProcess game, float factor)
    {
        if (Applied)
        {
            SetFactor(game, factor);
            return true;
        }

        nint site = game.Base + Offsets.Weapon.CalcShotIntervalReturn;
        var current = game.ReadBytes(site, Original.Length);
        if (current is null)
            return Fail("could not read game code");
        if (!current.AsSpan().SequenceEqual(Original))
            return Fail("game code differs from the expected build");

        if (_stub == 0)
        {
            _stub = game.AllocExecutable(StubSize);
            if (_stub == 0)
                return Fail("could not allocate memory in the game");
        }

        if (!game.WriteCode(_stub, BuildStub(site, factor)))
            return Fail("could not write the hook stub");

        // jmp qword ptr [rip+0] ; <stub address> ; then nop padding up to the end of the copied instructions
        var detour = new List<byte> { 0xFF, 0x25, 0, 0, 0, 0 };
        detour.AddRange(BitConverter.GetBytes((long)_stub));
        while (detour.Count < Original.Length)
            detour.Add(0x90);

        if (!game.WriteCode(site, [.. detour]))
            return Fail("could not install the hook");

        Applied = true;
        Error = null;
        return true;
    }

    public void SetFactor(GameProcess game, float factor)
    {
        if (_stub != 0)
            game.Write(_stub + _dataOffset, factor);
    }

    public void Revert(GameProcess game)
    {
        if (!Applied)
            return;
        game.WriteCode(game.Base + Offsets.Weapon.CalcShotIntervalReturn, Original);
        SetFactor(game, 1f);
        Applied = false;
    }

    private byte[] BuildStub(nint site, float factor)
    {
        var code = new List<byte>();

        code.AddRange(Original[..8]);                   // mov rbx,[rsp+90h]
        int mulPos = code.Count;
        code.AddRange([0xF3, 0x0F, 0x59, 0x35, 0, 0, 0, 0]); // mulss xmm6, dword ptr [rip+data]  (disp patched below)
        code.AddRange(Original[8..]);                   // movaps xmm0,xmm6; movaps xmm6,[rsp+40h]
        code.AddRange([0xFF, 0x25, 0, 0, 0, 0]);        // jmp qword ptr [rip+0]
        code.AddRange(BitConverter.GetBytes((long)(site + Original.Length)));

        while (code.Count % 8 != 0)
            code.Add(0xCC);
        _dataOffset = code.Count;
        code.AddRange(BitConverter.GetBytes(factor));
        code.AddRange(new byte[4]);

        var disp = BitConverter.GetBytes(_dataOffset - (mulPos + 8));
        for (int i = 0; i < 4; i++)
            code[mulPos + 4 + i] = disp[i];

        return [.. code];
    }

    private bool Fail(string reason)
    {
        Error = $"shot interval hook: {reason}";
        return false;
    }
}
