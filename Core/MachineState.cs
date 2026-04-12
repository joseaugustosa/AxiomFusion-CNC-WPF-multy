using System.Text.RegularExpressions;

namespace AxiomFusion.CncController.Core;

/// <summary>Estado partilhado da máquina — thread-safe.</summary>
public class MachineState
{
    private readonly object _lock = new();

    // ── Posições ──────────────────────────────────────────────────────────
    public AxisPos WorkPos    { get; private set; } = new();
    public AxisPos MachinePos { get; private set; } = new();
    public AxisPos Wco        { get; private set; } = new();

    // ── Estado modal ──────────────────────────────────────────────────────
    public string Status     { get; private set; } = "Idle";
    public string ActiveWcs  { get; private set; } = "G54";
    public int    FeedRate   { get; private set; }
    public int    LaserPower { get; private set; }          // valor S

    // ── Overrides ─────────────────────────────────────────────────────────
    public int FeedOverride   { get; private set; } = 100;
    public int RapidOverride  { get; private set; } = 100;
    public int SpindleOverride{ get; private set; } = 100;

    // ── Hardware ──────────────────────────────────────────────────────────
    public bool LimitX    { get; private set; }
    public bool LimitY    { get; private set; }
    public bool LimitZ    { get; private set; }
    public bool ProbePin  { get; private set; }
    public int  AlarmCode { get; private set; }
    public int  ErrorCode { get; private set; }
    public int  BufAvail  { get; private set; } = 128;

    // ── Ligação ───────────────────────────────────────────────────────────
    public bool Connected { get; private set; }

    // ─────────────────────────────────────────────────────────────────────

    public void SetConnected(bool v)   { lock (_lock) Connected = v; }
    public void SetAlarm(int code)     { lock (_lock) AlarmCode = code; }
    public void SetError(int code)     { lock (_lock) ErrorCode = code; }
    public void SetStatus(string s)    { lock (_lock) Status = s; }

    /// <summary>Actualiza estado a partir de uma linha de relatório GRBL.</summary>
    public void UpdateFromStatus(string report)
    {
        lock (_lock)
        {
            // Estado: <Idle|...>
            var stateMatch = Regex.Match(report, @"<(\w+)");
            if (stateMatch.Success) Status = stateMatch.Groups[1].Value;

            // MPos
            var mpos = ParseXyza(report, "MPos");
            if (mpos != null) MachinePos = mpos;

            // WPos
            var wpos = ParseXyza(report, "WPos");
            if (wpos != null) WorkPos = wpos;

            // WCO
            var wco = ParseXyza(report, "WCO");
            if (wco != null)
            {
                Wco = wco;
                // Calcular WPos a partir de MPos - WCO se WPos não estiver presente
                if (wpos == null && mpos != null)
                {
                    WorkPos = new AxisPos(
                        MachinePos.X - Wco.X,
                        MachinePos.Y - Wco.Y,
                        MachinePos.Z - Wco.Z,
                        MachinePos.A - Wco.A);
                }
            }

            // FS: feed, spindle S
            var fs = Regex.Match(report, @"FS:(-?\d+),(-?\d+)");
            if (fs.Success)
            {
                FeedRate   = int.Parse(fs.Groups[1].Value);
                LaserPower = int.Parse(fs.Groups[2].Value);
            }

            // Overrides Ov:feed,rapid,spindle
            var ov = Regex.Match(report, @"Ov:(\d+),(\d+),(\d+)");
            if (ov.Success)
            {
                FeedOverride    = int.Parse(ov.Groups[1].Value);
                RapidOverride   = int.Parse(ov.Groups[2].Value);
                SpindleOverride = int.Parse(ov.Groups[3].Value);
            }

            // Pinos Pn:XYZProbe
            var pn = Regex.Match(report, @"Pn:([A-Za-z]*)");
            if (pn.Success)
            {
                var pins = pn.Groups[1].Value.ToUpperInvariant();
                LimitX   = pins.Contains('X');
                LimitY   = pins.Contains('Y');
                LimitZ   = pins.Contains('Z');
                ProbePin = pins.Contains('P');
            }

            // Buffer Bf:plan,rx
            var bf = Regex.Match(report, @"Bf:(\d+),(\d+)");
            if (bf.Success) BufAvail = int.Parse(bf.Groups[2].Value);
        }
    }

    /// <summary>Devolve snapshot imutável para a GUI (sem necessidade de lock externo).</summary>
    public MachineSnapshot GetSnapshot()
    {
        lock (_lock)
        {
            return new MachineSnapshot
            {
                Status         = Status,
                WorkPos        = new AxisPos(WorkPos.X, WorkPos.Y, WorkPos.Z, WorkPos.A),
                MachinePos     = new AxisPos(MachinePos.X, MachinePos.Y, MachinePos.Z, MachinePos.A),
                FeedRate       = FeedRate,
                LaserPower     = LaserPower,
                FeedOverride   = FeedOverride,
                RapidOverride  = RapidOverride,
                SpindleOverride= SpindleOverride,
                LimitX         = LimitX,
                LimitY         = LimitY,
                LimitZ         = LimitZ,
                ProbePin       = ProbePin,
                AlarmCode      = AlarmCode,
                ErrorCode      = ErrorCode,
                BufAvail       = BufAvail,
                Connected      = Connected,
                ActiveWcs      = ActiveWcs,
            };
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static AxisPos? ParseXyza(string s, string tag)
    {
        var m = Regex.Match(s, $@"{tag}:([+-]?\d+\.?\d*),([+-]?\d+\.?\d*),([+-]?\d+\.?\d*),?([+-]?\d+\.?\d*)?");
        if (!m.Success) return null;
        return new AxisPos(
            double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
            double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture),
            double.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture),
            m.Groups[4].Success ? double.Parse(m.Groups[4].Value, System.Globalization.CultureInfo.InvariantCulture) : 0.0);
    }
}

// ── Value types ───────────────────────────────────────────────────────────────

public record AxisPos(double X = 0, double Y = 0, double Z = 0, double A = 0);

public class MachineSnapshot
{
    public string   Status          { get; init; } = "Idle";
    public AxisPos  WorkPos         { get; init; } = new();
    public AxisPos  MachinePos      { get; init; } = new();
    public int      FeedRate        { get; init; }
    public int      LaserPower      { get; init; }
    public int      FeedOverride    { get; init; } = 100;
    public int      RapidOverride   { get; init; } = 100;
    public int      SpindleOverride { get; init; } = 100;
    public bool     LimitX          { get; init; }
    public bool     LimitY          { get; init; }
    public bool     LimitZ          { get; init; }
    public bool     ProbePin        { get; init; }
    public int      AlarmCode       { get; init; }
    public int      ErrorCode       { get; init; }
    public int      BufAvail        { get; init; } = 128;
    public bool     Connected       { get; init; }
    public string   ActiveWcs       { get; init; } = "G54";
}
