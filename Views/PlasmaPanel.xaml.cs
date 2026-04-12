using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AxiomFusion.CncController.ViewModels;

namespace AxiomFusion.CncController.Views;

public partial class PlasmaPanel : UserControl
{
    public PlasmaPanel()
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
            RbPlasmaPwm.IsChecked = true;
        else
            RbPlasmaM.IsChecked = true;
    }

    private void PlasmaMode_Checked(object sender, RoutedEventArgs e)
    {
        if (VM is null) return;
        if (RbPlasmaPwm.IsChecked == true)
            VM.SetPlasmaTorchModeCommand.Execute("PWM");
        else if (RbPlasmaM.IsChecked == true)
            VM.SetPlasmaTorchModeCommand.Execute("ONOFF");
    }

    private void SliderPwm_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (VM is null || TbSValue == null) return;
        int maxS = VM.PlasmaMaxSForUi;
        TbSValue.Text = $" S={(int)(VM.LaserPwmPct / 100.0 * maxS)}";
    }

    private void BtnTtl_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        BtnTtl.Content = vm.LaserTtlOn ? "TOCHA ON" : "TOCHA OFF";
        vm.ToggleLaserTtlCommand.Execute(null);
    }

    private void BtnTestFire_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        int dur = int.TryParse(TbDuration.Text, out var d) ? d : 500;

        if (vm.LaserMode == "PWM")
            vm.SetLaserPwmCommand.Execute(null);
        else
            vm.ToggleLaserTtlCommand.Execute(null);

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(dur) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (vm.LaserMode == "PWM")
            {
                vm.LaserPwmPct = 0;
                vm.SetLaserPwmCommand.Execute(null);
            }
            else
                vm.ToggleLaserTtlCommand.Execute(null);
        };
        timer.Start();
    }
}
