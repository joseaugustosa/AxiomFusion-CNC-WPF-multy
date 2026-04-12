using System.Windows.Controls;

namespace AxiomFusion.CncController.Views;

public partial class DroPanel : UserControl
{
    public DroPanel() => InitializeComponent();

    private void BtnToggle_Changed(object sender, System.Windows.RoutedEventArgs e)
    {
        BtnToggle.Content = BtnToggle.IsChecked == true ? "Trabalho" : "Máquina";
    }
}
