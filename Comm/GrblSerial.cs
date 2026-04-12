using System.IO.Ports;
using System.Text;

namespace AxiomFusion.CncController.Comm;

/// <summary>Wrapper de baixo nível para a porta série (GRBL/ISO).</summary>
public class GrblSerial : IDisposable
{
    private SerialPort? _port;

    public bool IsOpen => _port?.IsOpen == true;

    public bool Open(string portName, int baud = 115200)
    {
        try
        {
            _port = new SerialPort(portName, baud, Parity.None, 8, StopBits.One)
            {
                ReadTimeout  = 50,
                WriteTimeout = 2000,
                Encoding     = Encoding.ASCII,
                NewLine      = "\n",
            };
            _port.Open();
            // Soft reset ao abrir (Ctrl+X)
            SendRealtime([0x18]);
            Thread.Sleep(100);
            // Limpar buffer de entrada
            _port.DiscardInBuffer();
            return true;
        }
        catch
        {
            _port?.Dispose();
            _port = null;
            return false;
        }
    }

    public void Close()
    {
        try { _port?.Close(); } catch { }
        _port?.Dispose();
        _port = null;
    }

    public bool WriteLine(string line)
    {
        if (_port is not { IsOpen: true }) return false;
        try
        {
            var bytes = Encoding.ASCII.GetBytes(line + "\n");
            _port.Write(bytes, 0, bytes.Length);
            return true;
        }
        catch { return false; }
    }

    public string? ReadLine()
    {
        if (_port is not { IsOpen: true }) return null;
        try { return _port.ReadLine().Trim(); }
        catch (TimeoutException) { return null; }
        catch { return null; }
    }

    public void SendRealtime(byte[] bytes)
    {
        if (_port is not { IsOpen: true }) return;
        try { _port.Write(bytes, 0, bytes.Length); _port.BaseStream.Flush(); }
        catch { }
    }

    public static IReadOnlyList<string> ListPorts()
        => SerialPort.GetPortNames().OrderBy(p => p).ToArray();

    public void Dispose() => Close();
}
