using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using AxiomFusion.CncController.Core;
using AxiomFusion.CncController.ViewModels;

namespace AxiomFusion.CncController.Views;

public partial class GCodePanel : UserControl
{
    private int _lastHighlight = -1;

    public GCodePanel() => InitializeComponent();

    // ── Carregamento de ficheiro ──────────────────────────────────────────

    private void BtnLoad_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "G-code|*.nc;*.gcode;*.txt;*.tap|Todos|*.*",
            Title  = "Abrir ficheiro G-code"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var parser  = new GCodeParser();
            var program = parser.LoadFile(dlg.FileName);
            LoadProgram(program);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Erro ao carregar ficheiro",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LoadProgram(GCodeProgram program)
    {
        LbLines.Items.Clear();
        _lastHighlight = -1;

        for (int i = 0; i < program.Lines.Count; i++)
            LbLines.Items.Add($"{i+1,5}  {program.Lines[i]}");

        TbFilename.Text = $"{System.IO.Path.GetFileName(program.Filepath)}  ({program.LineCount} linhas)";

        if (DataContext is MainViewModel vm)
            vm.OnProgramLoaded(program);
    }

    // ── Highlight de linha ────────────────────────────────────────────────

    public void HighlightLine(int index)
    {
        if (index < 0 || index >= LbLines.Items.Count) return;

        // Reset anterior
        if (_lastHighlight >= 0 && _lastHighlight < LbLines.Items.Count)
            SetItemBackground(_lastHighlight, Brushes.Transparent);

        SetItemBackground(index, new SolidColorBrush(Color.FromRgb(0x2a, 0x35, 0x50)));
        LbLines.ScrollIntoView(LbLines.Items[index]);
        _lastHighlight = index;
    }

    private void SetItemBackground(int index, Brush brush)
    {
        var container = LbLines.ItemContainerGenerator
            .ContainerFromIndex(index) as ListBoxItem;
        if (container != null) container.Background = brush;
    }

    public void OnProgramFinished()
    {
        if (_lastHighlight >= 0 && _lastHighlight < LbLines.Items.Count)
            SetItemBackground(_lastHighlight, Brushes.Transparent);
        _lastHighlight = -1;
    }
}
