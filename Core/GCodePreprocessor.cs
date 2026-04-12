using System.Text.RegularExpressions;

namespace AxiomFusion.CncController.Core;

public static class GCodePreprocessor
{
    private static readonly HashSet<string> LaserOnVariants  = ["M3","M03","M4","M04"];
    private static readonly HashSet<string> LaserOffVariants = ["M5","M05","M9","M09"];

    public static List<string> Preprocess(List<string> lines, MCodes codes)
    {
        string mOn   = codes.On.ToUpperInvariant().Trim();
        string mOff  = codes.Off.ToUpperInvariant().Trim();
        int    maxS  = codes.MaxS;
        string mode  = codes.Mode;
        double pierce= codes.Pierce;

        var result = new List<string>(lines.Count);
        foreach (var raw in lines)
        {
            string codePart, comment;
            int si = raw.IndexOf(';');
            if (si >= 0) { codePart = raw[..si]; comment = "; " + raw[(si+1)..]; }
            else         { codePart = raw;        comment = "";                    }

            var line = codePart.Trim();
            if (string.IsNullOrEmpty(line)) { result.Add(raw); continue; }

            line = ReplaceMOn(line,  mOn,  maxS, mode, pierce);
            line = ReplaceMOff(line, mOff);
            result.Add(line + comment);
        }
        return result;
    }

    private static string ReplaceMOn(string line, string mOn, int maxS, string mode, double pierce)
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

    private static string ReplaceMOff(string line, string mOff)
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
