using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using MediaColor = System.Windows.Media.Color;
using AxiomFusion.CncController.Core;
using AxiomFusion.CncController.Dialogs;
using AxiomFusion.CncController.Visualizer;

namespace AxiomFusion.CncController.Views;

public partial class VisualizerPanel : UserControl
{
    private GCodeProgram?           _program;
    private List<ToolpathMove>?     _moves;
    private List<Point3D[]>         _rapidSegs  = [];
    private List<Point3D[]>         _travelSegs = [];
    private List<Point3D[]>         _cutSegs    = [];

    /// <summary>Rotação tipo moeda no plano (eixo Z — como a rolar na mesa).</summary>
    private readonly AxisAngleRotation3D _logoRotation =
        new(new Vector3D(0, 0, 1), 0);

    private Model3DGroup? _logoModelContent;

    /// <summary>Velocidade contínua em graus/segundo (persistida em settings).</summary>
    private double _logoSpinDegPerSec = 36.0;

    private long _logoSpinLastTimestamp;

    private SettingsManager? _settings;

    /// <summary>Definido pela janela principal para persistir a velocidade do logo em <c>config/settings.json</c>.</summary>
    public SettingsManager? Settings
    {
        get => _settings;
        set
        {
            _settings = value;
            ApplyLogoSpinFromSettings();
            ApplyWatermarkFromSettings();
        }
    }

    /// <summary>Lê <c>viewport_watermark_opacity</c> (0–1) e aplica à marca de água.</summary>
    public void ApplyWatermarkFromSettings()
    {
        var o = 0.09;
        if (_settings is not null)
        {
            o = _settings.GetDouble("viewport_watermark_opacity", 0.09);
            if (double.IsNaN(o) || double.IsInfinity(o)) o = 0.09;
        }

        o = Math.Clamp(o, 0, 1);
        WatermarkViewbox.Opacity = o;
    }

    private void ApplyLogoSpinFromSettings()
    {
        var degPerSec = 36.0;
        if (_settings is not null)
        {
            degPerSec = _settings.GetDouble("logo_spin_deg_per_sec", 36.0);
            if (double.IsNaN(degPerSec) || double.IsInfinity(degPerSec))
                degPerSec = 36.0;
        }

        _logoSpinDegPerSec = Math.Clamp(degPerSec, 0, 120);
    }

