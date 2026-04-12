using System;
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

        DispatcherUnhandledException += (_, ex) =>
        {
            var inner = ex.Exception.InnerException;
            var text  = inner is not null
                ? $"{ex.Exception.Message}\n\nDetalhe: {inner.Message}"
                : ex.Exception.Message;
            MessageBox.Show(text, "Erro Inesperado",
                MessageBoxButton.OK, MessageBoxImage.Error);
            ex.Handled = true;
        };

        try
        {
            var main = new MainWindow();
            MainWindow = main;
            main.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), "Erro ao abrir a janela principal",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
