using ManifoldSharp;
using ManifoldSharp.Linalg;

// Builds a square-based pyramid, checks it, and writes it out as an STL you can slice and print.
//
// The big idea: a pyramid is the "convex hull" of five points — the four corners of the
// base plus the apex. Imagine stretching a rubber skin tightly around those five points;
// the shape the skin makes is the hull. Manifold.Hull does exactly that, so all we have
// to do is say where the corners are.
class Demo_Pyramid
{
    public static void Run()
    {
        // ── Stage 1: choose the dimensions ────────────────────────────────────────────
        // STL files have no units, but slicers (Cura, PrusaSlicer, Bambu Studio…) assume
        // millimetres. So these numbers are mm: a 20 mm wide base, 15 mm tall.
        double baseSize = 20.0;
        double height   = 15.0;

        // Half the base size is handy: it puts the base centred on the origin, so the
        // corners sit at ±half in X and Y.
        double half = baseSize / 2.0;

        // ── Stage 2: list the five corner points ──────────────────────────────────────
        // Coordinates are (X, Y, Z). X is left/right, Y is front/back, Z is up.
        // The base lies flat on Z = 0, which is where a printer's build plate is.
        //
        //  Looking down from above (the apex is in the middle, above the page):
        //
        //      (-h, +h) ●───────● (+h, +h)
        //               │       │
        //               │   ▲   │        ▲ = apex at (0, 0, height)
        //               │       │
        //      (-h, -h) ●───────● (+h, -h)
        //
        var points = new List<Vec3>
        {
            new Vec3(-half, -half, 0.0),   // base: front-left
            new Vec3(+half, -half, 0.0),   // base: front-right
            new Vec3(+half, +half, 0.0),   // base: back-right
            new Vec3(-half, +half, 0.0),   // base: back-left
            new Vec3(0.0,   0.0,   height) // apex: straight up from the centre
        };

        // ── Stage 3: wrap a solid around the points ───────────────────────────────────
        // Hull finds the smallest convex solid containing every point and builds its
        // surface out of triangles. We never say which points join to which — Hull
        // works that out. (Order in the list doesn't matter either.)
        //
        // Note: a Manifold is immutable. Operations like Translate, Rotate and Hull never
        // change an existing shape; they hand back a new one, which you must keep:
        //     shape = shape.Translate(...);   // ✔ keeps the moved copy
        //     shape.Translate(...);           // ✘ the moved copy is thrown away
        Manifold pyramid = Manifold.Hull(points);

        // ── Stage 4: check it worked ──────────────────────────────────────────────────
        // Manifold doesn't throw exceptions for bad geometry. A failed operation returns
        // an empty shape carrying an error status, so check before going further.
        if (pyramid.Status() != Error.NoError)
        {
            throw new InvalidOperationException(pyramid.Status().ToStr());
        }

        // ── Stage 5: sanity-check the numbers ─────────────────────────────────────────
        // Comparing against hand-worked answers is a good habit; it catches mistakes
        // like a wrong coordinate long before you've wasted filament.
        //
        //   Volume = base area × height ÷ 3      = 20 × 20 × 15 ÷ 3            = 2000 mm³
        //   Area   = base + 4 triangular sides
        //          slant height = √(15² + 10²)   ≈ 18.03 mm
        //          = 20² + 4 × (½ × 20 × 18.03) ≈ 400 + 721.1                 ≈ 1121.1 mm²
        //   Triangles = 2 (square base split in two) + 4 (one per side)       = 6
        Console.WriteLine($"volume    = {pyramid.Volume()}");
        Console.WriteLine($"area      = {pyramid.SurfaceArea()}");
        Console.WriteLine($"triangles = {pyramid.NumTri()}");

        // ── Stage 6: turn it into a plain triangle mesh ───────────────────────────────
        // Internally Manifold keeps extra bookkeeping (which triangles are neighbours, and
        // so on). GetMeshGL flattens that into simple vertex and triangle lists — the
        // shape STL files (and GPUs) want. -1 means "use the default normal channel".
        MeshGL mesh = pyramid.GetMeshGL(-1);

        // ── Stage 7: export and view ──────────────────────────────────────────────────
        // result.stl is the file to load into your slicer; the viewer opens an
        // interactive 3D preview in your browser so you can spin it around first.
        StlExporter.Export(mesh, "result.stl");
        StlViewer.Render("result.stl", colour: "#e0b040");
    }
}
