using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AxiomFusion.CncController.Comm;
using AxiomFusion.CncController.Core;
using AxiomFusion.CncController.Visualizer;

namespace AxiomFusion.CncController.ViewModels;

/// <summary>Dados para animar o bico ao longo do percurso (interpolação entre pontos do toolpath).</summary>
public sealed record SimulationPoseEventArgs(
    Point3D TipWorld,
    Point3D SegmentStartWorld,
    double  ADeg,
    int     ActiveSourceLineIndex,
    bool    Cutting,
    double  PathProgress01);

public partial class MainViewModel : ObservableObject
{
    private const string AutoPortName = "AUTO";
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
    [ObservableProperty] private string  _selectedPort     = AutoPortName;
    [ObservableProperty] private bool    _isConnected;
    [ObservableProperty] private string  _connectLabel     = "Ligar";
    [ObservableProperty] private string  _controllerTypeLabel = "[GRBL  M3/M5]";

    // ── Programa ─────────────────────────────────────────────────────────
    [ObservableProperty] private string  _statusMessage    = "";
    [ObservableProperty] private int     _currentLine;
    [ObservableProperty] private int     _totalLines;
    [ObservableProperty] private double  _progress;
    /// <summary>Reprodução local do programa (lista + 3D) sem enviar à máquina.</summary>
    [ObservableProperty] private bool    _isSimulating;

    /// <summary>Percentagem de velocidade da simulação (definições e binding opcional na UI).</summary>
    [ObservableProperty] private double  _simulationSpeedPercent = 100;

    public event EventHandler<SimulationPoseEventArgs>? SimulationPoseRequested;

    private DispatcherTimer? _simTimer;
    private List<SimSegment>? _simSegments;
    private double            _simTotalLengthMm;
    private int               _simSegI;
    private double            _simU;
    private DateTime          _simLastUtc;

    private const double SimRapidFeedMmMin = 12000;
    private const double SimMinFeedMmMin   = 80;

    private sealed record SimSegment(ToolpathMove Prev, ToolpathMove Curr, Point3D P0, Point3D P1, double LengthMm);

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
        SelectedPort = NormalizePortName(_settings.GetString("port", AutoPortName));
        RefreshPorts();
        LoadMdiHistory();
        UpdateCtrlTypeLabel();

