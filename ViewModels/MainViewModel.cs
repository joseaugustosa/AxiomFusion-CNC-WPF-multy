using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AxiomFusion.CncController.Comm;
using AxiomFusion.CncController.Core;

namespace AxiomFusion.CncController.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly SettingsManager _settings;
    private IController _ctrl;

    // ── Posições DRO ─────────────────────────────────────────────────────
    [ObservableProperty] private double _posX;
    [ObservableProperty] private double _posY;
    [ObservableProperty] private double _posZ;
    [ObservableProperty] private double _posA;
    [ObservableProperty] private bool   _showWork = true;

    // ── Estado ───────────────────────────────────────────────────────────
    [ObservableProperty] private string  _machineStatus    = "Idle";
    [ObservableProperty] private string  _statusColor      = "#2ecc71";
    [ObservableProperty] private string  _statusLabel      = "Inativo";
    [ObservableProperty] private int     _feedRate;
    [ObservableProperty] private int     _laserPower;
    [ObservableProperty] private int     _feedOverride     = 100;
    [ObservableProperty] private bool    _limitX;
    [ObservableProperty] private bool    _limitY;
    [ObservableProperty] private bool    _limitZ;

    // ── Ligação ───────────────────────────────────────────────────────────
    [ObservableProperty] private string  _selectedPort     = "COM3";
    [ObservableProperty] private bool    _isConnected;
    [ObservableProperty] private string  _connectLabel     = "Ligar";
    [ObservableProperty] private string  _controllerTypeLabel = "[GRBL  M3/M5]";

    // ── Programa ─────────────────────────────────────────────────────────
    [ObservableProperty] private string  _statusMessage    = "";
    [ObservableProperty] private int     _currentLine;
    [ObservableProperty] private int     _totalLines;
    [ObservableProperty] private double  _progress;

    // ── MDI ───────────────────────────────────────────────────────────────
    [ObservableProperty] private string  _mdiInput         = "";
    public ObservableCollection<string>  MdiHistory { get; } = [];

    // ── Laser ─────────────────────────────────────────────────────────────
    [ObservableProperty] private double  _laserPwmPct      = 50.0;
    [ObservableProperty] private bool    _laserTtlOn;
    [ObservableProperty] private string  _laserMode        = "PWM";

    // ── Portas disponíveis ────────────────────────────────────────────────
    public ObservableCollection<string> AvailablePorts { get; } = [];

    // ── Programa carregado ────────────────────────────────────────────────
    public GCodeProgram? LoadedProgram { get; private set; }

    public event EventHandler<GCodeProgram>? ProgramLoaded;
    public event EventHandler<int>?          LineHighlightRequested;

    public MainViewModel(SettingsManager settings)
    {
        _settings = settings;
        _ctrl     = CreateController();
        ConnectControllerEvents();
        RefreshPorts();
        LoadMdiHistory();
        UpdateCtrlTypeLabel();
    }

    // ── Comandos ──────────────────────────────────────────────────────────

    [RelayCommand]
    private void ToggleConnect()
    {
        if (IsConnected)
        {
            _ctrl.Disconnect();
        }
        else
        {
            int baud = _settings.GetInt("baud", 115200);
            try { _ctrl.Connect(SelectedPort, baud); }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Erro de ligação",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    [RelayCommand]
    private void RefreshPorts()
    {
        AvailablePorts.Clear();
        foreach (var p in GrblSerial.ListPorts()) AvailablePorts.Add(p);
        if (!AvailablePorts.Contains(SelectedPort) && AvailablePorts.Count > 0)
            SelectedPort = AvailablePorts[0];
    }

    [RelayCommand]
    private void Home()   => _ctrl.Home();

    [RelayCommand]
    private void Unlock() => _ctrl.Unlock();

    [RelayCommand]
    private void StartProgram()
    {
        if (LoadedProgram is null || !IsConnected) return;
        _ctrl.StartProgram(LoadedProgram);
    }

    [RelayCommand]
    private void FeedHold() => _ctrl.FeedHold();

    [RelayCommand]
    private void AbortProgram() => _ctrl.AbortProgram();

    [RelayCommand]
    private void SendMdi()
    {
        var cmd = MdiInput.Trim();
        if (string.IsNullOrEmpty(cmd)) return;
        _ctrl.SendMdi(cmd);
        if (!MdiHistory.Contains(cmd)) MdiHistory.Insert(0, cmd);
        if (MdiHistory.Count > 50) MdiHistory.RemoveAt(MdiHistory.Count - 1);
        MdiInput = "";
    }

    [RelayCommand]
    private void JogAxis(object? param)
    {
        if (param is not string s) return;
        var parts = s.Split(':');
        if (parts.Length < 2) return;
        string axis = parts[0];
        double step = double.TryParse(parts[1], out var v)
            ? v : _settings.GetDouble("jog_step", 1.0);
        double feed = _settings.GetDouble("jog_feed", 500.0);
        _ctrl.Jog(axis, step, feed);
    }

    [RelayCommand]
    private void SetFeedOverride(object? param)
    {
        int dir = param is int i ? i : 0;
        _ctrl.SetFeedOverride(dir);
    }

    [RelayCommand]
    private void SetLaserPwm()
        => _ctrl.SetLaserPwm(LaserPwmPct, _settings.GetInt("laser_max_s", 1000));

    [RelayCommand]
    private void ToggleLaserTtl()
    {
        LaserTtlOn = !LaserTtlOn;
        _ctrl.SetLaserTtl(LaserTtlOn, _settings.GetInt("laser_max_s", 1000));
    }

    [RelayCommand]
    private void SetWcsZero(object? param)
    {
        if (param is not string s) return;
        var parts = s.Split(':');
        string wcs  = parts[0];
        string axis = parts.Length > 1 ? parts[1] : "ALL";

        string[] wcsList = ["G54","G55","G56","G57","G58","G59"];
        int idx = Array.IndexOf(wcsList, wcs);
        if (idx < 0) return;
        int num = idx + 1;

        var axes = axis == "ALL"
            ? new Dictionary<string,double> { ["X"]=0,["Y"]=0,["Z"]=0,["A"]=0 }
            : new Dictionary<string,double> { [axis]=0 };
        _ctrl.SetWcsZero(num, axes);
    }

    [RelayCommand]
    private void GotoWorkZero()
        => _ctrl.SendMdi("G0 X0 Y0 Z25 A0");

    // ── Programa carregado (chamado do GCodePanel) ─────────────────────────

    public void OnProgramLoaded(GCodeProgram program)
    {
        var codes = _settings.BuildMCodes();
        program.Lines     = GCodePreprocessor.Preprocess(program.Lines, codes);
        program.LineCount = program.Lines.Count;

        var linesToCheck = program.Lines.Count > 500
            ? program.Lines[..500] : program.Lines;
        var warnings = GCodePreprocessor.Validate(linesToCheck);
        if (warnings.Count > 0)
        {
            string prefix = program.Lines.Count > 500
                ? $"(Verificadas apenas as primeiras 500 linhas)\n" : "";
            StatusMessage = prefix + string.Join("\n", warnings);
        }

        LoadedProgram = program;
        TotalLines    = program.LineCount;
        CurrentLine   = 0;
        Progress      = 0;
        ProgramLoaded?.Invoke(this, program);
    }

    // ── Configurações (recriar controlador) ───────────────────────────────

    public void ApplyNewSettings()
    {
        bool wasConnected = IsConnected;
        string port = SelectedPort;
        int    baud = _settings.GetInt("baud", 115200);

        if (wasConnected) _ctrl.Disconnect();
        DisconnectControllerEvents();
        _ctrl.Dispose();

        _ctrl = CreateController();
        ConnectControllerEvents();
        UpdateCtrlTypeLabel();

        if (wasConnected)
        {
            try { _ctrl.Connect(port, baud); } catch { }
        }
    }

    public void SaveSession()
    {
        _settings.Set("mdi_history", MdiHistory.ToList());
    }

    // ── Helpers privados ──────────────────────────────────────────────────

    private IController CreateController()
    {
        var codes = _settings.BuildMCodes();
        return _settings.GetString("controller_type", "GRBL") == "GRBL"
            ? new GrblController(codes)
            : new IsoController(codes);
    }

    private void ConnectControllerEvents()
    {
        _ctrl.ConnectionChanged += OnConnectionChanged;
        _ctrl.StatusUpdated     += OnStatusUpdated;
        _ctrl.AlarmTriggered    += OnAlarm;
        _ctrl.ErrorReceived     += OnError;
        _ctrl.LineExecuted      += OnLineExecuted;
        _ctrl.ProgramFinished   += OnProgramFinished;
        _ctrl.LogMessage        += OnLogMessage;
    }

    private void DisconnectControllerEvents()
    {
        _ctrl.ConnectionChanged -= OnConnectionChanged;
        _ctrl.StatusUpdated     -= OnStatusUpdated;
        _ctrl.AlarmTriggered    -= OnAlarm;
        _ctrl.ErrorReceived     -= OnError;
        _ctrl.LineExecuted      -= OnLineExecuted;
        _ctrl.ProgramFinished   -= OnProgramFinished;
        _ctrl.LogMessage        -= OnLogMessage;
    }

    private void UpdateCtrlTypeLabel()
    {
        var t  = _settings.GetString("controller_type", "GRBL");
        var on = _settings.GetString("m_on",  "M3");
        var of = _settings.GetString("m_off", "M5");
        ControllerTypeLabel = $"[{t}  {on}/{of}]";
    }

    private void LoadMdiHistory()
    {
        foreach (var h in _settings.GetStringList("mdi_history"))
            MdiHistory.Add(h);
    }

    // ── Event handlers ────────────────────────────────────────────────────

    private void OnConnectionChanged(object? _, bool connected)
    {
        IsConnected   = connected;
        ConnectLabel  = connected ? "Desligar" : "Ligar";
        StatusMessage = connected ? $"Ligado a {SelectedPort}" : "Desligado";
    }

    private void OnStatusUpdated(object? _, MachineSnapshot snap)
    {
        var pos = ShowWork ? snap.WorkPos : snap.MachinePos;
        PosX = pos.X; PosY = pos.Y; PosZ = pos.Z; PosA = pos.A;

        FeedRate    = snap.FeedRate;
        LaserPower  = snap.LaserPower;
        FeedOverride = snap.FeedOverride;
        LimitX = snap.LimitX; LimitY = snap.LimitY; LimitZ = snap.LimitZ;

        var (label, color) = GrblConstants.GetState(snap.Status);
        MachineStatus = snap.Status;
        StatusLabel   = label;
        StatusColor   = color;
    }

    private void OnAlarm(object? _, GrblAlarmEventArgs e)
    {
        StatusMessage = $"ALARME {e.Code}: {e.Description}";
        MessageBox.Show(e.Description, $"ALARME {e.Code}",
            MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void OnError(object? _, GrblErrorEventArgs e)
    {
        StatusMessage = $"Erro {e.Code}: {e.Description}";
    }

    private void OnLineExecuted(object? _, int idx)
    {
        CurrentLine = idx + 1;
        Progress    = TotalLines > 0 ? (double)CurrentLine / TotalLines * 100.0 : 0;
        LineHighlightRequested?.Invoke(this, idx);
    }

    private void OnProgramFinished(object? _, EventArgs __)
    {
        StatusMessage = "Programa concluído!";
        Progress      = 100;
    }

    private void OnLogMessage(object? _, string msg)
    {
        StatusMessage = msg;
    }
}
