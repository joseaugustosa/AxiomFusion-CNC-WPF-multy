using System.Windows;
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
        _settings = new SettingsManager();
        _vm       = new MainViewModel(_settings);
        DataContext = _vm;

        InitializeComponent();

        // Ligar eventos do ViewModel que precisam de acção na View
        _vm.ProgramLoaded          += OnProgramLoaded;
        _vm.LineHighlightRequested += OnLineHighlight;
        _vm.PropertyChanged        += OnVmPropertyChanged;
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
        VizPanel.HighlightLine(idx);
    }

    private void OnVmPropertyChanged(object? _, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsConnected))
        {
            TbConnStatus.Text = _vm.IsConnected ? "Ligado"    : "Desligado";
            TbPort.Text       = _vm.IsConnected ? _vm.SelectedPort : "";
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
