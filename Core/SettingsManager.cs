using System.Text.Json;
using System.Text.Json.Nodes;

namespace AxiomFusion.CncController.Core;

public class SettingsManager
{
    private readonly string _path;
    private JsonObject _data;
    private readonly object _lock = new();

    private static readonly JsonObject Defaults = new()
    {
        // Tipo de máquina
        ["machine_type"]      = "Laser",

        // Controlador
        ["controller_type"]   = "GRBL",
        ["m_on"]              = "M3",
        ["m_off"]             = "M5",
        ["m_home"]            = "$H",
        ["m_unlock"]          = "$X",
        ["m_pierce"]          = 0.5,

        // Ligação
        ["port"]              = "AUTO",
        ["baud"]              = 115200,

        // Laser
        ["laser_mode"]        = "PWM",
        ["laser_max_s"]       = 1000,
        ["laser_default_power"] = 50,
        ["laser_test_fire_ms"]  = 500,

        // Spindle (Drill / Turn)
        ["spindle_on_fwd"]    = "M3",
        ["spindle_on_rev"]    = "M4",
        ["spindle_off"]       = "M5",
        ["spindle_max_rpm"]   = 24000,
        ["spindle_default_rpm"] = 1000,

        // Coolant
        ["coolant_on"]        = "M8",
        ["coolant_off"]       = "M9",

        // Torno
        ["turn_diameter_mode"] = false,
        ["turn_css_mode"]      = false,
        ["turn_css_speed"]     = 100.0,   // m/min

        // TurnLaser — M-codes do laser (diferentes do spindle)
        ["turn_laser_on"]     = "M7",
        ["turn_laser_off"]    = "M107",

        // Plasma — PWM = S define intensidade | ONOFF = só M ligar/desligar (sem S)
        ["plasma_mode"]       = "PWM",
        ["plasma_max_s"]      = 1000,

        // TurnPlasma — tocha (como TurnLaser; CAM costuma usar M7/M107)
        ["turn_plasma_on"]    = "M7",
        ["turn_plasma_off"]   = "M107",

        // Jog
        ["jog_feed"]          = 500.0,
        ["jog_step"]          = 1.0,

        // Visualizador
        ["tube_W"]            = 50.0,
        ["tube_H"]            = 50.0,
        ["standoff"]          = 3.0,
        // Logo 3D: graus/segundo (0 = parado)
        ["logo_spin_deg_per_sec"] = 36.0,
        // Marca de água 2D no viewport (0–1)
        ["viewport_watermark_opacity"] = 0.09,
        // Cor de fundo do viewport 3D (HEX ARGB/RGB)
        ["viewport_background"] = "#11111b",
        // Simulação: percentagem da velocidade (feed F e G0)
        ["simulation_speed_percent"] = 100.0,

        // Janela
        ["window_width"]      = 1400,
        ["window_height"]     = 900,
        ["mdi_history"]       = new JsonArray(),
    };

    public SettingsManager(string? path = null)
    {
        _path = path ?? Path.Combine(
            AppContext.BaseDirectory, "config", "settings.json");
        _data = [];
        Load();
    }

    public void Load()
    {
        lock (_lock)
        {
            _data = [];
            foreach (var kv in Defaults) _data[kv.Key] = kv.Value?.DeepClone();

            if (!File.Exists(_path)) return;
            try
            {
                var json = File.ReadAllText(_path);
                var node = JsonNode.Parse(json) as JsonObject;
                if (node != null)
                    foreach (var kv in node)
                        _data[kv.Key] = kv.Value?.DeepClone();
            }
            catch { }
        }
    }

    public void Save()
    {
        lock (_lock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, _data.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, _path, overwrite: true);
        }
    }

    public T Get<T>(string key, T defaultValue = default!)
    {
        lock (_lock)
        {
            if (!_data.TryGetPropertyValue(key, out var node) || node is null)
                return defaultValue;
            try { return node.GetValue<T>(); }
            catch { return defaultValue; }
        }
    }

    public string GetString(string key, string def = "") => Get(key, def);
    public int    GetInt   (string key, int    def = 0) => Get(key, def);
    public double GetDouble(string key, double def = 0) => Get(key, def);
    public bool   GetBool  (string key, bool   def = false) => Get(key, def);

    public List<string> GetStringList(string key)
    {
        lock (_lock)
        {
            if (!_data.TryGetPropertyValue(key, out var node) || node is not JsonArray arr)
                return [];
            return arr.Select(n => n?.GetValue<string>() ?? "").ToList();
        }
    }

    public void Set(string key, object? value)
    {
        lock (_lock)
        {
            _data[key] = value switch
            {
                null             => null,
                string s         => JsonValue.Create(s),
                int    i         => JsonValue.Create(i),
                double d         => JsonValue.Create(d),
                bool   b         => JsonValue.Create(b),
                float  f         => JsonValue.Create((double)f),
                IEnumerable<string> list => new JsonArray(list.Select(s => JsonValue.Create(s) as JsonNode).ToArray()),
                _                => JsonValue.Create(value.ToString()),
            };
        }
        Save();
    }

    // ── Factories ─────────────────────────────────────────────────────────

    public MCodes BuildMCodes() => new(
        On:     GetString("m_on",     "M3"),
        Off:    GetString("m_off",    "M5"),
        MaxS:   GetInt   ("laser_max_s", 1000),
        Mode:   GetString("laser_mode",  "PWM"),
        Pierce: GetDouble("m_pierce",    0.5),
        Home:   GetString("m_home",      "$H"),
        Unlock: GetString("m_unlock",    "$X"));

    /// <summary>MCodes para plasma (modo próprio: intensidade S vs só M).</summary>
    public MCodes BuildPlasmaMCodes()
    {
        int maxS = GetInt("plasma_max_s", 0);
        if (maxS <= 0) maxS = GetInt("laser_max_s", 1000);
        return new MCodes(
            On:     GetString("m_on",  "M3"),
            Off:    GetString("m_off", "M5"),
            MaxS:   maxS,
            Mode:   GetString("plasma_mode", "PWM"),
            Pierce: GetDouble("m_pierce", 0.5),
            Home:   GetString("m_home",   "$H"),
            Unlock: GetString("m_unlock", "$X"));
    }

    public SpindleCodes BuildSpindleCodes() => new(
        SpindleOnFwd: GetString("spindle_on_fwd", "M3"),
        SpindleOnRev: GetString("spindle_on_rev", "M4"),
        SpindleOff:   GetString("spindle_off",    "M5"),
        CoolantOn:    GetString("coolant_on",     "M8"),
        CoolantOff:   GetString("coolant_off",    "M9"),
        MaxRpm:       GetInt   ("spindle_max_rpm", 24000),
        Home:         GetString("m_home",          "$H"),
        Unlock:       GetString("m_unlock",        "$X"));

    public MachineType GetMachineType()
    {
        var s = GetString("machine_type", "Laser");
        return Enum.TryParse<MachineType>(s, true, out var mt) ? mt : MachineType.Laser;
    }

    public void SetMachineType(MachineType mt) => Set("machine_type", mt.ToString());
}
