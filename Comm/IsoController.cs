using System.Collections.Concurrent;
using System.Globalization;
using AxiomFusion.CncController.Core;

namespace AxiomFusion.CncController.Comm;

/// <summary>Backend ISO genérico — protocolo handshake (send → wait ok → next).</summary>
public sealed class IsoController : ControllerBase
{
    private readonly GrblSerial _serial = new();
    private readonly ConcurrentQueue<string> _outQ = new();
    private Thread?   _worker;
    private CancellationTokenSource? _cts;
    private ManualResetEventSlim _pauseEv = new(initialState: true);
    private volatile bool _connected;
    private int _lineIndex;

    public IsoController(MCodes codes) : base(codes) { }

    public override bool IsConnected => _connected;

    // ── Ligação ───────────────────────────────────────────────────────────

    public override void Connect(string port, int baud)
    {
        if (_connected) return;
        if (!_serial.Open(port, baud))
            throw new InvalidOperationException($"Não foi possível abrir {port}");

        _cts = new CancellationTokenSource();
        _lineIndex = 0;
        _pauseEv.Set();

        _worker = new Thread(() => WorkerLoop(_cts.Token))
            { IsBackground = true, Name = "IsoWorker" };
        _worker.Start();

        _connected = true;
        _state.SetConnected(true);
        RaiseConnectionChanged(true);
        RaiseLog($"Ligado (ISO) a {port} @ {baud}");
    }

    public override void Disconnect()
    {
        if (!_connected) return;
        _cts?.Cancel();
        _pauseEv.Set();
        _serial.Close();
        _connected = false;
        _state.SetConnected(false);
        RaiseConnectionChanged(false);
        RaiseLog("Desligado");
    }

    // ── Comandos ──────────────────────────────────────────────────────────

    public override void SendMdi(string line)  => _outQ.Enqueue(line.Trim());
    public override void FeedHold()            => _pauseEv.Reset();
    public override void CycleStart()          => _pauseEv.Set();
    public override void SoftReset()           { while (_outQ.TryDequeue(out _)) {} }
    public override void JogCancel()           { while (_outQ.TryDequeue(out _)) {} }

    public override void Jog(string axis, double distance, double feed)
    {
        _outQ.Enqueue("G91");
        _outQ.Enqueue(
            $"G0 {axis.ToUpperInvariant()}{distance.ToString("F3", CultureInfo.InvariantCulture)} F{feed.ToString("F0", CultureInfo.InvariantCulture)}");
        _outQ.Enqueue("G90");
    }

    public override void Home()   => _outQ.Enqueue(_mCodes.Home.Length > 0 ? _mCodes.Home : "G28");
    public override void Unlock() { if (!string.IsNullOrWhiteSpace(_mCodes.Unlock)) _outQ.Enqueue(_mCodes.Unlock); }

    public override void SetFeedOverride(int direction) { /* ISO não tem override realtime */ }

    public override void SetWcsZero(int wcsNum, Dictionary<string, double> axes)
    {
        var axStr = string.Join(" ", axes.Select(kv =>
            $"{kv.Key.ToUpperInvariant()}{kv.Value.ToString("F3", CultureInfo.InvariantCulture)}"));
        _outQ.Enqueue($"G10 L20 P{wcsNum} {axStr}");
    }

    public override void StartProgram(GCodeProgram program)
    {
        _lineIndex = 0;
        while (_outQ.TryDequeue(out _)) { }
        foreach (var raw in program.Lines)
        {
            var clean = raw.Contains(';') ? raw[..raw.IndexOf(';')].Trim() : raw.Trim();
            if (!string.IsNullOrEmpty(clean)) _outQ.Enqueue(clean);
        }
    }

    public override void AbortProgram()
    {
        while (_outQ.TryDequeue(out _)) { }
        RaiseLog("Programa abortado");
    }

    // ── Worker ────────────────────────────────────────────────────────────

    private void WorkerLoop(CancellationToken ct)
    {
        bool waitingOk = false;
        string? lastSent = null;
        var timeout = TimeSpan.FromSeconds(10);
        DateTime sentAt = DateTime.UtcNow;

        while (!ct.IsCancellationRequested && _serial.IsOpen)
        {
            _pauseEv.Wait(100, ct);
            if (ct.IsCancellationRequested) break;

            if (waitingOk)
            {
                var resp = _serial.ReadLine();
                if (resp == null)
                {
                    if (DateTime.UtcNow - sentAt > timeout)
                    {
                        RaiseLog($"Timeout aguardando ok para: {lastSent}");
                        waitingOk = false;
                    }
                    continue;
                }

                if (resp == "ok")
                {
                    RaiseLineExecuted(_lineIndex++);
                    waitingOk = false;
                    // Simular estado
                    _state.SetStatus("Idle");
                    RaiseStatusUpdated();
                }
                else if (resp.StartsWith("error:", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(resp[6..], out int code)) RaiseError(code);
                    waitingOk = false;
                }
                else if (resp.StartsWith("alarm:", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(resp[6..], out int code)) RaiseAlarm(code);
                    waitingOk = false;
                }
            }
            else if (_outQ.TryDequeue(out var line))
            {
                _state.SetStatus("Run");
                RaiseStatusUpdated();
                _serial.WriteLine(line);
                lastSent  = line;
                sentAt    = DateTime.UtcNow;
                waitingOk = true;

                if (_outQ.IsEmpty) RaiseProgramFinished();
            }
        }
    }

    public override void Dispose()
    {
        Disconnect();
        _serial.Dispose();
        base.Dispose();
    }
}
