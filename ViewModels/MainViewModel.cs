using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AxiomFusion.CncController.Comm;
using AxiomFusion.CncController.Core;

namespace AxiomFusion.CncController.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly SettingsManager _settings;
    private IController _ctrl;

    // ── DRO ──────────────────────────────────────────────────────────────
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

    // ── Ligação ──────────────────────────────────────────────────────────
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

    // ── Spindle (Drill / Turn) ────────────────────────────────────────────
    [ObservableProperty] private double  _spindleRpm       = 1000;
    [ObservableProperty] private bool    _spindleRunning;
    [ObservableProperty] private bool    _coolantOn;
    [ObservableProperty] private bool    _diameterMode;    // torno: X = diâmetro
    [ObservableProperty] private bool    _cssMode;         // torno: velocidade de corte constante
    [ObservableProperty] private double  _cssSpeed         = 100.0; // m/min

    // ── Tipo de máquina ───────────────────────────────────────────────────
    [ObservableProperty] private MachineType _currentMachine = MachineType.Laser;
    [ObservableProperty] private string      _machineName    = "Laser";

    // Visibilidade dos painéis direitos
    [ObservableProperty] private Visibility _laserPanelVisible     = Visibility.Visible;
    [ObservableProperty] private Visibility _plasmaPanelVisible    = Visibility.Collapsed;
    [ObservableProperty] private Visibility _drillPanelVisible     = Visibility.Collapsed;
    [ObservableProperty] private Visibility _turnPanelVisible      = Visibility.Collapsed;
    [ObservableProperty] private Visibility _turnLaserPanelVisible = Visibility.Collapsed;
    [ObservableProperty] private Visibility _turnPlasmaPanelVisible  = Visibility.Collapsed;

    /// <summary>Plasma: true = intensidade via S (PWM), false = só M ligar/desligar.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlasmaShowMOnlyTorch))]
    private bool _plasmaUsesIntensity = true;

    /// <summary>Para visibilidade do painel só-M na UI plasma.</summary>
    public bool PlasmaShowMOnlyTorch => !PlasmaUsesIntensity;

    /// <summary>S máximo para labels no painel torno+plasma (binding).</summary>
    public int PlasmaMaxSForUi => MaxSForTorch();

    // Label dinâmico no StatusPanel ("Laser S:" vs "Spindle RPM:")
    [ObservableProperty] private string _spindleOrLaserLabel = "Laser S:";

    // Lista de nomes de máquinas para o ComboBox na toolbar
    public ObservableCollection<string> MachineTypeNames { get; } =
        new(Enum.GetValues<MachineType>().Select(MachineProfiles.DisplayName));

    [ObservableProperty] private string _selectedMachineName = MachineProfiles.DisplayName(MachineType.Laser);

    // ── Portas ────────────────────────────────────────────────────────────
    public ObservableCollection<string> AvailablePorts { get; } = [];

    // ── Programa ─────────────────────────────────────────────────────────
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

        // Restaurar tipo de máquina guardado
        var saved = _settings.GetMachineType();
        ApplyMachineType(saved, notify: false);
    }

    // ── Comandos de ligação / máquina ─────────────────────────────────────

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

    // ── Programa ─────────────────────────────────────────────────────────

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

    // ── MDI ───────────────────────────────────────────────────────────────

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

    // ── Jog ───────────────────────────────────────────────────────────────

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

    // ── Overrides ────────────────────────────────────────────────────────

    [RelayCommand]
    private void SetFeedOverride(object? param)
    {
        int dir = param is int i ? i : 0;
        _ctrl.SetFeedOverride(dir);
    }

    // ── Laser ─────────────────────────────────────────────────────────────

    [RelayCommand]
    private void SetLaserPwm()
        => _ctrl.SetLaserPwm(LaserPwmPct, MaxSForTorch());

    [RelayCommand]
    private void ToggleLaserTtl()
    {
        LaserTtlOn = !LaserTtlOn;
        _ctrl.SetLaserTtl(LaserTtlOn, MaxSForTorch());
    }

    /// <summary>Plasma: alternar entre S=intensidade e só M (persiste em plasma_mode).</summary>
    [RelayCommand]
    private void SetPlasmaTorchMode(object? param)
    {
        bool pwm = param is string s && s == "PWM";
        PlasmaUsesIntensity = pwm;
        _settings.Set("plasma_mode", pwm ? "PWM" : "ONOFF");
        LaserMode = pwm ? "PWM" : "TTL";
        _ctrl.UpdateMCodes(CodesForController());
        RefreshTorchStatusLabel();
    }

    // ── Spindle (Drill / Turn) ────────────────────────────────────────────

    [RelayCommand]
    private void SpindleOn()
    {
        SpindleRunning = true;
        _ctrl.SendMdi($"{_settings.GetString("spindle_on_fwd","M3")} S{(int)SpindleRpm}");
    }

    [RelayCommand]
    private void SpindleReverse()
    {
        SpindleRunning = true;
        _ctrl.SendMdi($"{_settings.GetString("spindle_on_rev","M4")} S{(int)SpindleRpm}");
    }

    [RelayCommand]
    private void SpindleOff()
    {
        SpindleRunning = false;
        _ctrl.SendMdi(_settings.GetString("spindle_off", "M5"));
    }

    [RelayCommand]
    private void ApplySpindleRpm()
    {
        if (!SpindleRunning) return;
        _ctrl.SendMdi($"S{(int)SpindleRpm}");
    }

    [RelayCommand]
    private void ToggleCoolant()
    {
        CoolantOn = !CoolantOn;
        var mcode = CoolantOn
            ? _settings.GetString("coolant_on",  "M8")
            : _settings.GetString("coolant_off", "M9");
        _ctrl.SendMdi(mcode);
    }

    [RelayCommand]
    private void CoolantOnCmd()
    {
        CoolantOn = true;
        _ctrl.SendMdi(_settings.GetString("coolant_on", "M8"));
    }

    [RelayCommand]
    private void CoolantOffCmd()
    {
        CoolantOn = false;
        _ctrl.SendMdi(_settings.GetString("coolant_off", "M9"));
    }

    [RelayCommand]
    private void ToolChange(object? param)
    {
        int toolNum = param is string s && int.TryParse(s, out var n) ? n : 1;
        _ctrl.SendMdi($"T{toolNum} M6");
    }

    [RelayCommand]
    private void ToggleDiameterMode()
    {
        DiameterMode = !DiameterMode;
        _ctrl.SendMdi(DiameterMode ? "G7" : "G8");
        _settings.Set("turn_diameter_mode", DiameterMode);
    }

    [RelayCommand]
    private void ApplyCssSpeed()
    {
        if (CssMode)
            _ctrl.SendMdi($"G96 S{(int)CssSpeed}");
        else
            _ctrl.SendMdi($"G97 S{(int)SpindleRpm}");
    }

    // ── Laser on/off para TurnLaser ───────────────────────────────────────

    [RelayCommand]
    private void TurnLaserOn()
    {
        var mOn  = _settings.GetString("turn_laser_on", "M7");
        int maxS = _settings.GetInt("laser_max_s", 1000);
        int s    = (int)(LaserPwmPct / 100.0 * maxS);
        _ctrl.SendMdi($"{mOn} S{s}");
    }

    [RelayCommand]
    private void TurnLaserOff()
        => _ctrl.SendMdi(_settings.GetString("turn_laser_off", "M107"));

    [RelayCommand]
    private void TurnPlasmaOn()
    {
        var pl  = _settings.BuildPlasmaMCodes();
        var mOn = _settings.GetString("turn_plasma_on", "M7").Trim();
        if (string.Equals(pl.Mode, "ONOFF", StringComparison.OrdinalIgnoreCase))
            _ctrl.SendMdi(mOn);
        else
        {
            int s = (int)(LaserPwmPct / 100.0 * pl.MaxS);
            _ctrl.SendMdi($"{mOn} S{s}");
        }
    }

    [RelayCommand]
    private void TurnPlasmaOff()
        => _ctrl.SendMdi(_settings.GetString("turn_plasma_off", "M107").Trim());

    // ── WCS / Zero ────────────────────────────────────────────────────────

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

    // ── Tipo de máquina ───────────────────────────────────────────────────

    [RelayCommand]
    private void ChangeMachineType(object? param)
    {
        if (param is not string name) return;
        var mt = Enum.GetValues<MachineType>()
                     .FirstOrDefault(m => MachineProfiles.DisplayName(m) == name);
        ApplyMachineType(mt, notify: true);
    }

    partial void OnSelectedMachineNameChanged(string value)
        => ChangeMachineType(value);

    private void ApplyMachineType(MachineType mt, bool notify)
    {
        CurrentMachine   = mt;
        MachineName      = MachineProfiles.DisplayName(mt);
        SelectedMachineName = MachineName;

        LaserPanelVisible      = mt == MachineType.Laser      ? Visibility.Visible : Visibility.Collapsed;
        PlasmaPanelVisible     = mt == MachineType.Plasma     ? Visibility.Visible : Visibility.Collapsed;
        DrillPanelVisible      = mt == MachineType.Drill      ? Visibility.Visible : Visibility.Collapsed;
        TurnPanelVisible       = mt == MachineType.Turn       ? Visibility.Visible : Visibility.Collapsed;
        TurnLaserPanelVisible  = mt == MachineType.TurnLaser  ? Visibility.Visible : Visibility.Collapsed;
        TurnPlasmaPanelVisible = mt == MachineType.TurnPlasma ? Visibility.Visible : Visibility.Collapsed;

        PlasmaUsesIntensity = string.Equals(_settings.GetString("plasma_mode", "PWM"), "PWM",
            StringComparison.OrdinalIgnoreCase);
        if (mt is MachineType.Plasma or MachineType.TurnPlasma)
            LaserMode = PlasmaUsesIntensity ? "PWM" : "TTL";
        else if (mt is MachineType.Laser or MachineType.TurnLaser)
            LaserMode = string.Equals(_settings.GetString("laser_mode", "PWM"), "PWM", StringComparison.OrdinalIgnoreCase)
                ? "PWM" : "TTL";
        RefreshTorchStatusLabel();

        _settings.SetMachineType(mt);
        _ctrl.UpdateMCodes(CodesForController());

        if (notify)
            StatusMessage = $"Modo máquina: {MachineName}";
    }

    // ── Programa carregado ────────────────────────────────────────────────

    public void OnProgramLoaded(GCodeProgram program)
    {
        var torchCodes   = TorchCodesForProgram();
        var spindleCodes = _settings.BuildSpindleCodes();
        program.Lines = GCodePreprocessor.PreprocessForMachine(
            program.Lines, CurrentMachine, torchCodes, spindleCodes);
        program.LineCount = program.Lines.Count;

        var linesToCheck = program.Lines.Count > 500
            ? program.Lines[..500] : program.Lines;
        var warnings = GCodePreprocessor.Validate(linesToCheck);
        if (warnings.Count > 0)
        {
            string prefix = program.Lines.Count > 500
                ? "(Verificadas apenas as primeiras 500 linhas)\n" : "";
            StatusMessage = prefix + string.Join("\n", warnings);
        }

        LoadedProgram = program;
        TotalLines    = program.LineCount;
        CurrentLine   = 0;
        Progress      = 0;
        ProgramLoaded?.Invoke(this, program);
    }

    // ── Configurações ─────────────────────────────────────────────────────

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

        var mt = _settings.GetMachineType();
        ApplyMachineType(mt, notify: false);

        if (wasConnected)
            try { _ctrl.Connect(port, baud); } catch { }
    }

    public void SaveSession()
    {
        _settings.Set("mdi_history", MdiHistory.ToList());
    }

    // ── Helpers privados ──────────────────────────────────────────────────

    private IController CreateController()
    {
        var codes = CodesForController();
        return _settings.GetString("controller_type", "GRBL") == "GRBL"
            ? new GrblController(codes)
            : new IsoController(codes);
    }

    private MCodes CodesForController()
    {
        var mt = _settings.GetMachineType();
        return mt is MachineType.Plasma or MachineType.TurnPlasma
            ? _settings.BuildPlasmaMCodes()
            : _settings.BuildMCodes();
    }

    private MCodes TorchCodesForProgram()
        => CurrentMachine is MachineType.Plasma or MachineType.TurnPlasma
            ? _settings.BuildPlasmaMCodes()
            : _settings.BuildMCodes();

    private int MaxSForTorch()
    {
        if (CurrentMachine is MachineType.Plasma or MachineType.TurnPlasma)
        {
            int p = _settings.GetInt("plasma_max_s", 0);
            return p > 0 ? p : _settings.GetInt("laser_max_s", 1000);
        }
        return _settings.GetInt("laser_max_s", 1000);
    }

    private void RefreshTorchStatusLabel()
    {
        SpindleOrLaserLabel = CurrentMachine switch
        {
            MachineType.Laser      => "Laser S:",
            MachineType.Plasma     => PlasmaUsesIntensity ? "Plasma S:" : "Tocha:",
            MachineType.TurnPlasma => PlasmaUsesIntensity ? "Plasma S:" : "Tocha:",
            _                      => "Spindle:",
        };
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

        FeedRate     = snap.FeedRate;
        LaserPower   = snap.LaserPower;
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
