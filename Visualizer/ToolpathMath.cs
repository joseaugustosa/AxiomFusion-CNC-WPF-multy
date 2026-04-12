using System.Windows.Media.Media3D;

namespace AxiomFusion.CncController.Visualizer;

/// <summary>Converte coordenadas de máquina (X, A, Z) em ponto 3D para o visualizador.</summary>
public static class ToolpathMath
{
    /// <summary>
    /// Máquina: eixo X = comprimento do tubo, A = rotação em graus, Z = distância ao eixo.
    /// Mundo 3D: X_3d = X_maq, Y_3d = Z_maq * sin(A), Z_3d = Z_maq * cos(A)
    /// </summary>
    public static Point3D ToWorld(double x, double aDeg, double zMachine)
    {
        double aRad = aDeg * Math.PI / 180.0;
        return new Point3D(x,
                           zMachine * Math.Sin(aRad),
                           zMachine * Math.Cos(aRad));
    }
}
