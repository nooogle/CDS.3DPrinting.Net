using ManifoldSharp;

/// <summary>Writes a <see cref="MeshGL"/> to a binary STL file.</summary>
public static class StlExporter
{
    public static void Export(MeshGL mesh, string path)
    {
        using var stream = File.Create(path);
        Export(mesh, stream);
    }

    public static void Export(MeshGL mesh, Stream stream)
    {
        int numProp = (int)mesh.NumProp;
        var props = mesh.VertProperties;
        var tris = mesh.TriVerts;
        int numTri = tris.Count / 3;

        using var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true);
        writer.Write(new byte[80]);          // header
        writer.Write((uint)numTri);

        float[] p = new float[9];
        for (int t = 0; t < numTri; t++)
        {
            for (int c = 0; c < 3; c++)
            {
                int v = (int)tris[t * 3 + c];
                for (int k = 0; k < 3; k++)
                {
                    p[c * 3 + k] = props[v * numProp + k];
                }
            }

            // Facet normal from the CCW winding: (b - a) x (c - a), normalised.
            float ux = p[3] - p[0], uy = p[4] - p[1], uz = p[5] - p[2];
            float vx = p[6] - p[0], vy = p[7] - p[1], vz = p[8] - p[2];
            float nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
            float len = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
            if (len > 0) { nx /= len; ny /= len; nz /= len; }

            writer.Write(nx); writer.Write(ny); writer.Write(nz);
            foreach (float f in p) writer.Write(f);
            writer.Write((ushort)0);         // attribute byte count
        }
    }
}
