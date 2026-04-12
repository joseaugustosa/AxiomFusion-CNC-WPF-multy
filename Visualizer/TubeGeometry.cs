namespace AxiomFusion.CncController.Visualizer;

/// <summary>Gera malha de tubo rectangular (prisma) para o visualizador 3D.</summary>
public static class TubeGeometry
{
    /// <summary>
    /// Devolve (vertices float[N*6], indices int[M]) — formato pos(xyz) + normal(xyz).
    /// O tubo estende-se ao longo do eixo X de 0 até <paramref name="length"/>.
    /// A secção é W × H centrada em Y=0, Z=0.
    /// </summary>
    public static (float[] Vertices, int[] Indices) GenerateMesh(
        double W, double H, double length)
    {
        float w = (float)(W / 2.0);
        float h = (float)(H / 2.0);
        float L = (float)length;

        // 4 cantos da secção: (y, z)
        (float y, float z)[] corners = [(-w, -h), (w, -h), (w, h), (-w, h)];
        // Normais de cada face (outward)
        (float ny, float nz)[] normals = [(0, -1), (1, 0), (0, 1), (-1, 0)];

        var verts   = new List<float>();
        var indices = new List<int>();
        int vBase   = 0;

        // 4 faces laterais
        for (int f = 0; f < 4; f++)
        {
            int n = (f + 1) % 4;
            var (y0, z0) = corners[f];
            var (y1, z1) = corners[n];
            var (ny, nz) = normals[f];

            // 4 vértices: (x=0,y0,z0), (x=L,y0,z0), (x=0,y1,z1), (x=L,y1,z1)
            verts.AddRange([0, y0, z0, 0, ny, nz]);
            verts.AddRange([L, y0, z0, 0, ny, nz]);
            verts.AddRange([L, y1, z1, 0, ny, nz]);
            verts.AddRange([0, y1, z1, 0, ny, nz]);

            indices.AddRange([vBase, vBase+1, vBase+2, vBase, vBase+2, vBase+3]);
            vBase += 4;
        }

        // Tampa traseira (x=0, normal -X)
        AddCap(verts, indices, ref vBase, 0f, -1f, corners);
        // Tampa dianteira (x=L, normal +X)
        AddCap(verts, indices, ref vBase, L, +1f, corners);

        return ([.. verts], [.. indices]);
    }

    private static void AddCap(List<float> verts, List<int> indices, ref int vBase,
                                float x, float nx, (float y, float z)[] corners)
    {
        // Centro
        verts.AddRange([x, 0, 0, nx, 0, 0]);
        int center = vBase++;

        // Cantos
        var order = nx > 0 ? corners : corners.Reverse().ToArray();
        foreach (var (y, z) in order)
        {
            verts.AddRange([x, y, z, nx, 0, 0]);
            vBase++;
        }

        // Triângulos em fan
        for (int i = 0; i < 4; i++)
        {
            indices.Add(center);
            indices.Add(center + 1 + i);
            indices.Add(center + 1 + (i + 1) % 4);
        }
    }
}
