using ManifoldSharp;
using ManifoldSharp.Linalg;

//Demo_Cube.Run();
//Demo_Pyramid.Run();
//Demo_Pot.Run();
//Demo_PotWithHoles.Run();
//Demo_ChamferedPot.Run();
//Demo_Donut.Run();
//Demo_RoundedBox.Run();
Demo_Minkowski.Run();


// Manifold cube = Manifold.Cube(new Vec3(1.0, 1.0, 1.0), center: true);
// Manifold sphere = Manifold.Sphere(radius: 0.5, circularSegments: 256);

// // Move the sphere up along the Z-axis to intersect the cube's top face
// sphere = sphere.Translate(new Vec3(0.0, 0.0, 0.5));

// Manifold result = cube - sphere;

// // Errors are a status on the result, never an exception: a failed operation
// // comes back as empty geometry carrying the reason.
// if (result.Status() != Error.NoError)
// {
//     throw new InvalidOperationException(result.Status().ToStr());
// }

// Console.WriteLine($"volume = {result.Volume()}");
// Console.WriteLine($"area   = {result.SurfaceArea()}");
// Console.WriteLine($"tris   = {result.NumTri()}");

// MeshGL mesh = result.GetMeshGL(-1);        // VertProperties / TriVerts, GPU-ready

// // Export to an STL file (binary format)
// string stlPath = args.Length > 0 ? args[0] : "result.stl";
// StlExporter.Export(mesh, stlPath);
// Console.WriteLine($"wrote  = {Path.GetFullPath(stlPath)}");

// // Turn the STL into an interactive page and open it in the browser.
// StlViewer.Render(stlPath, colour: "#d42a24");
