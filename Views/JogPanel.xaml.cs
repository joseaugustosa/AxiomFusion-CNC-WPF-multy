using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AxiomFusion.CncController.ViewModels;

namespace AxiomFusion.CncController.Views;

public partial class JogPanel : UserControl
{
    private double _stepXY = 1.0;
    private double _stepA  = 1.0;

    public JogPanel()
    {
        InitializeComponent();
        Focusable = true;
    }

    private void CmbStepXY_SelectionChanged(object s, SelectionChangedEventArgs e)
    {
        if (CmbStepXY.SelectedItem is ComboBoxItem item &&
            double.TryParse(item.Content?.ToString(), out var v))
            _stepXY = v;
    }

    private void CmbStepA_SelectionChanged(object s, SelectionChangedEventArgs e)
    {
        if (CmbStepA.SelectedItem is ComboBoxItem item &&
            double.TryParse(item.Content?.ToString(), out var v))
            _stepA = v;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (DataContext is not MainViewModel vm) return;
        double feed = double.TryParse(TbFeed.Text, out var f) ? f : 500;

        string? param = e.Key switch
        {
            Key.Right => $"X:{_stepXY}",
            Key.Left  => $"X:{-_stepXY}",
            Key.Up    => $"Y:{_stepXY}",
            Key.Down  => $"Y:{-_stepXY}",
            Key.PageUp   => $"Z:{_stepXY}",
            Key.PageDown => $"Z:{-_stepXY}",
            Key.OemOpenBrackets  => $"A:{-_stepA}",
            Key.OemCloseBrackets => $"A:{_stepA}",
            _ => null
        };
        if (param != null && vm.JogAxisCommand.CanExecute(param))
        {
            vm.JogAxisCommand.Execute(param);
            e.Handled = true;
        }
    }
}