    public VisualizerPanel()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyLogoSpinFromSettings();
        ApplyWatermarkFromSettings();
        TryInitLogo3D();
        CompositionTarget.Rendering += OnLogoSpinRendering;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        CompositionTarget.Rendering -= OnLogoSpinRendering;
        _logoSpinLastTimestamp = 0;
    }

    /// <summary>Rotação sincronizada com o refresh do ecrã; Δt real evita soluços do timer fixo.</summary>
    private void OnLogoSpinRendering(object? sender, EventArgs e)
    {
        if (_logoModelContent is null) return;
        if (LogoOverlay.Visibility != Visibility.Visible) return;
        if (LogoModelVisual.Content is null) return;
        if (_logoSpinDegPerSec < 1e-6) return;

        var t = Stopwatch.GetTimestamp();
        if (_logoSpinLastTimestamp == 0)
        {
            _logoSpinLastTimestamp = t;
            return;
        }

        var dt = (t - _logoSpinLastTimestamp) / (double)Stopwatch.Frequency;
        _logoSpinLastTimestamp = t;
        if (dt <= 0) return;
        // Depois de pausa no debugger ou 1º frame após largo intervalo
        if (dt > 0.1) dt = 0.1;

        _logoRotation.Angle = (_logoRotation.Angle + _logoSpinDegPerSec * dt) % 360.0;
    }

    private void TryInitLogo3D()
    {
        var dir  = Path.Combine(AppContext.BaseDirectory, "Assets", "Brand");
        var path = Path.Combine(dir, Logo3DHelper.DefaultObjFileName);
        // Centrado na origem; coluna esquerda + câmara fixa encaixam o símbolo na vista
        var group = Logo3DHelper.TryLoadFromFile(path, targetSize: 28.5,
            anchorOffset: new Point3D(0, 0, 0));
        if (group is null)
        {
            _logoModelContent = null;
            LogoModelVisual.Content = null;
            BtnLogo.IsChecked    = false;
            TbLogoLabel.Text     = "Logo 3D: —";
            BtnLogo.IsEnabled    = false;
            LogoOverlay.Visibility = Visibility.Collapsed;
            return;
        }

        _logoModelContent         = group;
        LogoModelVisual.Content   = BtnLogo.IsChecked == true ? group : null;
        LogoModelVisual.Transform = new RotateTransform3D(_logoRotation);
        RefitLogoCamera();
    }

    private void LogoViewport_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is HelixViewport3D vp)
        {
            vp.IsRotationEnabled = false;
            vp.IsPanEnabled      = false;
            vp.IsZoomEnabled     = false;
        }
    }

    private void LogoViewport_SizeChanged(object sender, SizeChangedEventArgs e)
        => RefitLogoCamera();

    private void LogoHitArea_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_logoModelContent is null || LogoOverlay.Visibility != Visibility.Visible)
            return;
        e.Handled = true;

        var owner = Window.GetWindow(this) ?? Application.Current?.MainWindow;
        var degPerSec = _logoSpinDegPerSec;
        var dlg = new LogoSpinSettingsDialog(degPerSec);
        // CenterOwner sem Owner definido faz o WPF lançar ao abrir o diálogo modal.
        if (owner is not null)
        {
            dlg.Owner = owner;
            dlg.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        bool? result;
        try
        {
            result = dlg.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Rotação do logo",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (result != true) return;

        var next = dlg.DegreesPerSecond;
        if (double.IsNaN(next) || double.IsInfinity(next))
            next = 36.0;
        next = Math.Clamp(next, 0, 120);
        _logoSpinDegPerSec = next;
        _logoSpinLastTimestamp = 0;
        try
        {
            _settings?.Set("logo_spin_deg_per_sec", next);
        }
        catch
        {
            /* falha ao gravar config — velocidade na memória mantém-se */
        }
    }

    private void RefitLogoCamera()
    {
        if (_logoModelContent is null || LogoViewport is null) return;
        if (LogoModelVisual.Content is null) return;
        try
        {
            // Caixa nos eixos mundiais oscila com a rotação em Z; inflar evita cortar nas pontas.
            var bounds = Visual3DHelper.FindBounds(LogoModelVisual, LogoModelVisual.Transform);
            if (bounds.IsEmpty)
            {
                LogoViewport.ZoomExtents(0);
                return;
            }

            const double xyFactor = 1.58;
            const double zFactor  = 1.15;
            var cx = bounds.X + bounds.SizeX * 0.5;
            var cy = bounds.Y + bounds.SizeY * 0.5;
            var cz = bounds.Z + bounds.SizeZ * 0.5;
            var sx = bounds.SizeX * xyFactor;
            var sy = bounds.SizeY * xyFactor;
            var sz = bounds.SizeZ * zFactor;
            var inflated = new Rect3D(
                cx - sx * 0.5,
                cy - sy * 0.5,
                cz - sz * 0.5,
                sx, sy, sz);
            LogoViewport.ZoomExtents(inflated, 0);
        }
        catch
        {
            try { LogoViewport.ZoomExtents(0); } catch { /* viewport ainda a inicializar */ }
        }
    }

    // ── Carregar programa ─────────────────────────────────────────────────

    public void LoadProgram(GCodeProgram program, double tubeW, double tubeH)
    {
        _program = program;
        _moves   = program.Toolpath;

        // Gerar malha do tubo
        double length = program.Bounds.XMax - program.Bounds.XMin + 40;
        var (verts, tris) = TubeGeometry.GenerateMesh(tubeW, tubeH, length);
        TubeVisual.MeshGeometry = BuildMesh(verts, tris);

        // Construir segmentos por categoria
        _rapidSegs  = [];
        _travelSegs = [];
        _cutSegs    = [];

        for (int i = 1; i < _moves.Count; i++)
        {
            var prev = _moves[i - 1];
            var curr = _moves[i];
            var p0 = ToolpathMath.ToWorld(prev.X, prev.A, prev.Z);
            var p1 = ToolpathMath.ToWorld(curr.X, curr.A, curr.Z);

            if (curr.IsRapid)         _rapidSegs.Add([p0, p1]);
            else if (!curr.LaserOn)   _travelSegs.Add([p0, p1]);
            else                      _cutSegs.Add([p0, p1]);
        }

        RebuildLines();
        HideLaserNozzle();
        FitView();
    }

    private void RebuildLines()
    {
        PathRapid.Points  = ToPointCollection(_rapidSegs);
        PathTravel.Points = ToPointCollection(_travelSegs);
        PathCut.Points    = ToPointCollection(_cutSegs);
        PathCurrent.Points = [];
    }

    // ── Highlight de linha ────────────────────────────────────────────────

    /// <param name="showLaserNozzle">Durante simulação: mostra bico + feixe no 3D.</param>
    public void HighlightLine(int lineIndex, bool showLaserNozzle = false)
    {
        if (_moves is null) return;
        var hits = _moves.Where(m => m.LineIndex == lineIndex).ToList();
        if (hits.Count == 0)
        {
            PathCurrent.Points = [];
            if (showLaserNozzle)
                HideLaserNozzle();
            return;
        }

        var pts = new Point3DCollection();
        foreach (var m in hits)
        {
            var p = ToolpathMath.ToWorld(m.X, m.A, m.Z);
            pts.Add(p); pts.Add(p); // Ponto → segmento de comprimento 0 → destaque
        }
        PathCurrent.Points = pts;

        if (!showLaserNozzle)
        {
            HideLaserNozzle();
            return;
        }

        var last = hits[^1];
        var tip  = ToolpathMath.ToWorld(last.X, last.A, last.Z);
        UpdateLaserNozzle(tip, last.A, last);
    }

    /// <summary>Simulação: bico move-se ao longo do segmento; linha activa do P0 do segmento até à ponta.</summary>
    public void UpdateSimulationPose(Point3D tipWorld, Point3D segmentStartWorld, double aDeg, bool cutting)
    {
        PathCurrent.Points = new Point3DCollection { segmentStartWorld, tipWorld };
        UpdateLaserNozzle(tipWorld, aDeg, cutting);
    }

    /// <summary>Esconde o bico/feixe (fim da simulação ou destaque só da linha).</summary>
    public void HideLaserNozzle()
    {
        LaserNozzleSphere.Visible = false;
        LaserNozzleBeam.Visible   = false;
    }

    private void UpdateLaserNozzle(Point3D tipWorld, double aDeg, ToolpathMove move)
        => UpdateLaserNozzle(tipWorld, aDeg, move.LaserOn && !move.IsRapid);

    /// <summary>Orientação: eixo do feixe = -Z máquina (radial para o eixo do tubo); bico fora ao longo de +Z máquina.</summary>
    private void UpdateLaserNozzle(Point3D tipWorld, double aDeg, bool cutting)
    {
        var radialOut = new Vector3D(0, tipWorld.Y, tipWorld.Z);
        if (radialOut.LengthSquared < 1e-10)
            radialOut = ToolpathMath.MachineZOutwardWorld(aDeg);
        else
            radialOut.Normalize();

        const double nozzleOffset = 11.0;
        var nozzlePos = tipWorld + radialOut * nozzleOffset;

        LaserNozzleSphere.Center = nozzlePos;
        LaserNozzleBeam.Point1   = nozzlePos;
        LaserNozzleBeam.Point2   = tipWorld;

        var hot  = MediaColor.FromRgb(255, 110, 70);
        var cool = MediaColor.FromRgb(150, 200, 255);
        var c    = cutting ? hot : cool;

        LaserNozzleSphere.Fill = new SolidColorBrush(c);
        LaserNozzleBeam.Fill   = new SolidColorBrush(MediaColor.FromRgb(
            (byte)Math.Clamp(c.R + 25, 0, 255),
            (byte)Math.Clamp(c.G + 15, 0, 255),
            (byte)Math.Clamp(c.B + 10, 0, 255)));

        LaserNozzleSphere.Visible = true;
        LaserNozzleBeam.Visible   = true;
    }

    // ── Controlos de câmara ───────────────────────────────────────────────

    private void BtnFit_Click(object sender, RoutedEventArgs e) => FitView();

    private void FitView()
    {
        Viewport.ZoomExtents(animationTime: 500);
    }

    private void BtnTube_Click(object sender, RoutedEventArgs e)
    {
        var on = BtnTube.IsChecked == true;
        TubeVisual.Visible = on;
        TbTubeLabel.Text   = on ? "Tubo: ON" : "Tubo: OFF";
    }

    private void BtnLogo_Click(object sender, RoutedEventArgs e)
    {
        if (!BtnLogo.IsEnabled) return;
        var on = BtnLogo.IsChecked == true;
        LogoModelVisual.Content = on ? _logoModelContent : null;
        TbLogoLabel.Text        = on ? "Logo 3D: ON" : "Logo 3D: OFF";
        LogoOverlay.Visibility  = on ? Visibility.Visible : Visibility.Collapsed;
        if (on)
            _logoSpinLastTimestamp = 0;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static Point3DCollection ToPointCollection(List<Point3D[]> segs)
    {
        var pts = new Point3DCollection(segs.Count * 2);
        foreach (var seg in segs) { pts.Add(seg[0]); pts.Add(seg[1]); }
        return pts;
    }

    private static MeshGeometry3D BuildMesh(float[] verts, int[] tris)
    {
        var positions = new Point3DCollection();
        var normals   = new Vector3DCollection();
        var indices   = new Int32Collection(tris);

        for (int i = 0; i < verts.Length; i += 6)
        {
            positions.Add(new Point3D(verts[i], verts[i+1], verts[i+2]));
            normals.Add(new Vector3D(verts[i+3], verts[i+4], verts[i+5]));
        }

        return new MeshGeometry3D
        {
            Positions       = positions,
            Normals         = normals,
            TriangleIndices = indices,
        };
    }
}
