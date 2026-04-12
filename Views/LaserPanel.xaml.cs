using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AxiomFusion.CncController.ViewModels;

namespace AxiomFusion.CncController.Views;

public partial class LaserPanel : UserControl
{
    public LaserPanel() => InitializeComponent();

    private void Mode_Changed(object sender, RoutedEventArgs e)
    {
        if (PwmGroup == null) return;
        bool isPwm = RbPwm.IsChecked == true;
        PwmGroup.Visibility = isPwm ? Visibility.Visible : Visibility.Collapsed;
        TtlGroup.Visibility = isPwm ? Visibility.Collapsed : Visibility.Visible;
        if (DataContext is MainViewModel vm) vm.LaserMode = isPwm ? "PWM" : "TTL";
    }

    private void SliderPwm_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (DataContext is not MainViewModel vm) return;
        // Actualizar texto S value
        int maxS = 1000; // será carregado via binding futuro
        TbSValue.Text = $" S={(int)(vm.LaserPwmPct / 100.0 * maxS)}";
    }

    private void BtnTtl_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        BtnTtl.Content = vm.LaserTtlOn ? "LASER ON" : "LASER OFF";
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
