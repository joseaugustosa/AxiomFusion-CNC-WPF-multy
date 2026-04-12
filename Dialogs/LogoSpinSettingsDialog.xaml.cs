using System.Globalization;
using System.Windows;

namespace AxiomFusion.CncController.Dialogs;

public partial class LogoSpinSettingsDialog : Window
{
    /// <summary>Velocidade em graus/segundo (0 = parado).</summary>
    public double DegreesPerSecond { get; private set; }

    public LogoSpinSettingsDialog(double degreesPerSecond)
    {
        InitializeComponent();
        DegreesPerSecond = Math.Clamp(degreesPerSecond, 0, 120);
        SliderSpeed.Value = DegreesPerSecond;
        UpdateLabel();
    }

    private void SliderSpeed_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded) return;
        DegreesPerSecond = SliderSpeed.Value;
        UpdateLabel();
    }

    private void UpdateLabel()
    {
        var v = SliderSpeed.Value;
        TbValue.Text = v <= 0.01
            ? "Parado"
            : string.Format(CultureInfo.CurrentCulture, "{0:0.#} °/s", v);
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        var v = SliderSpeed.Value;
        if (double.IsNaN(v) || double.IsInfinity(v)) v = 36.0;
        DegreesPerSecond = Math.Clamp(v, 0, 120);
        DialogResult = true;
    }
}
