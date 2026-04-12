using System.Windows;
using System.Windows.Controls;
using AxiomFusion.CncController.ViewModels;

namespace AxiomFusion.CncController.Views;

public partial class TurnPanel : UserControl
{
    public TurnPanel() => InitializeComponent();

    private MainViewModel? VM => DataContext as MainViewModel;

    private void SpeedMode_Changed(object sender, RoutedEventArgs e)
    {
        if (VM is null || LbSpeedUnit is null) return;
        bool css = RbCss.IsChecked == true;
        VM.CssMode = css;
        LbSpeedUnit.Content = css ? "m/min" : "RPM";
        TbSpeed.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateTarget();
    }

    private void TbSpeed_LostFocus(object sender, RoutedEventArgs e)
    {
        if (VM is null) return;
        if (double.TryParse(TbSpeed.Text, out var v))
        {
            if (VM.CssMode) VM.CssSpeed  = v;
            else            VM.SpindleRpm = v;
        }
    }

    private void DiameterMode_Click(object sender, RoutedEventArgs e)
        => VM?.ToggleDiameterModeCommand.Execute(null);
}
