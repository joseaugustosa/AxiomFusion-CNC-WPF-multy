using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AxiomFusion.CncController.ViewModels;

namespace AxiomFusion.CncController.Views;

public partial class TurnLaserPanel : UserControl
{
    private DispatcherTimer? _testTimer;

    public TurnLaserPanel() => InitializeComponent();

    private MainViewModel? VM => DataContext as MainViewModel;

    private void TbRpm_LostFocus(object sender, RoutedEventArgs e)
    {
        if (VM is null) return;
        if (double.TryParse(TbRpm.Text, out var rpm))
            VM.SpindleRpm = rpm;
    }

    private void DiameterMode_Click(object sender, RoutedEventArgs e)
        => VM?.ToggleDiameterModeCommand.Execute(null);

    private void SliderPwm_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (VM is null) return;
        // Actualizar label S=value
        // O binding já actualiza LaserPwmPct; só precisamos do label S
        // Obtemos laser_max_s da ViewModel (não temos acesso directo a settings aqui,
        // mas podemos calcular via LaserPwmPct)
        if (TbSValue != null)
            TbSValue.Text = $" S={(int)(VM.LaserPwmPct / 100.0 * 1000)}";
    }

    private void BtnTestFire_Click(object sender, RoutedEventArgs e)
    {
        if (VM is null) return;
        if (!int.TryParse(TbDuration.Text, out int ms)) ms = 500;

        VM.TurnLaserOnCommand.Execute(null);

        _testTimer?.Stop();
        _testTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        _testTimer.Tick += (_, __) =>
        {
            _testTimer.Stop();
            VM.TurnLaserOffCommand.Execute(null);
        };
        _testTimer.Start();
    }
}
