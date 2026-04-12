using AxiomFusion.CncController.Core;

namespace AxiomFusion.CncController.Comm;

/// <summary>Base com helpers de invocação de eventos (thread-safe para a UI).</summary>
public abstract class ControllerBase : IController
{
    protected MCodes _mCodes;
    protected readonly MachineState _state = new();

    protected ControllerBase(MCodes mCodes) => _mCodes = mCodes;

    // ── Eventos ───────────────────────────────────────────────────────────
    public event EventHandler<bool>?            ConnectionChanged;
    public event EventHandler<MachineSnapshot>? StatusUpdated;
    public event EventHandler<GrblAlarmEventArgs>?  AlarmTriggered;
    public event EventHandler<GrblErrorEventArgs>?  ErrorReceived;
    public event EventHandler<int>?             LineExecuted;
    public event EventHandler?                  ProgramFinished;
    public event EventHandler<string>?          LogMessage;

    // ── Interface pública ─────────────────────────────────────────────────
    public abstract bool   IsConnected { get; }
    public          MCodes MCodes      => _mCodes;

    public abstract void Connect(string port, int baud);
    public abstract void Disconnect();
    public abstract void SendMdi(string line);
    public abstract void Jog(string axis, double distance, double feed);
    public abstract void JogCancel();
    public abstract void StartProgram(GCodeProgram program);
    public abstract void AbortProgram();
    public abstract void FeedHold();
    public abstract void CycleStart();
    public abstract void SoftReset();
    public abstract void Home();
    public abstract void Unlock();
    public abstract void SetFeedOverride(int direction);
    public abstract void SetWcsZero(int wcsNum, Dictionary<string, double> axes);

    public virtual void SetLaserPwm(double pct, int maxS)
    {
        int s = (int)(pct / 100.0 * maxS);
        SendMdi($"{_mCodes.On} S{s}");
    }

    public virtual void SetLaserTtl(bool on, int maxS)
    {
        SendMdi(on ? $"{_mCodes.On} S{maxS}" : _mCodes.Off);
    }

    public virtual void LaserOff() => SendMdi(_mCodes.Off);

    public void UpdateMCodes(MCodes codes) => _mCodes = codes;

    // ── Helpers para disparar eventos na thread da UI ─────────────────────
    protected void RaiseConnectionChanged(bool v)
        => App.Dispatch(() => ConnectionChanged?.Invoke(this, v));
    protected void RaiseStatusUpdated()
        => App.Dispatch(() => StatusUpdated?.Invoke(this, _state.GetSnapshot()));
    protected void RaiseAlarm(int code)
        => App.Dispatch(() => AlarmTriggered?.Invoke(this,
               new GrblAlarmEventArgs(code, GrblConstants.GetAlarm(code))));
    protected void RaiseError(int code)
        => App.Dispatch(() => ErrorReceived?.Invoke(this,
               new GrblErrorEventArgs(code, GrblConstants.GetError(code))));
    protected void RaiseLineExecuted(int idx)
        => App.Dispatch(() => LineExecuted?.Invoke(this, idx));
    protected void RaiseProgramFinished()
        => App.Dispatch(() => ProgramFinished?.Invoke(this, EventArgs.Empty));
    protected void RaiseLog(string msg)
        => App.Dispatch(() => LogMessage?.Invoke(this, msg));

    public virtual void Dispose() { }
}
