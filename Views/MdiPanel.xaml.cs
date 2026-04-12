using System.Windows.Controls;
using System.Windows.Input;
using AxiomFusion.CncController.ViewModels;

namespace AxiomFusion.CncController.Views;

public partial class MdiPanel : UserControl
{
    private int _histIdx = -1;

    public MdiPanel() => InitializeComponent();

    private void TbMdi_KeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var hist = vm.MdiHistory;

        if (e.Key == Key.Return)
        {
            vm.SendMdiCommand.Execute(null);
            _histIdx = -1;
            e.Handled = true;
        }
        else if (e.Key == Key.Up && hist.Count > 0)
        {
            _histIdx = Math.Min(_histIdx + 1, hist.Count - 1);
            vm.MdiInput = hist[_histIdx];
            e.Handled = true;
        }
        else if (e.Key == Key.Down && hist.Count > 0)
        {
            _histIdx = Math.Max(_histIdx - 1, -1);
            vm.MdiInput = _histIdx >= 0 ? hist[_histIdx] : "";
            e.Handled = true;
        }
    }

    private void LbHistory_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (LbHistory.SelectedItem is string cmd && DataContext is MainViewModel vm)
        {
            vm.MdiInput = cmd;
            TbMdi.Focus();
        }
    }
}
