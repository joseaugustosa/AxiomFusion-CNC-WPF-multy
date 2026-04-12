using AxiomFusion.CncController.Core;

namespace AxiomFusion.CncController.Comm;

/// <summary>Interface comum para todos os backends de controlador (GRBL, ISO...).</summary>
public interface IController : IDisposable
{
    // ── Eventos (equivalentes aos pyqtSignal do Python) ──────────────────
    event EventHandler<bool>             ConnectionChanged;
    event EventHandler<MachineSnapshot>  StatusUpdated;
    event EventHandler<GrblAlarmEventArgs>   AlarmTriggered;
    event EventHandler<GrblErrorEventArgs>   ErrorReceived;
    event EventHandler<int>              LineExecuted;
    event EventHandler                   ProgramFinished;
    event EventHandler<string>           LogMessage;

    // ── Estado ───────────────────────────────────────────────────────────
    bool   IsConnected { get; }
    MCodes MCodes      { get; }

    // ── Ligação ───────────────────────────────────────────────────────────
    void Connect(string port, int baud);
    void Disconnect();

    // ── Controlo manual ───────────────────────────────────────────────────
    void SendMdi(string line);
    void Jog(string axis, double distance, double feed);
    void JogCancel();

    // ── Programa ─────────────────────────────────────────────────────────
    void StartProgram(GCodeProgram program);
    void AbortProgram();

    // ── Máquina ──────────────────────────────────────────────────────────
    void FeedHold();
    void CycleStart();
    void SoftReset();
    void Home();
    void Unlock();

    // ── Offsets ───────────────────────────────────────────────────────────
    void SetWcsZero(int wcsNum, Dictionary<string, double> axes);

    // ── Overrides ─────────────────────────────────────────────────────────
    void SetFeedOverride(int direction);

    // ── Laser ─────────────────────────────────────────────────────────────
    void SetLaserPwm(double pct, int maxS);
    void SetLaserTtl(bool on,   int maxS);
    void LaserOff();

    void UpdateMCodes(MCodes codes);
}

public record GrblAlarmEventArgs(int Code, string Description);
public record GrblErrorEventArgs(int Code, string Description);
