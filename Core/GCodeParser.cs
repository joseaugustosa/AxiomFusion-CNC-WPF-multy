using System.Globalization;
using System.Text.RegularExpressions;

namespace AxiomFusion.CncController.Core;

public class GCodeParser
{
    private static readonly Regex TokenRx = new(@"([A-Z])([-+]?[0-9]*\.?[0-9]+)", RegexOptions.Compiled);
    private static readonly Regex McodeRx = new(@"\bM(-?\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public GCodeProgram LoadFile(string filepath)
    {
        var prog = new GCodeProgram { Filepath = filepath };
        prog.Lines       = File.ReadAllLines(filepath).ToList();
        prog.SourceLines = prog.Lines.ToList();
        prog.LineCount   = prog.Lines.Count;

        prog.Toolpath = BuildToolpath(prog.Lines, MachineType.Laser, 3, 5);
        prog.Bounds   = ComputeBounds(prog.Toolpath);
        return prog;
    }

    /// <summary>Tenta extrair o número de um M-code (ex.: "M7", "M107" → 7, 107).</summary>
    public static bool TryParseMCodeNumber(string? s, out int code)
    {
        code = 0;
        if (string.IsNullOrWhiteSpace(s)) return false;
        var m = Regex.Match(s.Trim(), @"^M(-?\d+)$", RegexOptions.IgnoreCase);
        if (!m.Success) return false;
        return int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out code);
    }

    /// <summary>
    /// Percurso para simulação: estado do feixe/tocha conforme o tipo de máquina.
    /// Laser/Plasma 2D: M configurados + convenção GRBL M3/M4 ligar, M5/M9 desligar.
    /// Torno + laser/plasma: só os M configurados (ex. M7/M107); M3–M5 são spindle, não tocha.
    /// </summary>
    public static List<ToolpathMove> BuildToolpath(
        List<string> lines,
        MachineType  machine,
        int          torchOnCode,
        int          torchOffCode)
    {
        var moves = new List<ToolpathMove>();
        double x = 0, y = 0, z = 0, a = 0, feed = 0;
        string modalG  = "G0";
        bool   absMode = true;
        bool   laserOn = false;

        for (int idx = 0; idx < lines.Count; idx++)
        {
            var raw  = lines[idx];
            var line = StripComment(raw).Trim();
            if (string.IsNullOrEmpty(line)) continue;

            line = line.ToUpperInvariant();
            ApplyTorchState(ref laserOn, line, machine, torchOnCode, torchOffCode);

            var words = new Dictionary<char, double>();
            foreach (Match m in TokenRx.Matches(line))
                words[m.Groups[1].Value[0]] = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);

            if (words.TryGetValue('G', out var gval))
            {
                int g = (int)gval;
                if (g == 90) absMode = true;
                else if (g == 91) absMode = false;
                else if (g is 0 or 1) modalG = $"G{g}";
            }

            if (words.TryGetValue('F', out var fval)) feed = fval;

            bool hasMove = words.ContainsKey('X') || words.ContainsKey('Y')
                        || words.ContainsKey('Z') || words.ContainsKey('A');
            if (!hasMove) continue;

            double nx = x, ny = y, nz = z, na = a;
            if (words.TryGetValue('X', out var xv)) nx = absMode ? xv : x + xv;
            if (words.TryGetValue('Y', out var yv)) ny = absMode ? yv : y + yv;
            if (words.TryGetValue('Z', out var zv)) nz = absMode ? zv : z + zv;
            if (words.TryGetValue('A', out var av)) na = absMode ? av : a + av;

            if (words.TryGetValue('G', out var gm)) modalG = (int)gm == 0 ? "G0" : "G1";

            moves.Add(new ToolpathMove(nx, ny, nz, na, feed,
                IsRapid: modalG == "G0", LaserOn: laserOn, LineIndex: idx));
            (x, y, z, a) = (nx, ny, nz, na);
        }

        return moves;
    }

    private static string StripComment(string raw)
    {
        if (!raw.Contains(';')) return raw;
        return raw[..raw.IndexOf(';')];
    }

    private static void ApplyTorchState(
        ref bool      laserOn,
        string        lineUpper,
        MachineType   machine,
        int           torchOnCode,
        int           torchOffCode)
    {
        foreach (Match m in McodeRx.Matches(lineUpper))
        {
            if (!int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var code))
                continue;

            if (machine is MachineType.TurnLaser or MachineType.TurnPlasma)
            {
                if (code == torchOffCode) laserOn = false;
                else if (code == torchOnCode) laserOn = true;
            }
            else
            {
                if (code == torchOnCode || code is 3 or 4) laserOn = true;
                if (code == torchOffCode || code is 5 or 9) laserOn = false;
            }
        }
    }

    public static AxisBounds ComputeBounds(List<ToolpathMove> toolpath)
    {
        if (toolpath.Count == 0)
            return new AxisBounds { XMax = 100, YMax = 100, ZMax = 50, AMax = 360 };

        var b = new AxisBounds
        {
            XMin = toolpath[0].X, XMax = toolpath[0].X,
            YMin = toolpath[0].Y, YMax = toolpath[0].Y,
            ZMin = toolpath[0].Z, ZMax = toolpath[0].Z,
            AMin = toolpath[0].A, AMax = toolpath[0].A,
        };
        foreach (var m in toolpath)
        {
            b.XMin = Math.Min(b.XMin, m.X); b.XMax = Math.Max(b.XMax, m.X);
            b.YMin = Math.Min(b.YMin, m.Y); b.YMax = Math.Max(b.YMax, m.Y);
            b.ZMin = Math.Min(b.ZMin, m.Z); b.ZMax = Math.Max(b.ZMax, m.Z);
            b.AMin = Math.Min(b.AMin, m.A); b.AMax = Math.Max(b.AMax, m.A);
        }
        return b;
    }
}
