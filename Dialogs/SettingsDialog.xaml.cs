using System.Globalization;
using System.Windows;
using System.Windows.Controls;
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
        RbGrbl.IsChecked  = _s.GetString("controller_type", "GRBL") == "GRBL";
        RbIso.IsChecked   = !RbGrbl.IsChecked;
        TbMOn.Text        = _s.GetString("m_on",         "M3");
        TbMOff.Text       = _s.GetString("m_off",        "M5");
        TbHome.Text       = _s.GetString("m_home",       "$H");
        TbUnlock.Text     = _s.GetString("m_unlock",     "$X");
        TbPierce.Text     = _s.GetDouble("m_pierce",     0.5).ToString("F2", CultureInfo.InvariantCulture);
        TbMaxS.Text       = _s.GetInt("laser_max_s",     1000).ToString();
        RbPwm.IsChecked   = _s.GetString("laser_mode",   "PWM") == "PWM";
        RbTtl.IsChecked   = !RbPwm.IsChecked;
        TbPort.Text       = _s.GetString("port",         "COM3");
        TbBaud.Text       = _s.GetInt("baud",            115200).ToString();
        TbW.Text          = _s.GetDouble("tube_W",       50.0).ToString("F1", CultureInfo.InvariantCulture);
        TbH.Text          = _s.GetDouble("tube_H",       50.0).ToString("F1", CultureInfo.InvariantCulture);
        TbStandoff.Text   = _s.GetDouble("standoff",     3.0).ToString("F1", CultureInfo.InvariantCulture);
        TbJogFeed.Text    = _s.GetDouble("jog_feed",     500.0).ToString("F0", CultureInfo.InvariantCulture);
    }

    public void SaveToSettings(SettingsManager s)
    {
        s.Set("controller_type", RbGrbl.IsChecked == true ? "GRBL" : "ISO");
        s.Set("m_on",    TbMOn.Text.Trim().Length > 0 ? TbMOn.Text.Trim() : "M3");
        s.Set("m_off",   TbMOff.Text.Trim().Length > 0 ? TbMOff.Text.Trim() : "M5");
        s.Set("m_home",  TbHome.Text.Trim());
        s.Set("m_unlock",TbUnlock.Text.Trim());
        if (double.TryParse(TbPierce.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var pierce))
            s.Set("m_pierce", pierce);
        if (int.TryParse(TbMaxS.Text, out var maxS)) s.Set("laser_max_s", maxS);
        s.Set("laser_mode", RbPwm.IsChecked == true ? "PWM" : "TTL");
        s.Set("port", TbPort.Text.Trim());
        if (int.TryParse(TbBaud.Text, out var baud)) s.Set("baud", baud);
        if (double.TryParse(TbW.Text,        NumberStyles.Any, CultureInfo.InvariantCulture, out var w))  s.Set("tube_W",   w);
        if (double.TryParse(TbH.Text,        NumberStyles.Any, CultureInfo.InvariantCulture, out var h))  s.Set("tube_H",   h);
        if (double.TryParse(TbStandoff.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var st)) s.Set("standoff", st);
        if (double.TryParse(TbJogFeed.Text,  NumberStyles.Any, CultureInfo.InvariantCulture, out var jf)) s.Set("jog_feed", jf);
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
        DialogResult = true;
        Close();
    }
}
