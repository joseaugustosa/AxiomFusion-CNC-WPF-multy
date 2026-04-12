namespace AxiomFusion.CncController.Core;

/// <summary>Tipos de máquina suportados.</summary>
public enum MachineType
{
    Laser,      // Laser 2D (corte / gravação)
    Drill,      // Fresa / Furadeira CNC
    Turn,       // Torno CNC
    TurnLaser,  // Torno + Laser combinado
}

/// <summary>M-codes para controlo de spindle (fresa/torno).</summary>
public record SpindleCodes(
    string SpindleOnFwd  = "M3",
    string SpindleOnRev  = "M4",
    string SpindleOff    = "M5",
    string CoolantOn     = "M8",
    string CoolantOff    = "M9",
    int    MaxRpm        = 24000,
    string Home          = "$H",
    string Unlock        = "$X");

/// <summary>Perfis pré-definidos por tipo de máquina.</summary>
public static class MachineProfiles
{
    public static IReadOnlyDictionary<MachineType, SpindleCodes> SpindleDefaults =>
        new Dictionary<MachineType, SpindleCodes>
        {
            [MachineType.Laser]      = new("M3","M4","M5","M8","M9",1000,"$H","$X"),
            [MachineType.Drill]      = new("M3","M4","M5","M8","M9",24000,"$H","$X"),
            [MachineType.Turn]       = new("M3","M4","M5","M8","M9",3000,"$H","$X"),
            [MachineType.TurnLaser]  = new("M3","M4","M5","M8","M9",3000,"$H","$X"),
        };

    public static string DisplayName(MachineType t) => t switch
    {
        MachineType.Laser     => "Laser",
        MachineType.Drill     => "Fresa/Furadeira",
        MachineType.Turn      => "Torno",
        MachineType.TurnLaser => "Torno + Laser",
        _                     => t.ToString(),
    };
}
