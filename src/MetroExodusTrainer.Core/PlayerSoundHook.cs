namespace MetroExodusTrainer.Core;

/// <summary>
/// Makes NPCs ignore sounds made by the player. Two functions where an NPC takes in a sound are detoured: the sound
/// memory (<c>sound_memory_manager::on_sound</c>) and the threat memory (<c>base_threat_memory_manager::on_sound</c>).
/// Each stub looks up the sound's source object through its shared part pointer and returns "not heard" when it is the
/// player; every other sound runs the original code. The player's object address lives in the stub's data cell.
/// A site is only patched when its original bytes are exactly the expected ones.
/// </summary>
public sealed class PlayerSoundHook
{
    private sealed record Site(nint Rva, byte[] Original, byte SourceOffset);

    // Built when the hook is installed: the addresses are only known once the game build has been resolved.
    private static Site[] CreateSites() =>
    [
        // prologue of sound_memory_manager::on_sound; the sound's source shared part is at [rdx+8]
        new(Offsets.Sound.SoundMemoryOnSound,
        [
            0x4C, 0x89, 0x4C, 0x24, 0x20,   // mov [rsp+20h],r9
            0x44, 0x89, 0x44, 0x24, 0x18,   // mov [rsp+18h],r8d
            0x55, 0x53, 0x57,               // push rbp; push rbx; push rdi
            0x48, 0x8D, 0x6C, 0x24, 0xE0,   // lea rbp,[rsp-20h]
        ], 0x08),
        // prologue of base_threat_memory_manager::on_sound; the source weak pointer is at [rdx+28h]
        new(Offsets.Sound.ThreatMemoryOnSound,
        [
            0x48, 0x89, 0x5C, 0x24, 0x08,   // mov [rsp+8],rbx
            0x48, 0x89, 0x74, 0x24, 0x10,   // mov [rsp+10h],rsi
            0x57,                           // push rdi
            0x48, 0x83, 0xEC, 0x70,         // sub rsp,70h
        ], 0x28),
    ];

    private const int StubSize = 64;
    private const int DataSize = 16;

    private nint _block;
    private Site[] _sites = [];
    private readonly bool[] _installed = new bool[2];

    public bool Applied { get; private set; }
    public string? Error { get; private set; }

    /// <summary>Installs the hooks (if needed) and tells them which object is the player.</summary>
    public bool TryApply(GameProcess game, nint player)
    {
        if (!Applied)
        {
            _sites = CreateSites();
            foreach (var site in _sites)
            {
                var current = game.ReadBytes(game.Base + site.Rva, site.Original.Length);
                if (current is null)
                    return Fail("could not read game code");
                if (!current.AsSpan().SequenceEqual(site.Original))
                    return Fail("game code differs from the expected build");
            }

            if (_block == 0)
            {
                _block = game.AllocExecutable(DataSize + StubSize * _sites.Length);
                if (_block == 0)
                    return Fail("could not allocate memory in the game");
            }

            game.Write(_block, 1L); // matches no object until a player is known

            for (int i = 0; i < _sites.Length; i++)
            {
                nint stub = _block + DataSize + StubSize * i;
                nint site = game.Base + _sites[i].Rva;
                if (!game.WriteCode(stub, BuildStub(_sites[i], site, stub)))
                    return Fail("could not write the hook stub");
            }

            for (int i = 0; i < _sites.Length; i++)
            {
                nint stub = _block + DataSize + StubSize * i;
                var detour = new List<byte> { 0xFF, 0x25, 0, 0, 0, 0 };
                detour.AddRange(BitConverter.GetBytes((long)stub));
                while (detour.Count < _sites[i].Original.Length)
                    detour.Add(0x90);
                if (!game.WriteCode(game.Base + _sites[i].Rva, [.. detour]))
                {
                    Revert(game);
                    return Fail("could not install the hook");
                }
                _installed[i] = true;
            }

            Applied = true;
            Error = null;
        }

        SetPlayer(game, player);
        return true;
    }

    /// <summary>The address NPC sound sources are compared against: the player's shared base object (entity + 0xE0).</summary>
    public void SetPlayer(GameProcess game, nint player)
    {
        if (_block != 0)
            game.Write(_block, player == 0 ? 1L : (long)(player + Offsets.Sound.SharedBaseInEntity));
    }

    public void Revert(GameProcess game)
    {
        if (_sites.Length == 0)
            _sites = CreateSites();
        for (int i = 0; i < _sites.Length; i++)
        {
            if (_installed[i])
                game.WriteCode(game.Base + _sites[i].Rva, _sites[i].Original);
            else
                Detour.RemoveLeftover(game, game.Base + _sites[i].Rva, _sites[i].Original);
            _installed[i] = false;
        }
        if (_block != 0)
            game.Write(_block, 1L);
        Applied = false;
    }

    private byte[] BuildStub(Site site, nint siteAddress, nint stubAddress)
    {
        var code = new List<byte>
        {
            0x48, 0x85, 0xD2,                       // test rdx,rdx
            0x74, 0x18,                             // jz orig
            0x48, 0x8B, 0x42, site.SourceOffset,    // mov rax,[rdx+offset]   (shared part of the source)
            0x48, 0x85, 0xC0,                       // test rax,rax
            0x74, 0x0F,                             // jz orig
            0x48, 0x8B, 0x00,                       // mov rax,[rax]          (source base object)
            0x48, 0x3B, 0x05, 0, 0, 0, 0,           // cmp rax,[rip+data]
            0x75, 0x03,                             // jne orig
            0x31, 0xC0,                             // xor eax,eax            (sound not heard)
            0xC3,                                   // ret
        };
        const int cmpEnd = 24;
        var disp = BitConverter.GetBytes((int)(_block - (stubAddress + cmpEnd)));
        for (int i = 0; i < 4; i++)
            code[20 + i] = disp[i];

        code.AddRange(site.Original);                           // orig:
        code.AddRange([0xFF, 0x25, 0, 0, 0, 0]);                // jmp qword ptr [rip+0]
        code.AddRange(BitConverter.GetBytes((long)(siteAddress + site.Original.Length)));
        while (code.Count < StubSize)
            code.Add(0xCC);
        return [.. code];
    }

    private bool Fail(string reason)
    {
        Error = $"player sound hook: {reason}";
        return false;
    }
}
