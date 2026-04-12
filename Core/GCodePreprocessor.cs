using System.Text.RegularExpressions;

namespace AxiomFusion.CncController.Core;

public static class GCodePreprocessor
{
    // ── Variantes conhecidas de M-codes laser/spindle ─────────────────────
    private static readonly HashSet<string> LaserOnVariants  = ["M3","M03","M4","M04"];
    private static readonly HashSet<string> LaserOffVariants = ["M5","M05","M9","M09"];

    // ── Preprocessamento por tipo de máquina ─────────────────────────────

    /// <summary>Preprocessa linhas de G-code para o modo laser (comportamento original).</summary>
    public static List<string> Preprocess(List<string> lines, MCodes codes)
        => PreprocessLaser(lines, codes);

    /// <summary>Preprocessa G-code de acordo com o tipo de máquina.</summary>
    public static List<string> PreprocessForMachine(
        List<string> lines,
        MachineType  machine,
        MCodes       laserCodes,
        SpindleCodes spindleCodes)
    {
        return machine switch
        {
            MachineType.Laser     => PreprocessLaser(lines, laserCodes),
            MachineType.Drill     => PreprocessDrill(lines, spindleCodes),
            MachineType.Turn      => PreprocessTurn(lines, spindleCodes),
            MachineType.TurnLaser => PreprocessTurnLaser(lines, laserCodes, spindleCodes),
            _                     => lines,
        };
    }

    // ── Laser ─────────────────────────────────────────────────────────────

    private static List<string> PreprocessLaser(List<string> lines, MCodes codes)
    {
        string mOn   = codes.On.ToUpperInvariant().Trim();
        string mOff  = codes.Off.ToUpperInvariant().Trim();
        int    maxS  = codes.MaxS;
        string mode  = codes.Mode;
        double pierce= codes.Pierce;

        var result = new List<string>(lines.Count);
        foreach (var raw in lines)
        {
            var (codePart, comment) = SplitComment(raw);
            var line = codePart.Trim();
            if (string.IsNullOrEmpty(line)) { result.Add(raw); continue; }

            line = ReplaceLaserOn(line,  mOn,  maxS, mode, pierce);
            line = ReplaceLaserOff(line, mOff);
            result.Add(line + comment);
        }
        return result;
    }

    // ── Drill / Fresa ─────────────────────────────────────────────────────

    private static List<string> PreprocessDrill(List<string> lines, SpindleCodes codes)
    {
        var result = new List<string>(lines.Count);
        foreach (var raw in lines)
        {
            var (codePart, comment) = SplitComment(raw);
            var line = codePart.Trim();
            if (string.IsNullOrEmpty(line)) { result.Add(raw); continue; }

            line = ReplaceSpindle(line, codes);
            line = ReplaceCoolant(line, codes);
            result.Add(line + comment);
        }
        return result;
    }

    // ── Turn / Torno ──────────────────────────────────────────────────────

    private static List<string> PreprocessTurn(List<string> lines, SpindleCodes codes)
    {
        // Mesmo que Drill mas mantém M4 (reversão do spindle)
        return PreprocessDrill(lines, codes);
    }

    // ── TurnLaser — torno + laser combinado ───────────────────────────────
    // Convenção: M3/M4/M5 = spindle | M7/M107 = laser on | M9/M109 = laser off
    // O utilizador configura o M-code do laser para um valor diferente do spindle.

    private static readonly HashSet<string> TurnLaserOnVariants  = ["M7","M07"];
    private static readonly HashSet<string> TurnLaserOffVariants = ["M107","M09"];

    private static List<string> PreprocessTurnLaser(
        List<string> lines, MCodes laserCodes, SpindleCodes spindleCodes)
    {
        string lOn    = laserCodes.On.ToUpperInvariant().Trim();
        string lOff   = laserCodes.Off.ToUpperInvariant().Trim();
        int    maxS   = laserCodes.MaxS;
        string mode   = laserCodes.Mode;
        double pierce = laserCodes.Pierce;

        var result = new List<string>(lines.Count);
        foreach (var raw in lines)
        {
            var (codePart, comment) = SplitComment(raw);
            var line = codePart.Trim();
            if (string.IsNullOrEmpty(line)) { result.Add(raw); continue; }

            // Spindle (M3/M4/M5) — substituição por configurados
            line = ReplaceSpindle(line, spindleCodes);
            // Coolant (M8/M9)
            line = ReplaceCoolant(line, spindleCodes);
            // Laser on (M7 → laser M-code configurado)
            line = ReplaceTurnLaserOn(line,  lOn, maxS, mode, pierce);
            // Laser off (M107 → laser off configurado)
            line = ReplaceTurnLaserOff(line, lOff);
            result.Add(line + comment);
        }
        return result;
    }

    // ── Helpers de substituição ───────────────────────────────────────────

    private static string ReplaceLaserOn(string line, string mOn, int maxS, string mode, double pierce)
    {
        string upper = line.ToUpperInvariant();
        foreach (var variant in LaserOnVariants)
        {
            if (!Regex.IsMatch(upper, $@"\b{variant}\b")) continue;
            bool hasS = Regex.IsMatch(upper, @"\bS\d");
            line = Regex.Replace(line, $@"\b{variant}\b", mOn, RegexOptions.IgnoreCase);
            if (!hasS && mode == "PWM") line += $" S{maxS}";
            if (pierce > 0 && !upper.Contains("G4") && !upper.Contains("G04"))
                line += $"\nG4 P{pierce:F2}";
            break;
        }
        return line;
    }

