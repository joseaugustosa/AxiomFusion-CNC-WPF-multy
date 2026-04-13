using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AxiomFusion.CncController.Core;

namespace AxiomFusion.CncController.Dialogs;

public partial class SettingsDialog : Window
{
    private readonly SettingsManager _s;
    private readonly IReadOnlyDictionary<string, MCodes> _presets
        = GCodePreprocessor.Presets;

    public SettingsDialog(SettingsManager settings)
    {
        _s = settings;
        InitializeComponent();
        LoadFromSettings();
        LoadPresets();
        UpdatePreview();
    }

    // ── Carregar / Guardar ────────────────────────────────────────────────

    private void LoadFromSettings()
    {
        // ── Tab: Tipo de Máquina ──────────────────────────────────────────
        var mt = _s.GetMachineType();
        RbLaser.IsChecked     = mt == MachineType.Laser;
        RbPlasma.IsChecked    = mt == MachineType.Plasma;
        RbDrill.IsChecked     = mt == MachineType.Drill;
        RbTurn.IsChecked      = mt == MachineType.Turn;
        RbTurnLaser.IsChecked = mt == MachineType.TurnLaser;
        RbTurnPlasma.IsChecked = mt == MachineType.TurnPlasma;

        TbSpindleOnFwd.Text  = _s.GetString("spindle_on_fwd",  "M3");
        TbSpindleOnRev.Text  = _s.GetString("spindle_on_rev",  "M4");
        TbSpindleOff.Text    = _s.GetString("spindle_off",     "M5");
        TbSpindleMaxRpm.Text = _s.GetInt   ("spindle_max_rpm", 24000).ToString();
        TbCoolantOn.Text     = _s.GetString("coolant_on",      "M8");
        TbCoolantOff.Text    = _s.GetString("coolant_off",     "M9");
        TbTurnLaserOn.Text   = _s.GetString("turn_laser_on",   "M7");
        TbTurnLaserOff.Text  = _s.GetString("turn_laser_off",  "M107");
        TbTurnPlasmaOn.Text  = _s.GetString("turn_plasma_on",  "M7");
        TbTurnPlasmaOff.Text = _s.GetString("turn_plasma_off", "M107");

        // ── Tab: Controlador ──────────────────────────────────────────────
        var controllerType = _s.GetString("controller_type", "GRBL").Trim();
        RbGrbl.IsChecked  = string.Equals(controllerType, "GRBL", StringComparison.OrdinalIgnoreCase);
        RbMach3.IsChecked = string.Equals(controllerType, "MACH3", StringComparison.OrdinalIgnoreCase);
        RbIso.IsChecked   = RbGrbl.IsChecked != true && RbMach3.IsChecked != true;
        TbMOn.Text        = _s.GetString("m_on",         "M3");
        TbMOff.Text       = _s.GetString("m_off",        "M5");
        TbHome.Text       = _s.GetString("m_home",       "$H");
        TbUnlock.Text     = _s.GetString("m_unlock",     "$X");
        TbPierce.Text     = _s.GetDouble("m_pierce",     0.5).ToString("F2", CultureInfo.InvariantCulture);

        // ── Tab: Laser / Plasma ───────────────────────────────────────────
        TbMaxS.Text       = _s.GetInt("laser_max_s",     1000).ToString();
        TbPlasmaMaxS.Text = _s.GetInt("plasma_max_s",    1000).ToString();
        RbPwm.IsChecked   = _s.GetString("laser_mode",   "PWM") == "PWM";
        RbTtl.IsChecked   = !RbPwm.IsChecked;
        var pMode = _s.GetString("plasma_mode", "PWM");
        RbPlasmaPwm.IsChecked   = pMode != "ONOFF";
        RbPlasmaOnOff.IsChecked = pMode == "ONOFF";

        // ── Tab: Ligação ──────────────────────────────────────────────────
        TbPort.Text       = _s.GetString("port",         "AUTO");
        TbBaud.Text       = _s.GetInt("baud",            115200).ToString();

        // ── Tab: Tubo / Máquina ───────────────────────────────────────────
        TbW.Text          = _s.GetDouble("tube_W",       50.0).ToString("F1", CultureInfo.InvariantCulture);
        TbH.Text          = _s.GetDouble("tube_H",       50.0).ToString("F1", CultureInfo.InvariantCulture);
        TbStandoff.Text   = _s.GetDouble("standoff",     3.0).ToString("F1", CultureInfo.InvariantCulture);
        TbJogFeed.Text    = _s.GetDouble("jog_feed",     500.0).ToString("F0", CultureInfo.InvariantCulture);
        SldWatermarkOpacity.Value = Math.Clamp(
            _s.GetDouble("viewport_watermark_opacity", 0.09) * 100.0, 0, 100);
        SldSimulationSpeed.Value = Math.Clamp(_s.GetDouble("simulation_speed_percent", 100.0), 10, 400);
        TbViewportBackground.Text = _s.GetString("viewport_background", "#11111b");
    }

