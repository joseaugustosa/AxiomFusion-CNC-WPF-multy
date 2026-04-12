using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AxiomFusion.CncController.ViewModels;

namespace AxiomFusion.CncController.Views;

public partial class TurnPlasmaPanel : UserControl
{
    private DispatcherTimer? _testTimer;

    public TurnPlasmaPanel()
    {
        InitializeComponent();
        Loaded += (_, _) => SyncPlasmaRadios();
        DataContextChanged += (_, _) => SyncPlasmaRadios();
    }

    private MainViewModel? VM => DataContext as MainViewModel;

    private void SyncPlasmaRadios()
    {
        if (VM is null) return;
        if (VM.PlasmaUsesIntensity)
        {
            RbPlasmaPwm.IsChecked = true;
        }
        else
        {
            RbPlasmaM.IsChecked = true;
        }
    }

    private void PlasmaMode_Checked(object sender, RoutedEventArgs e)
    {
        if (VM is null) return;
        if (RbPlasmaPwm.IsChecked == true)
            VM.SetPlasmaTorchModeCommand.Execute("PWM");
        else if (RbPlasmaM.IsChecked == true)
            VM.SetPlasmaTorchModeCommand.Execute("ONOFF");
    }

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
        if (VM is null || TbSValue == null) return;
        int maxS = VM.PlasmaMaxSForUi;
        TbSValue.Text = $" S={(int)(VM.LaserPwmPct / 100.0 * maxS)}";
    }

    private void BtnTestFire_Click(object sender, RoutedEventArgs e)
    {
        if (VM is null) return;
        if (!int.TryParse(TbDuration.Text, out int ms)) ms = 500;

        VM.TurnPlasmaOnCommand.Execute(null);

        _testTimer?.Stop();
        _testTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        _testTimer.Tick += (_, __) =>
        {
            _testTimer.Stop();
            VM.TurnPlasmaOffCommand.Execute(null);
        };
        _testTimer.Start();
    }
}
