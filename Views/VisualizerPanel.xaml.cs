using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using AxiomFusion.CncController.Core;
using AxiomFusion.CncController.Visualizer;

namespace AxiomFusion.CncController.Views;

public partial class VisualizerPanel : UserControl
{
    private GCodeProgram?           _program;
    private List<ToolpathMove>?     _moves;
    private List<Point3D[]>         _rapidSegs  = [];
    private List<Point3D[]>         _travelSegs = [];
    private List<Point3D[]>         _cutSegs    = [];

    public VisualizerPanel() => InitializeComponent();

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

    public void HighlightLine(int lineIndex)
    {
        if (_moves is null) return;
        var hits = _moves.Where(m => m.LineIndex == lineIndex).ToList();
        if (hits.Count == 0) return;

        var pts = new Point3DCollection();
        foreach (var m in hits)
        {
            var p = ToolpathMath.ToWorld(m.X, m.A, m.Z);
            pts.Add(p); pts.Add(p); // Ponto → segmento de comprimento 0 → destaque
        }
        PathCurrent.Points = pts;
    }

    // ── Controlos de câmara ───────────────────────────────────────────────

    private void BtnFit_Click(object sender, RoutedEventArgs e) => FitView();

    private void FitView()
    {
        Viewport.ZoomExtents(animationTime: 500);
    }

    private void BtnTube_Click(object sender, RoutedEventArgs e)
    {
        TubeVisual.Visible = BtnTube.IsChecked == true;
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
