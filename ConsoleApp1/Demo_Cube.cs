using ManifoldSharp;
using ManifoldSharp.Linalg;

class Demo_Cube
{
    public static void Run()
    {
        Manifold cube = Manifold.Cube(new Vec3(1.0, 1.0, 1.0), center: true);

        Console.WriteLine($"volume    = {cube.Volume()}");
        Console.WriteLine($"area      = {cube.SurfaceArea()}");
        Console.WriteLine($"triangles = {cube.NumTri()}");

        MeshGL mesh = cube.GetMeshGL(-1);        // VertProperties / TriVerts, GPU-ready

        // Export and view
        StlExporter.Export(mesh, "result.stl");
        StlViewer.Render("result.stl", colour: "#24ca24");
    }
}