    public void SaveToSettings(SettingsManager s)
    {
        // ── Tipo de Máquina ───────────────────────────────────────────────
        var mt = RbTurnPlasma.IsChecked == true ? MachineType.TurnPlasma
               : RbPlasma.IsChecked == true ? MachineType.Plasma
               : RbDrill.IsChecked == true ? MachineType.Drill
               : RbTurn.IsChecked  == true ? MachineType.Turn
               : RbTurnLaser.IsChecked == true ? MachineType.TurnLaser
               : MachineType.Laser;
        s.SetMachineType(mt);

        s.Set("spindle_on_fwd",  TbSpindleOnFwd.Text.Trim());
        s.Set("spindle_on_rev",  TbSpindleOnRev.Text.Trim());
        s.Set("spindle_off",     TbSpindleOff.Text.Trim());
        if (int.TryParse(TbSpindleMaxRpm.Text, out var maxRpm)) s.Set("spindle_max_rpm", maxRpm);
        s.Set("coolant_on",      TbCoolantOn.Text.Trim());
        s.Set("coolant_off",     TbCoolantOff.Text.Trim());
        s.Set("turn_laser_on",   TbTurnLaserOn.Text.Trim());
        s.Set("turn_laser_off",  TbTurnLaserOff.Text.Trim());
        s.Set("turn_plasma_on",  TbTurnPlasmaOn.Text.Trim());
        s.Set("turn_plasma_off", TbTurnPlasmaOff.Text.Trim());

        // ── Controlador ───────────────────────────────────────────────────
        var controllerType = RbGrbl.IsChecked == true ? "GRBL"
            : RbMach3.IsChecked == true ? "MACH3"
            : "ISO";
        s.Set("controller_type", controllerType);
        s.Set("m_on",    TbMOn.Text.Trim().Length > 0 ? TbMOn.Text.Trim() : "M3");
        s.Set("m_off",   TbMOff.Text.Trim().Length > 0 ? TbMOff.Text.Trim() : "M5");
        s.Set("m_home",  TbHome.Text.Trim());
        s.Set("m_unlock",TbUnlock.Text.Trim());
        if (double.TryParse(TbPierce.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var pierce))
            s.Set("m_pierce", pierce);

        // ── Laser / Plasma ────────────────────────────────────────────────
        if (int.TryParse(TbMaxS.Text, out var maxS)) s.Set("laser_max_s", maxS);
        if (int.TryParse(TbPlasmaMaxS.Text, out var pMaxS)) s.Set("plasma_max_s", pMaxS);
        s.Set("laser_mode", RbPwm.IsChecked == true ? "PWM" : "TTL");
        s.Set("plasma_mode", RbPlasmaOnOff.IsChecked == true ? "ONOFF" : "PWM");

        // ── Ligação ───────────────────────────────────────────────────────
        var port = TbPort.Text.Trim();
        s.Set("port", string.IsNullOrWhiteSpace(port) ? "AUTO" : port);
        if (int.TryParse(TbBaud.Text, out var baud)) s.Set("baud", baud);

        // ── Tubo / Máquina ────────────────────────────────────────────────
        if (double.TryParse(TbW.Text,        NumberStyles.Any, CultureInfo.InvariantCulture, out var w))  s.Set("tube_W",   w);
        if (double.TryParse(TbH.Text,        NumberStyles.Any, CultureInfo.InvariantCulture, out var h))  s.Set("tube_H",   h);
        if (double.TryParse(TbStandoff.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var st)) s.Set("standoff", st);
        if (double.TryParse(TbJogFeed.Text,  NumberStyles.Any, CultureInfo.InvariantCulture, out var jf)) s.Set("jog_feed", jf);
        s.Set("viewport_watermark_opacity", SldWatermarkOpacity.Value / 100.0);
        s.Set("simulation_speed_percent", SldSimulationSpeed.Value);
        s.Set("viewport_background", NormalizeColorHex(TbViewportBackground.Text, "#11111b"));
    }

