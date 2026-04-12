namespace AxiomFusion.CncController.Core;

public record ToolpathMove(
    double X,
    double Y,
    double Z,
    double A,
    double Feed,
    bool   IsRapid,
    bool   LaserOn,
    int    LineIndex);

public class GCodeProgram
{
    public string              Filepath  { get; set; } = "";
    /// <summary>Linhas lidas do ficheiro (antes do pré-processamento por tipo de máquina).</summary>
    public List<string>?       SourceLines { get; set; }
    public List<string>        Lines     { get; set; } = [];
    public List<ToolpathMove>  Toolpath  { get; set; } = [];
    public AxisBounds          Bounds    { get; set; } = new();
    public List<string>        Warnings  { get; set; } = [];
    public int                 LineCount { get; set; }
}

public class AxisBounds
{
    public double XMin { get; set; }
    public double XMax { get; set; } = 100;
    public double YMin { get; set; }
    public double YMax { get; set; } = 100;
    public double ZMin { get; set; }
    public double ZMax { get; set; } = 50;
    public double AMin { get; set; }
    public double AMax { get; set; } = 360;
}
