namespace AxiomFusion.CncController.Core;

/// <summary>Tipos de máquina suportados.</summary>
public enum MachineType
{
    Laser,      // Laser 2D (corte / gravação)
    Plasma,     // Corte a plasma (tocha M3/M5 + pierce — mesmo pipeline que laser GRBL)
    Drill,      // Fresa / Furadeira CNC
    Turn,       // Torno CNC
    TurnLaser,  // Torno + Laser combinado
    TurnPlasma, // Torno + Plasma (tocha M7/M107 ou configurável — spindle separado)
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
            [MachineType.Plasma]     = new("M3","M4","M5","M8","M9",1000,"$H","$X"),
            [MachineType.Drill]      = new("M3","M4","M5","M8","M9",24000,"$H","$X"),
            [MachineType.Turn]       = new("M3","M4","M5","M8","M9",3000,"$H","$X"),
            [MachineType.TurnLaser]  = new("M3","M4","M5","M8","M9",3000,"$H","$X"),
            [MachineType.TurnPlasma] = new("M3","M4","M5","M8","M9",3000,"$H","$X"),
        };

    public static string DisplayName(MachineType t) => t switch
    {
        MachineType.Laser     => "Laser",
        MachineType.Plasma    => "Plasma",
        MachineType.Drill     => "Fresa/Furadeira",
        MachineType.Turn      => "Torno",
        MachineType.TurnLaser => "Torno + Laser",
        MachineType.TurnPlasma => "Torno + Plasma",
        _                     => t.ToString(),
    };
}