    private static string ReplaceLaserOff(string line, string mOff)
    {
        string upper = line.ToUpperInvariant();
        foreach (var variant in LaserOffVariants)
        {
            if (!Regex.IsMatch(upper, $@"\b{variant}\b")) continue;
            line = Regex.Replace(line, $@"\b{variant}\b", mOff, RegexOptions.IgnoreCase);
            break;
        }
        return line;
    }

    private static string ReplaceSpindle(string line, SpindleCodes codes)
    {
        string upper = line.ToUpperInvariant();
        // M3 → SpindleOnFwd
        if (Regex.IsMatch(upper, @"\bM0?3\b"))
            line = Regex.Replace(line, @"\bM0?3\b", codes.SpindleOnFwd, RegexOptions.IgnoreCase);
        // M4 → SpindleOnRev
        else if (Regex.IsMatch(upper, @"\bM0?4\b"))
            line = Regex.Replace(line, @"\bM0?4\b", codes.SpindleOnRev, RegexOptions.IgnoreCase);
        // M5 → SpindleOff
        else if (Regex.IsMatch(upper, @"\bM0?5\b"))
            line = Regex.Replace(line, @"\bM0?5\b", codes.SpindleOff, RegexOptions.IgnoreCase);
        return line;
    }

    private static string ReplaceCoolant(string line, SpindleCodes codes)
    {
        string upper = line.ToUpperInvariant();
        if (Regex.IsMatch(upper, @"\bM0?8\b"))
            line = Regex.Replace(line, @"\bM0?8\b", codes.CoolantOn,  RegexOptions.IgnoreCase);
        else if (Regex.IsMatch(upper, @"\bM0?9\b"))
            line = Regex.Replace(line, @"\bM0?9\b", codes.CoolantOff, RegexOptions.IgnoreCase);
        return line;
    }

    private static string ReplaceTurnLaserOn(string line, string mOn, int maxS, string mode, double pierce)
    {
        string upper = line.ToUpperInvariant();
        foreach (var variant in TurnLaserOnVariants)
        {
            if (!Regex.IsMatch(upper, $@"\b{variant}\b")) continue;
            bool hasS = Regex.IsMatch(upper, @"\bS\d");
            line = Regex.Replace(line, $@"\b{variant}\b", mOn, RegexOptions.IgnoreCase);
            if (!hasS && mode == "PWM") line += $" S{maxS}";
            if (pierce > 0) line += $"\nG4 P{pierce:F2}";
            break;
        }
        return line;
    }

    private static string ReplaceTurnLaserOff(string line, string mOff)
    {
        string upper = line.ToUpperInvariant();
        foreach (var variant in TurnLaserOffVariants)
        {
            if (!Regex.IsMatch(upper, $@"\b{variant}\b")) continue;
            line = Regex.Replace(line, $@"\b{variant}\b", mOff, RegexOptions.IgnoreCase);
            break;
        }
        return line;
    }

    private static (string code, string comment) SplitComment(string raw)
    {
        int si = raw.IndexOf(';');
        return si >= 0
            ? (raw[..si], "; " + raw[(si + 1)..])
            : (raw, "");
    }

    // ── Validação ─────────────────────────────────────────────────────────

    public static List<string> Validate(List<string> lines)
    {
        var warnings  = new List<string>();
        bool hasEnd   = false;
        bool hasUnits = false;

        for (int i = 0; i < lines.Count; i++)
        {
            var raw  = lines[i];
            var line = (raw.Contains(';') ? raw[..raw.IndexOf(';')] : raw).Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(line)) continue;
            int lnum = i + 1;

            if (line.Contains("G21") || line.Contains("G20")) hasUnits = true;
            if (line.Contains("M2") || line.Contains("M02") || line.Contains("M30")) hasEnd = true;

            if (line.Contains("$J="))       warnings.Add($"Linha {lnum}: sintaxe GRBL '$J=' — não compatível com ISO/A20Z");
            if (line.StartsWith('$'))       warnings.Add($"Linha {lnum}: comando GRBL '${line[1..Math.Min(3,line.Length)]}' — não compatível com ISO");

            foreach (Match m in Regex.Matches(line, @"[XYZAB]([-\d.]+)"))
            {
                if (double.TryParse(m.Groups[1].Value, out var v) && Math.Abs(v) > 99999)
                    warnings.Add($"Linha {lnum}: coordenada muito grande: {v}");
            }
        }

        if (!hasUnits) warnings.Add("Aviso: sem G20/G21 (unidades) no programa");
        if (!hasEnd)   warnings.Add("Aviso: sem M2/M30 (fim de programa) no ficheiro");
        return warnings;
    }

    // ── Presets ───────────────────────────────────────────────────────────

    public static IReadOnlyDictionary<string, MCodes> Presets => _presets;

    private static readonly Dictionary<string, MCodes> _presets = new()
    {
        ["GRBL (padrão)"]             = new("M3","M5",1000,"PWM",0.5,"$H","$X"),
        ["A20Z / Flymotion (Mach3)"]  = new("M3","M5",1000,"PWM",0.5,"G28",""),
        ["Marlin (impressora 3D)"]    = new("M3","M5", 255,"PWM",0.2,"G28",""),
        ["Laser TTL simples"]          = new("M3","M5",1000,"TTL",0.3,"G28",""),
        ["Spindle CNC (fresa)"]        = new("M3","M5",24000,"PWM",2.0,"G28",""),
        ["Personalizado"]              = new("M3","M5",1000,"PWM",0.5,"G28",""),
    };
}

public record MCodes(
    string On     = "M3",
    string Off    = "M5",
    int    MaxS   = 1000,
    string Mode   = "PWM",
    double Pierce = 0.5,
    string Home   = "$H",
    string Unlock = "$X");
