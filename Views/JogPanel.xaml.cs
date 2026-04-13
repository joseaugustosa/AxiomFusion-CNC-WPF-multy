using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Globalization;
using AxiomFusion.CncController.ViewModels;

namespace AxiomFusion.CncController.Views;

public partial class JogPanel : UserControl
{
    private double _stepXY = 1.0;
    private double _stepA  = 1.0;
    private readonly DispatcherTimer _jogRepeatTimer;
    private string? _activeJogRaw;

    public JogPanel()
    {
        InitializeComponent();
        Focusable = true;
        _jogRepeatTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(120)
        };
        _jogRepeatTimer.Tick += JogRepeatTimer_Tick;
    }

    private void CmbStepXY_SelectionChanged(object s, SelectionChangedEventArgs e)
    {
        if (CmbStepXY.SelectedItem is ComboBoxItem item &&
            double.TryParse(item.Content?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            _stepXY = v;
    }

    private void CmbStepA_SelectionChanged(object s, SelectionChangedEventArgs e)
    {
        if (CmbStepA.SelectedItem is ComboBoxItem item &&
            double.TryParse(item.Content?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            _stepA = v;
    }

    private void JogButton_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button { Tag: string raw }) return;
        _activeJogRaw = raw;
        ExecuteJogFromRaw(raw);
        _jogRepeatTimer.Start();
        e.Handled = true;
    }

    private void JogButton_MouseUp(object sender, MouseButtonEventArgs e)
    {
        StopContinuousJog();
        e.Handled = true;
    }

    private void JogButton_MouseLeave(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            StopContinuousJog();
    }

    private void JogRepeatTimer_Tick(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_activeJogRaw)) return;
        ExecuteJogFromRaw(_activeJogRaw);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        string? raw = e.Key switch
        {
            Key.Right => "X:1",
            Key.Left  => "X:-1",
            Key.Up    => "Y:1",
            Key.Down  => "Y:-1",
            Key.PageUp   => "Z:1",
            Key.PageDown => "Z:-1",
            Key.OemOpenBrackets  => "A:-1",
            Key.OemCloseBrackets => "A:1",
            _ => null
        };
        if (raw is not null)
        {
            ExecuteJogFromRaw(raw);
            e.Handled = true;
        }
    }

    private void ExecuteJogFromRaw(string raw)
    {
        if (DataContext is not MainViewModel vm) return;
        if (TryBuildJogParam(raw, out var param) && vm.JogAxisCommand.CanExecute(param))
            vm.JogAxisCommand.Execute(param);
    }

    private bool TryBuildJogParam(string raw, out string param)
    {
        param = "";
        var parts = raw.Split(':');
        if (parts.Length != 2) return false;

        var axis = parts[0].Trim().ToUpperInvariant();
        if (axis is not ("X" or "Y" or "Z" or "A")) return false;

        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var dir))
            return false;

        double unit = axis == "A" ? _stepA : _stepXY;
        double distance = dir >= 0 ? unit : -unit;
        double feed = double.TryParse(TbFeed.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)
            ? f : 500;

        param = $"{axis}:{distance.ToString(CultureInfo.InvariantCulture)}:{feed.ToString(CultureInfo.InvariantCulture)}";
        return true;
    }

    private void StopContinuousJog()
    {
        _jogRepeatTimer.Stop();
        _activeJogRaw = null;

        if (DataContext is MainViewModel vm && vm.JogCancelCommand.CanExecute(null))
            vm.JogCancelCommand.Execute(null);
    }
}
