using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using AxiomFusion.CncController.Core;

namespace AxiomFusion.CncController.Comm;

/// <summary>Backend GRBL com streaming por contagem de caracteres.</summary>
public sealed class GrblController : ControllerBase
{
    // Bytes realtime
    private static readonly byte[] RT_FeedHold    = [0x21];  // !
    private static readonly byte[] RT_CycleStart  = [0x7E];  // ~
    private static readonly byte[] RT_SoftReset   = [0x18];  // Ctrl-X
    private static readonly byte[] RT_StatusQuery = [0x3F];  // ?
    private static readonly byte[] RT_JogCancel   = [0x85];
    private static readonly byte[] RT_FeedOvPlus  = [0x91];
    private static readonly byte[] RT_FeedOvMinus = [0x92];
    private static readonly byte[] RT_FeedOv100   = [0x90];

    private readonly GrblSerial _serial = new();
    private readonly ConcurrentQueue<string> _outQ = new();
    private readonly ConcurrentQueue<byte[]> _rtQ  = new();
    private Thread?        _worker;
    private CancellationTokenSource? _cts;
    private volatile bool  _connected;
    private int            _lineIndex;

    // Contagem de buffer (algoritmo GRBL character-counting)
    private readonly Queue<int> _sentBuf = new();
    private int _charsInBuf;
    private const int GrblBufSize = 128;

    public GrblController(MCodes codes) : base(codes) { }

    public override bool IsConnected => _connected;

    // ── Ligação ───────────────────────────────────────────────────────────

    public override void Connect(string port, int baud)
    {
        if (_connected) return;
        if (!_serial.Open(port, baud))
            throw new InvalidOperationException($"Não foi possível abrir {port}");

        _cts    = new CancellationTokenSource();
        _lineIndex = 0;
        _charsInBuf = 0;
        _sentBuf.Clear();
        while (_outQ.TryDequeue(out _)) { }
        while (_rtQ.TryDequeue(out _))  { }

        _worker = new Thread(() => WorkerLoop(_cts.Token))
            { IsBackground = true, Name = "GrblWorker" };
        _worker.Start();

        _connected = true;
        _state.SetConnected(true);
        RaiseConnectionChanged(true);
        RaiseLog($"Ligado a {port} @ {baud}");
    }

    public override void Disconnect()
    {
        if (!_connected) return;
        _cts?.Cancel();
        _serial.Close();
        _connected = false;
        _state.SetConnected(false);
        RaiseConnectionChanged(false);
        RaiseLog("Desligado");
    }

    // ── Comandos ──────────────────────────────────────────────────────────

    public override void SendMdi(string line)  => _outQ.Enqueue(line.Trim());
    public override void FeedHold()            => _rtQ.Enqueue(RT_FeedHold);
    public override void CycleStart()          => _rtQ.Enqueue(RT_CycleStart);
    public override void SoftReset()           => _rtQ.Enqueue(RT_SoftReset);
    public override void JogCancel()           => _rtQ.Enqueue(RT_JogCancel);

    public override void Jog(string axis, double distance, double feed)
        => _outQ.Enqueue(
            $"$J=G91 G21 {axis.ToUpperInvariant()}{distance.ToString("F3", CultureInfo.InvariantCulture)} F{feed.ToString("F0", CultureInfo.InvariantCulture)}");

    public override void Home()   => _outQ.Enqueue(_mCodes.Home);
    public override void Unlock() => _outQ.Enqueue(_mCodes.Unlock);

    public override void SetFeedOverride(int direction)
    {
        if      (direction > 0)  _rtQ.Enqueue(RT_FeedOvPlus);
        else if (direction < 0)  _rtQ.Enqueue(RT_FeedOvMinus);
        else                     _rtQ.Enqueue(RT_FeedOv100);
    }

    public override void SetWcsZero(int wcsNum, Dictionary<string, double> axes)
    {
        var axStr = string.Join(" ", axes.Select(kv =>
            $"{kv.Key.ToUpperInvariant()}{kv.Value.ToString("F3", CultureInfo.InvariantCulture)}"));
        SendMdi($"G10 L20 P{wcsNum} {axStr}");
    }

    public override void StartProgram(GCodeProgram program)
    {
        _lineIndex = 0;
        while (_outQ.TryDequeue(out _)) { }
        foreach (var raw in program.Lines)
        {
            var clean = raw.Contains(';') ? raw[..raw.IndexOf(';')].Trim() : raw.Trim();
            if (!string.IsNullOrEmpty(clean))
                _outQ.Enqueue(clean);
        }
        RaiseLog($"A iniciar: {program.LineCount} linhas");
    }

    public override void AbortProgram()
    {
        while (_outQ.TryDequeue(out _)) { }
        _rtQ.Enqueue(RT_SoftReset);
        RaiseLog("Programa abortado");
    }

    // ── Worker thread ─────────────────────────────────────────────────────

    private void WorkerLoop(CancellationToken ct)
    {
        var lastStatus = DateTime.UtcNow;

        while (!ct.IsCancellationRequested && _serial.IsOpen)
        {
            // 1. Realtime
            while (_rtQ.TryDequeue(out var rb))
                _serial.SendRealtime(rb);

            // 2. Poll status a cada ~100ms
            if ((DateTime.UtcNow - lastStatus).TotalMilliseconds >= 100)
            {
                _serial.SendRealtime(RT_StatusQuery);
                lastStatus = DateTime.UtcNow;
            }

            // 3. Streaming com contagem de buffer
            while (_outQ.TryPeek(out var line))
            {
                int needed = line.Length + 1;
                if (_charsInBuf + needed > GrblBufSize) break;

                _outQ.TryDequeue(out _);
                _serial.WriteLine(line);
                _sentBuf.Enqueue(needed);
                _charsInBuf += needed;
            }

            // 4. Ler resposta
            var resp = _serial.ReadLine();
            if (resp == null) continue;

            ParseResponse(resp);
        }
    }

    private void ParseResponse(string resp)
    {
        if (resp.StartsWith('<') && resp.EndsWith('>'))
        {
            _state.UpdateFromStatus(resp);
            RaiseStatusUpdated();
        }
        else if (resp == "ok")
        {
            if (_sentBuf.TryDequeue(out var n)) _charsInBuf -= n;
            int idx = _lineIndex++;
            RaiseLineExecuted(idx);

            if (_outQ.IsEmpty && _sentBuf.Count == 0)
                RaiseProgramFinished();
        }
        else if (resp.StartsWith("error:", StringComparison.OrdinalIgnoreCase))
        {
            if (_sentBuf.TryDequeue(out var n)) _charsInBuf -= n;
            if (int.TryParse(resp[6..], out int code))
            {
                _state.SetError(code);
                RaiseError(code);
            }
        }
        else if (resp.StartsWith("alarm:", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(resp[6..], out int code))
            {
                _state.SetAlarm(code);
                _state.SetStatus("Alarm");
                RaiseAlarm(code);
            }
        }
        else if (resp.StartsWith("[MSG:", StringComparison.OrdinalIgnoreCase))
        {
            RaiseLog(resp[5..^1]);
        }
    }

    public override void Dispose()
    {
        Disconnect();
        _serial.Dispose();
        base.Dispose();
    }
}