    // ── Presets ───────────────────────────────────────────────────────────

    private void LoadPresets()
    {
        foreach (var key in _presets.Keys) CmbPreset.Items.Add(key);
        CmbPreset.SelectedIndex = 0;
    }

    private void CmbPreset_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (CmbPreset.SelectedItem is not string name) return;
        if (!_presets.TryGetValue(name, out var p)) return;

        TbMOn.Text     = p.On;
        TbMOff.Text    = p.Off;
        TbHome.Text    = p.Home;
        TbUnlock.Text  = p.Unlock;
        TbPierce.Text  = p.Pierce.ToString("F2", CultureInfo.InvariantCulture);
        TbMaxS.Text    = p.MaxS.ToString();
        RbPwm.IsChecked = p.Mode == "PWM";
        RbTtl.IsChecked = p.Mode != "PWM";
        UpdatePreview();
    }

    private void CmbBaud_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (CmbBaud.SelectedItem is ComboBoxItem item)
            TbBaud.Text = item.Content?.ToString() ?? "";
    }

    private void CmbViewportBackgroundPreset_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (CmbViewportBackgroundPreset.SelectedItem is not ComboBoxItem item)
            return;

        var hex = item.Tag?.ToString();
        if (string.IsNullOrWhiteSpace(hex))
            return;

        TbViewportBackground.Text = hex;
    }

    // ── Preview ───────────────────────────────────────────────────────────

    private void Preview_Changed(object sender, TextChangedEventArgs e) => UpdatePreview();

    private void UpdatePreview()
    {
        string mOn   = TbMOn?.Text ?? "M3";
        string mOff  = TbMOff?.Text ?? "M5";
        int    maxS  = int.TryParse(TbMaxS?.Text, out var s) ? s : 1000;
        double pierce= double.TryParse(TbPierce?.Text, NumberStyles.Any,
            CultureInfo.InvariantCulture, out var p) ? p : 0.5;

        var lines = new List<string>
        {
            "; Início do corte:",
            $"{mOn} S{maxS}",
        };
        if (pierce > 0) lines.Add($"G4 P{pierce:F2}  ; pierce delay");
        lines.Add("G1 X50.000 A90.000 Z28.000 F2000");
        lines.Add($"{mOff}  ; fim do corte");

        if (TbPreview != null)
            TbPreview.Text = string.Join(Environment.NewLine, lines);
    }

    // ── OK ────────────────────────────────────────────────────────────────

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        TbViewportBackground.Text = NormalizeColorHex(TbViewportBackground.Text, "#11111b");
        DialogResult = true;
        Close();
    }

    private static string NormalizeColorHex(string? input, string fallback)
    {
        var text = input?.Trim();
        if (string.IsNullOrWhiteSpace(text))
            return fallback;

        try
        {
            var color = (Color)ColorConverter.ConvertFromString(text);
            return $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
        }
        catch
        {
            return fallback;
        }
    }
}
