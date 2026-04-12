using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using AxiomFusion.CncController.ViewModels;

namespace AxiomFusion.CncController.Views;

public partial class OffsetPanel : UserControl
{
    private string _activeWcs = "G54";

    public OffsetPanel() => InitializeComponent();

    private void Wcs_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string wcs)
        {
            _activeWcs = wcs;
            if (DataContext is MainViewModel vm)
                vm.SendMdiCommand.Execute(wcs);   // Activar WCS no controlador
        }
    }

    private void ZeroAxis_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string axis && DataContext is MainViewModel vm)
            vm.SetWcsZeroCommand.Execute($"{_activeWcs}:{axis}");
    }

    private void ZeroAll_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.SetWcsZeroCommand.Execute($"{_activeWcs}:ALL");
    }
}
