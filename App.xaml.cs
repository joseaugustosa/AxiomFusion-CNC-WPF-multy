using System.Windows;
using System.Windows.Threading;

namespace AxiomFusion.CncController;

public partial class App : Application
{
    /// <summary>
    /// Executa uma acção na thread da UI de qualquer thread de fundo.
    /// Equivalente ao Qt Signals emitidos a partir de worker threads.
    /// </summary>
    public static void Dispatch(Action action)
    {
        var app = Current;
        if (app is null) return;
        if (app.Dispatcher.CheckAccess())
            action();
        else
            app.Dispatcher.BeginInvoke(action, DispatcherPriority.Normal);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Handler global de excepções não tratadas
        DispatcherUnhandledException += (_, ex) =>
        {
            MessageBox.Show(ex.Exception.Message, "Erro Inesperado",
                MessageBoxButton.OK, MessageBoxImage.Error);
            ex.Handled = true;
        };
    }
}
