using System.Globalization;
using System.Text;
using MetroExodusTrainer.Core;

/// <summary>
/// Keeps the last minute of movement-related player values in memory and writes them to a CSV on demand,
/// so a short-lived effect (like a slowdown) can be inspected after it happened.
/// </summary>
sealed class TraceRecorder
{
    private const int SampleMs = 50;
    private const int Capacity = 1200; // 60 s

    private static readonly string[] Columns =
    [
        "t_ms", "health", "stamina", "can_use", "freeze", "stam_state",
        "move_speed", "state_real", "state_target", "evade", "anim_coef", "speed_scale",
        "cur_walk", "cur_run", "cur_sprint", "cur_crouch",
        "tgt_walk", "tgt_run", "tgt_sprint", "tgt_crouch",
        "allow_walk", "allow_run", "allow_sprint", "allow_crouch", "allow_jump",
    ];

    private readonly string[] _rows = new string[Capacity];
    private int _next, _count;
    private long _lastSample;

    public void Sample(GameProcess game, nint player)
    {
        long now = Environment.TickCount64;
        if (player == 0 || now - _lastSample < SampleMs)
            return;
        _lastSample = now;

        var sb = new StringBuilder();
        sb.Append(now.ToString(CultureInfo.InvariantCulture));
        void F(nint address) =>
            sb.Append(',').Append(game.TryRead<float>(address, out var v) ? v.ToString("0.###", CultureInfo.InvariantCulture) : "");
        void B(nint address) =>
            sb.Append(',').Append(game.TryRead<byte>(address, out var v) ? v.ToString(CultureInfo.InvariantCulture) : "");
        void U(nint address) =>
            sb.Append(',').Append(game.TryRead<uint>(address, out var v) ? "0x" + v.ToString("X") : "");

        F(player + Offsets.Player.Health);
        F(player + Offsets.Player.Stamina);
        B(player + Offsets.Player.CanUseStamina);
        B(player + Offsets.Player.StaminaFreeze);
        B(player + Offsets.Player.StaminaState);
        F(player + Offsets.Player.MoveSpeed);
        U(player + Offsets.Player.StateReal);
        U(player + Offsets.Player.StateTarget);
        B(player + Offsets.Player.EvadeState);
        F(player + Offsets.Player.AnimSpeedCoef);
        F(game.Base + Offsets.PlayerSpeedScale);
        for (int i = 0; i < 4; i++) F(player + Offsets.Player.VsSpeed + i * 4);
        for (int i = 0; i < 4; i++) F(player + Offsets.Player.VsSpeedTarget + i * 4);
        for (int i = 0; i < 5; i++) B(player + Offsets.Player.VsMoveAllowed + i);

        _rows[_next] = sb.ToString();
        _next = (_next + 1) % Capacity;
        _count = Math.Min(_count + 1, Capacity);
    }

    /// <summary>Writes the buffered samples, oldest first, and returns the file path.</summary>
    public string Dump()
    {
        Directory.CreateDirectory("logs");
        string path = Path.Combine("logs", $"trace-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
        using var w = new StreamWriter(path);
        w.WriteLine(string.Join(',', Columns));
        for (int i = 0; i < _count; i++)
            w.WriteLine(_rows[(_next - _count + i + Capacity) % Capacity]);
        return Path.GetFullPath(path);
    }
}