        // Restaurar tipo de máquina guardado
        var saved = _settings.GetMachineType();
        ApplyMachineType(saved, notify: false);
        RefreshSimulationSettings();
    }

    private void RefreshSimulationSettings()
    {
        SimulationSpeedPercent = Math.Clamp(_settings.GetDouble("simulation_speed_percent", 100.0), 10, 400);
    }

    partial void OnIsSimulatingChanged(bool value)
        => ToggleSimulationCommand.NotifyCanExecuteChanged();

    partial void OnTotalLinesChanged(int value)
        => ToggleSimulationCommand.NotifyCanExecuteChanged();

    partial void OnSimulationSpeedPercentChanged(double value)
    {
        try
        {
            _settings.Set("simulation_speed_percent", Math.Clamp(value, 10, 400));
        }
        catch
        {
            /* ignorar falha ao gravar */
        }
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
            try { ConnectUsingSelectedOrAutoPort(baud); }
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
        AvailablePorts.Add(AutoPortName);
        foreach (var p in GrblSerial.ListPorts()) AvailablePorts.Add(p);
        if (!AvailablePorts.Contains(SelectedPort))
            SelectedPort = AutoPortName;
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
        StopSimulationPlayback();
        _ctrl.StartProgram(LoadedProgram);
    }

    [RelayCommand]
    private void FeedHold() => _ctrl.FeedHold();

    [RelayCommand]
    private void AbortProgram()
    {
        StopSimulationPlayback();
        _ctrl.AbortProgram();
    }

    /// <summary>Inicia ou interrompe a simulação visual (sem ligação à máquina).</summary>
    [RelayCommand(CanExecute = nameof(CanToggleSimulation))]
    private void ToggleSimulation()
    {
        if (IsSimulating)
        {
            StopSimulationPlayback();
            StatusMessage = "Simulação interrompida.";
            return;
        }

        if (LoadedProgram?.Toolpath is not { Count: >= 2 } moves) return;

        _simSegments = BuildSimulationSegments(moves);
        if (_simSegments.Count == 0) return;

        _simTotalLengthMm = _simSegments.Sum(s => s.LengthMm);
        if (_simTotalLengthMm < 1e-6)
        {
            _simSegments = null;
            return;
        }

        IsSimulating = true;
        _simSegI = 0;
        _simU    = 0;
        _simLastUtc = DateTime.UtcNow;

        var disp = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _simTimer = new DispatcherTimer(DispatcherPriority.Normal, disp)
        {
            Interval = TimeSpan.FromMilliseconds(16),
        };
        _simTimer.Tick += OnSimulationTick;
        _simTimer.Start();

        StatusMessage = "A simular (interpolação ao longo do percurso — não envia à máquina)…";
        EmitSimulationPose();
    }

    private bool CanToggleSimulation()
        => (LoadedProgram is not null && TotalLines > 0 && LoadedProgram.Toolpath.Count >= 2) || IsSimulating;

    private static List<SimSegment> BuildSimulationSegments(IReadOnlyList<ToolpathMove> moves)
    {
        var list = new List<SimSegment>();
        for (int i = 1; i < moves.Count; i++)
        {
            var prev = moves[i - 1];
            var curr = moves[i];
            var p0 = ToolpathMath.ToWorld(prev.X, prev.A, prev.Z);
            var p1 = ToolpathMath.ToWorld(curr.X, curr.A, curr.Z);
            var len = (p1 - p0).Length;
            list.Add(new SimSegment(prev, curr, p0, p1, len));
        }

        return list;
    }

    private void OnSimulationTick(object? sender, EventArgs e)
    {
        if (LoadedProgram is null || _simSegments is null || _simSegments.Count == 0)
        {
            StopSimulationPlayback();
            return;
        }

        var now = DateTime.UtcNow;
        double dtMs = (now - _simLastUtc).TotalMilliseconds;
        _simLastUtc = now;
        if (dtMs <= 0) dtMs = 16;
        if (dtMs > 200) dtMs = 200;

        double speedMul = Math.Clamp(SimulationSpeedPercent, 10, 400) / 100.0;
        double timeLeftMs = dtMs;

        while (timeLeftMs > 1e-9)
        {
            if (_simSegI >= _simSegments.Count)
            {
                Progress    = 100;
                CurrentLine = TotalLines;
                EmitSimulationPose();
                StopSimulationPlayback();
                StatusMessage = "Simulação concluída.";
                ToggleSimulationCommand.NotifyCanExecuteChanged();
                return;
            }

            var seg = _simSegments[_simSegI];
            double len = seg.LengthMm;
            if (len < 1e-9)
            {
                _simSegI++;
                _simU = 0;
                continue;
            }

            double feedMmMin = seg.Curr.IsRapid
                ? SimRapidFeedMmMin
                : Math.Max(seg.Curr.Feed, SimMinFeedMmMin);
            feedMmMin *= speedMul;
            double mmPerMs = feedMmMin / 60000.0;
            if (mmPerMs < 1e-12)
            {
                _simSegI++;
                _simU = 0;
                continue;
            }

            double remainMm = (1.0 - _simU) * len;
            if (remainMm <= 1e-9)
            {
                _simSegI++;
                _simU = 0;
                continue;
            }

            double timeToExitSeg = remainMm / mmPerMs;
            if (timeToExitSeg <= timeLeftMs)
            {
                timeLeftMs -= timeToExitSeg;
                _simSegI++;
                _simU = 0;
            }
            else
            {
                double distMm = timeLeftMs * mmPerMs;
                _simU += distMm / len;
                timeLeftMs = 0;
            }
        }

        if (_simSegI >= _simSegments.Count)
        {
            Progress    = 100;
            CurrentLine = TotalLines;
            EmitSimulationPose();
            StopSimulationPlayback();
            StatusMessage = "Simulação concluída.";
            ToggleSimulationCommand.NotifyCanExecuteChanged();
            return;
        }

        EmitSimulationPose();
    }

    private void EmitSimulationPose()
    {
        if (_simSegments is null || _simSegments.Count == 0 || LoadedProgram is null) return;

        if (_simSegI >= _simSegments.Count)
        {
            var last = _simSegments[^1];
            var tipEnd = last.P1;
            var aEnd   = last.Curr.A;
            UpdateProgressFromState();
            SimulationPoseRequested?.Invoke(this, new SimulationPoseEventArgs(
                tipEnd, last.P0, aEnd, last.Curr.LineIndex,
                last.Curr.LaserOn && !last.Curr.IsRapid, 1.0));
            return;
        }

        var s = _simSegments[_simSegI];
        double u = Math.Clamp(_simU, 0, 1);
        double x = s.Prev.X + (s.Curr.X - s.Prev.X) * u;
        double z = s.Prev.Z + (s.Curr.Z - s.Prev.Z) * u;
        double aDeg = s.Prev.A + (s.Curr.A - s.Prev.A) * u;
        var tipWorld = ToolpathMath.ToWorld(x, aDeg, z);

        UpdateProgressFromState();

        bool cutting = s.Curr.LaserOn && !s.Curr.IsRapid;
        double p01   = ComputePathProgress01();

        SimulationPoseRequested?.Invoke(this, new SimulationPoseEventArgs(
            tipWorld, s.P0, aDeg, s.Curr.LineIndex, cutting, p01));
    }

    private void UpdateProgressFromState()
    {
        if (_simSegments is null || _simTotalLengthMm < 1e-9) return;

        double acc = 0;
        for (int i = 0; i < _simSegI && i < _simSegments.Count; i++)
            acc += _simSegments[i].LengthMm;
        if (_simSegI < _simSegments.Count)
            acc += _simU * _simSegments[_simSegI].LengthMm;

        Progress = Math.Clamp(acc / _simTotalLengthMm * 100.0, 0, 100);
        if (_simSegI < _simSegments.Count)
            CurrentLine = _simSegments[_simSegI].Curr.LineIndex + 1;
        else
            CurrentLine = TotalLines;
    }

    private double ComputePathProgress01()
    {
        if (_simSegments is null || _simTotalLengthMm < 1e-9) return 0;
        double acc = 0;
        for (int i = 0; i < _simSegI && i < _simSegments.Count; i++)
            acc += _simSegments[i].LengthMm;
        if (_simSegI < _simSegments.Count)
            acc += _simU * _simSegments[_simSegI].LengthMm;
        return Math.Clamp(acc / _simTotalLengthMm, 0, 1);
    }

    private void StopSimulationPlayback()
    {
        if (_simTimer is not null)
        {
            _simTimer.Stop();
            _simTimer.Tick -= OnSimulationTick;
            _simTimer = null;
        }

        _simSegments = null;
        if (IsSimulating)
            IsSimulating = false;
    }

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
        double step = double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v : _settings.GetDouble("jog_step", 1.0);
        double feed = parts.Length >= 3 &&
                      double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var customFeed) &&
                      customFeed > 0
            ? customFeed
            : _settings.GetDouble("jog_feed", 500.0);
        _ctrl.Jog(axis, step, feed);
    }

    [RelayCommand]
    private void JogCancel() => _ctrl.JogCancel();

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

    [RelayCommand]
    private void SetLaserTtlState(bool on)
    {
        LaserTtlOn = on;
        _ctrl.SetLaserTtl(on, MaxSForTorch());
    }

    [RelayCommand]
    private async Task TestLaserFire(object? param)
    {
        int durationMs = param is int ms ? ms : 500;
        durationMs = Math.Clamp(durationMs, 50, 10000);

        bool wasTtlOn = LaserTtlOn;
        double pwmForTest = LaserPwmPct > 0.001
            ? LaserPwmPct
            : Math.Clamp(_settings.GetDouble("laser_default_power", 50), 1, 100);

        if (string.Equals(LaserMode, "PWM", StringComparison.OrdinalIgnoreCase))
            _ctrl.SetLaserPwm(pwmForTest, MaxSForTorch());
        else
            _ctrl.SetLaserTtl(true, MaxSForTorch());

        await Task.Delay(durationMs);
        _ctrl.LaserOff();
        LaserTtlOn = false;

        if (!string.Equals(LaserMode, "PWM", StringComparison.OrdinalIgnoreCase) && wasTtlOn)
            SetLaserTtlState(true);
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

        if (LoadedProgram?.SourceLines is { Count: > 0 })
        {
            StopSimulationPlayback();
            RebuildProgramToolpath(LoadedProgram);
            ToggleSimulationCommand.NotifyCanExecuteChanged();
            ProgramLoaded?.Invoke(this, LoadedProgram);
        }

        if (notify)
            StatusMessage = $"Modo máquina: {MachineName}";
    }

    // ── Programa carregado ────────────────────────────────────────────────

    /// <summary>Pré-processa linhas, reconstrói percurso 3D e limites para simulação (laser/plasma 2D ou torno).</summary>
    private void RebuildProgramToolpath(GCodeProgram program)
    {
        if (program.SourceLines is null || program.SourceLines.Count == 0)
            program.SourceLines = program.Lines.ToList();

        var src = program.SourceLines;
        var torchCodes   = TorchCodesForProgram();
        var spindleCodes = _settings.BuildSpindleCodes();
        program.Lines = GCodePreprocessor.PreprocessForMachine(
            src.ToList(), CurrentMachine, torchCodes, spindleCodes);
        program.LineCount = program.Lines.Count;

        var (torchOn, torchOff) = TorchMNumbersForSimulation();
        program.Toolpath = GCodeParser.BuildToolpath(
            program.Lines, CurrentMachine, torchOn, torchOff);
        program.Bounds = GCodeParser.ComputeBounds(program.Toolpath);
    }

    private (int on, int off) TorchMNumbersForSimulation()
    {
        var tc = TorchCodesForProgram();
        int defOn  = GCodeParser.TryParseMCodeNumber(tc.On,  out var o) ? o : 3;
        int defOff = GCodeParser.TryParseMCodeNumber(tc.Off, out var c) ? c : 5;

        return CurrentMachine switch
        {
            MachineType.TurnLaser => (
                GCodeParser.TryParseMCodeNumber(_settings.GetString("turn_laser_on", "M7"), out var a) ? a : 7,
                GCodeParser.TryParseMCodeNumber(_settings.GetString("turn_laser_off", "M107"), out var b) ? b : 107),
            MachineType.TurnPlasma => (
                GCodeParser.TryParseMCodeNumber(_settings.GetString("turn_plasma_on", "M7"), out var a2) ? a2 : 7,
                GCodeParser.TryParseMCodeNumber(_settings.GetString("turn_plasma_off", "M107"), out var b2) ? b2 : 107),
            _ => (defOn, defOff),
        };
    }

    public void OnProgramLoaded(GCodeProgram program)
    {
        StopSimulationPlayback();
        RebuildProgramToolpath(program);

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
        ToggleSimulationCommand.NotifyCanExecuteChanged();
        ProgramLoaded?.Invoke(this, program);
    }

    // ── Configurações ─────────────────────────────────────────────────────

    public void ApplyNewSettings()
    {
        bool wasConnected = IsConnected;
        int    baud = _settings.GetInt("baud", 115200);

        if (wasConnected) _ctrl.Disconnect();
        DisconnectControllerEvents();
        _ctrl.Dispose();

        _ctrl = CreateController();
        ConnectControllerEvents();
        UpdateCtrlTypeLabel();

        var mt = _settings.GetMachineType();
        ApplyMachineType(mt, notify: false);
        RefreshSimulationSettings();
        SelectedPort = NormalizePortName(_settings.GetString("port", AutoPortName));
        RefreshPorts();

        if (wasConnected)
            try { ConnectUsingSelectedOrAutoPort(baud); } catch { }
    }

    public void SaveSession()
    {
        _settings.Set("mdi_history", MdiHistory.ToList());
    }

    // ── Helpers privados ──────────────────────────────────────────────────

    private IController CreateController()
    {
        var codes = CodesForController();
        var controllerType = _settings.GetString("controller_type", "GRBL");
        return string.Equals(controllerType, "GRBL", StringComparison.OrdinalIgnoreCase)
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

    private void ConnectUsingSelectedOrAutoPort(int baud)
    {
        RefreshPorts();
        string requestedPort = NormalizePortName(SelectedPort);
        var ports = GrblSerial.ListPorts();
        var candidates = new List<string>();
        if (!string.Equals(requestedPort, AutoPortName, StringComparison.OrdinalIgnoreCase))
            candidates.Add(requestedPort);
        candidates.AddRange(ports.Where(p => !string.Equals(p, requestedPort, StringComparison.OrdinalIgnoreCase)));

        if (candidates.Count == 0)
            throw new InvalidOperationException("Nenhuma porta série foi encontrada.");

        Exception? lastError = null;
        foreach (var port in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                _ctrl.Connect(port, baud);
                SelectedPort = port;
                _settings.Set("port", string.Equals(requestedPort, AutoPortName, StringComparison.OrdinalIgnoreCase)
                    ? AutoPortName
                    : port);
                return;
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        throw new InvalidOperationException(
            "Não foi possível ligar automaticamente a nenhuma porta série.",
            lastError);
    }

    private static string NormalizePortName(string? port)
    {
        var value = port?.Trim();
        return string.IsNullOrWhiteSpace(value) ? AutoPortName : value;
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
