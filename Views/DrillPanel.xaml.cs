using System.Windows;
using System.Windows.Controls;
using AxiomFusion.CncController.ViewModels;

namespace AxiomFusion.CncController.Views;

public partial class DrillPanel : UserControl
{
    public DrillPanel() => InitializeComponent();

    private MainViewModel? VM => DataContext as MainViewModel;

    private void TbRpm_LostFocus(object sender, RoutedEventArgs e)
    {
        if (VM is null) return;
        if (double.TryParse(TbRpm.Text, out var rpm))
            VM.SpindleRpm = rpm;
    }

    private void BtnToolChange_Click(object sender, RoutedEventArgs e)
    {
        if (VM is null) return;
        VM.ToolChangeCommand.Execute(TbTool.Text.Trim());
    }
}
