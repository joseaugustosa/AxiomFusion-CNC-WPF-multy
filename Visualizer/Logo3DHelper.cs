using System.IO;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;

namespace AxiomFusion.CncController.Visualizer;

/// <summary>Carrega o OBJ da marca, centra, escala e posiciona no canto da vista.</summary>
public static class Logo3DHelper
{
    public const string DefaultObjFileName = "marca-axiom-sofwere-CNC.obj";

    /// <returns>Modelo agrupado pronto para <see cref="ModelVisual3D.Content"/>, ou null.</returns>
    public static Model3DGroup? TryLoadFromFile(string objFullPath, double targetSize, Point3D anchorOffset)
    {
        if (!File.Exists(objFullPath)) return null;

        Model3D? raw;
        try
        {
            var reader = new ObjReader();
            raw = reader.Read(objFullPath);
        }
        catch
        {
            return null;
        }

        if (raw is null) return null;

        var bounds = CombineBounds(raw);
        if (bounds.SizeX <= 1e-9 && bounds.SizeY <= 1e-9 && bounds.SizeZ <= 1e-9)
            return null;

        var cx = bounds.X + bounds.SizeX * 0.5;
        var cy = bounds.Y + bounds.SizeY * 0.5;
        var cz = bounds.Z + bounds.SizeZ * 0.5;
        var maxD = Math.Max(bounds.SizeX, Math.Max(bounds.SizeY, bounds.SizeZ));
        var s = targetSize / maxD;

        var m = new Matrix3D();
        m.Translate(new Vector3D(-cx, -cy, -cz));
        m.Scale(new Vector3D(s, s, s));
        m.Translate(new Vector3D(anchorOffset.X, anchorOffset.Y, anchorOffset.Z));

        var root = new Model3DGroup();
        root.Children.Add(raw);
        root.Transform = new MatrixTransform3D(m);
        return root;
    }

    private static Rect3D CombineBounds(Model3D model)
    {
        var rect = Rect3D.Empty;
        var any = false;

        void Visit(Model3D? node)
        {
            switch (node)
            {
                case GeometryModel3D gm when gm.Geometry is MeshGeometry3D mesh:
                {
                    var xf = gm.Transform ?? Transform3D.Identity;
                    foreach (Point3D p in mesh.Positions)
                    {
                        var q = xf.Transform(p);
                        if (!any)
                        {
                            rect = new Rect3D(q, new Size3D(1e-9, 1e-9, 1e-9));
                            any = true;
                        }
                        else rect.Union(q);
                    }
                    break;
                }
                case Model3DGroup g:
                    foreach (var c in g.Children)
                        Visit(c);
                    break;
            }
        }

        Visit(model);
        return rect;
    }
}
