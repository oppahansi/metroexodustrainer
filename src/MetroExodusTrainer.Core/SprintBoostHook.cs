namespace MetroExodusTrainer.Core;

/// <summary>
/// Hook at the start of cplayer::vs_actor_movement_ex_reset that makes it return immediately when the sprint
/// flag is held and the caller is one specific script block (the sprint boost). Everything else runs the original
/// code. The function's first instructions are copied into a stub, and the hook only applies when the bytes
/// there are exactly the expected ones, so a different game build is never touched.
/// </summary>
public sealed class SprintBoostHook
{
    private const int StubSize = 128;

    // push rbp; push rsi; push r14; sub rsp,40h; movzx r9d,word ptr [rcx+2552h]  (17 bytes, instruction-aligned)
    private static readonly byte[] Original =
        [0x40, 0x55, 0x56, 0x41, 0x56, 0x48, 0x83, 0xEC, 0x40, 0x44, 0x0F, 0xB7, 0x89, 0x52, 0x25, 0x00, 0x00];

    private nint _stub;
    private int _dataOffset;

    public bool Applied { get; private set; }
    public string? Error { get; private set; }

    public bool TryApply(GameProcess game)
    {
        if (Applied)
            return true;

        nint function = game.Base + Offsets.MovementExReset;
        var current = game.ReadBytes(function, Original.Length);
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

        var stub = BuildStub(function);
        if (!game.WriteCode(_stub, stub))
            return Fail("could not write the hook stub");

        // jmp qword ptr [rip+0] ; <stub address> ; then nop padding up to the end of the copied instructions
        var detour = new List<byte> { 0xFF, 0x25, 0, 0, 0, 0 };
        detour.AddRange(BitConverter.GetBytes((long)_stub));
        while (detour.Count < Original.Length)
            detour.Add(0x90);

        if (!game.WriteCode(function, [.. detour]))
            return Fail("could not install the hook");

        Applied = true;
        Error = null;
        return true;
    }

    /// <summary>Sets the script block (owner pointer) whose removal is suppressed while sprint is held; 0 = none.</summary>
    public void SetOwner(GameProcess game, long owner)
    {
        if (_stub != 0)
            game.Write(_stub + _dataOffset, owner);
    }

    public void Revert(GameProcess game)
    {
        if (!Applied)
            return;
        game.WriteCode(game.Base + Offsets.MovementExReset, Original);
        SetOwner(game, 0);
        Applied = false;
    }

    private byte[] BuildStub(nint function)
    {
        var code = new List<byte>();

        // test dword ptr [rcx+StateReal], SprintStateFlag
        code.AddRange([0xF7, 0x81]);
        code.AddRange(BitConverter.GetBytes((int)Offsets.Player.StateReal));
        code.AddRange(BitConverter.GetBytes((int)Offsets.Player.SprintStateFlag));
        code.AddRange([0x74, 0x0A]);                    // jz run_original (skips cmp 7 + jne 2 + ret 1)

        int cmpPos = code.Count;
        code.AddRange([0x48, 0x3B, 0x15, 0, 0, 0, 0]);  // cmp rdx, qword ptr [rip+data]  (disp patched below)
        code.AddRange([0x75, 0x01]);                    // jne run_original (skips the ret)
        code.Add(0xC3);                                 // ret: sprint held and this is the boost block -> do nothing

        code.AddRange(Original);                        // run_original: displaced instructions
        code.AddRange([0xFF, 0x25, 0, 0, 0, 0]);        // jmp qword ptr [rip+0]
        code.AddRange(BitConverter.GetBytes((long)(function + Original.Length)));

        while (code.Count % 8 != 0)
            code.Add(0xCC);
        _dataOffset = code.Count;
        code.AddRange(new byte[8]);                     // data: owner pointer, 0 until known

        int disp = _dataOffset - (cmpPos + 7);
        var dispBytes = BitConverter.GetBytes(disp);
        for (int i = 0; i < 4; i++)
            code[cmpPos + 3 + i] = dispBytes[i];

        return [.. code];
    }

    private bool Fail(string reason)
    {
        Error = $"sprint boost hook: {reason}";
        return false;
    }
}
