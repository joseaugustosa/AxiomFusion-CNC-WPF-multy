using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using AxiomFusion.CncController.Core;
using AxiomFusion.CncController.Dialogs;
using AxiomFusion.CncController.ViewModels;

namespace AxiomFusion.CncController;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly SettingsManager _settings;

    public MainWindow()
    {
        InitializeComponent();

        _settings = new SettingsManager();
        _vm       = new MainViewModel(_settings);
        DataContext = _vm;
        VizPanel.Settings = _settings;

        TryLoadWindowIcon();

        // Ligar eventos do ViewModel que precisam de acção na View
        _vm.ProgramLoaded            += OnProgramLoaded;
        _vm.LineHighlightRequested   += OnLineHighlight;
        _vm.SimulationPoseRequested += OnSimulationPose;
        _vm.PropertyChanged          += OnVmPropertyChanged;
    }

    /// <summary>Ícone fora do XAML: evita falhas do conversor pack:// com recursos embutidos.</summary>
    private void TryLoadWindowIcon()
    {
        string[] packUris =
        [
            "pack://application:,,,/AxiomFusion.CncController;component/Assets/Icons/axiom-logo-lightbg.png",
            "pack://application:,,,/AxiomFusion.CncController;component/Assets/Icons/axiom-logo.png",
            "pack://application:,,,/AxiomFusion.CncController;component/Assets/Icons/app.png",
            "pack://application:,,,/Assets/Icons/app.png",
        ];
        foreach (var s in packUris)
        {
            try
            {
                Icon = BitmapFrame.Create(new Uri(s, UriKind.Absolute));
                return;
            }
            catch { /* tentar seguinte */ }
        }

        try
        {
            foreach (var name in new[] { "axiom-logo-lightbg.png", "axiom-logo.png", "app.png" })
            {
                var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", name);
                if (File.Exists(path))
                {
                    Icon = BitmapFrame.Create(new Uri(path, UriKind.Absolute));
                    return;
                }
            }
        }
        catch { /* sem ícone */ }
    }

    // ── Propagação de eventos ─────────────────────────────────────────────

    private void OnProgramLoaded(object? _, GCodeProgram prog)
    {
        double W = _settings.GetDouble("tube_W", 50.0);
        double H = _settings.GetDouble("tube_H", 50.0);
        VizPanel.LoadProgram(prog, W, H);
    }

    private void OnLineHighlight(object? _, int idx)
    {
        GcodePanel.HighlightLine(idx);
        VizPanel.HighlightLine(idx, _vm.IsSimulating);
    }

    private void OnSimulationPose(object? _, SimulationPoseEventArgs e)
    {
        GcodePanel.HighlightLine(e.ActiveSourceLineIndex);
        VizPanel.UpdateSimulationPose(e.TipWorld, e.SegmentStartWorld, e.ADeg, e.Cutting);
    }

    private void OnVmPropertyChanged(object? _, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsConnected))
        {
            TbConnStatus.Text = _vm.IsConnected ? "Ligado"    : "Desligado";
            TbPort.Text       = _vm.IsConnected ? _vm.SelectedPort : "";
        }
        else if (e.PropertyName == nameof(MainViewModel.IsSimulating) && !_vm.IsSimulating)
        {
            VizPanel.HideLaserNozzle();
        }
    }

    // ── Configurações ─────────────────────────────────────────────────────

    private void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SettingsDialog(_settings) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            dlg.SaveToSettings(_settings);
            _vm.ApplyNewSettings();
            VizPanel.ApplyWatermarkFromSettings();
        }
    }

    // ── Fechar janela ─────────────────────────────────────────────────────

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _vm.SaveSession();
        _settings.Set("window_width",  (int)ActualWidth);
        _settings.Set("window_height", (int)ActualHeight);
        base.OnClosing(e);
    }
}
