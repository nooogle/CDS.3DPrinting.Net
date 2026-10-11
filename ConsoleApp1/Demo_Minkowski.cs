using System.Diagnostics;
using ManifoldSharp;
using ManifoldSharp.Linalg;

// What Minkowski sums (and differences) do, shown on an L-shaped bracket.
//
// The big idea: A ⊕ B means "put a copy of B at every point of A, and keep everything any
// copy covers". Think of B as a brush and painting A with it. With a ball as the brush:
//
//   • dilate  (A ⊕ ball)  the shape grows by r; OUTSIDE corners come out rounded,
//                         because the ball swings round them. Inside corners stay sharp.
//   • erode   (A ⊖ ball)  the opposite: keep only the points where the whole ball fits
//                         inside A. The shape shrinks by r.
//
// Combine them and the size comes back, but some corners don't:
//
//   • opening = erode, then dilate   → outside corners rounded, inside corners sharp
//   • closing = dilate, then erode   → inside corners filleted, outside corners sharp
//
// The bracket, seen from the front (it runs 20 mm deep, along Y):
//
//      ┌──┐  ← outside corners (convex): opening rounds these
//      │  │
//      │  │
//      │  └─────────────┐
//      │  ↑ inside      │
//      └──┼─corner──────┘
//         (concave): closing fillets this one
//
// Step through it in DevView: each stage ghosts the original sharp L, so you can see exactly
// where material was added or taken away. Try Cut along Y for the cleanest view.
class Demo_Minkowski
{
    public static void Run()
    {
        // ── Stage 1: dimensions (mm) ──────────────────────────────────────────────────
        double length    = 40.0;  // along X
        double depth     = 20.0;  // along Y
        double height    = 30.0;  // along Z
        double thickness = 6.0;   // of both arms
        double radius    = 2.0;   // the ball brush; keep well under half the thickness, or
                                  // eroding squeezes the arms down to nothing (at exactly
                                  // half they become paper-thin and everything after breaks)
        int    segments  = 24;    // ball smoothness; Minkowski cost grows fast with this

        // ── Stage 2: the sharp L and the brush ────────────────────────────────────────
        // Two boxes joined: a base along X and an upright along Z, overlapping in the corner.
        Manifold bracket = Manifold.Cube(new Vec3(length, depth, thickness), center: false)
                         + Manifold.Cube(new Vec3(thickness, depth, height), center: false);

        // The brush is centred on the origin. That matters: the sum adds the brush's
        // points to the shape's, so an off-centre brush would shift the result as well as
        // grow it.
        Manifold ball = Manifold.Sphere(radius, segments);

        var view = new DevView("Minkowski", colour: "#5b8c5a");
        view.Step("The sharp L", bracket);

        // Show the brush sitting on two points of the surface: the top outside corner and
        // the inside corner. Dilating sweeps it over every surface point like these.
        Manifold brushes = Manifold.Compose(new List<Manifold>
        {
            ball.Translate(new Vec3(0.0, depth / 2.0, height)),
            ball.Translate(new Vec3(thickness, depth / 2.0, thickness)),
        });
        view.Step("The brush: a ball, centred on the surface", bracket, tool: brushes);

        // ── Stage 3: dilate and erode ─────────────────────────────────────────────────
        Manifold grown  = Timed("dilate", () => Dilate(bracket, ball));
        Manifold shrunk = Timed("erode ", () => Erode(bracket, ball));

        view.Step("Dilate (L ⊕ ball): grows by r, outside rounded, inside sharp", grown, ghost: bracket);
        view.Step("Erode (L ⊖ ball): shrinks by r", shrunk, ghost: bracket);

        // ── Stage 4: opening and closing ──────────────────────────────────────────────
        // Each reuses a result from above as its first half.
        Manifold opened = Timed("open  ", () => Dilate(shrunk, ball));
        Manifold closed = Timed("close ", () => Erode(grown, ball));

        view.Step("Opening (erode, then dilate): outside edges rounded", opened, ghost: bracket);
        view.Step("Closing (dilate, then erode): inside corner filleted", closed, ghost: bracket);

        // ── Stage 5: both ─────────────────────────────────────────────────────────────
        // Close, then open: the inside corner gets its fillet AND every outside edge is
        // rounded. A finished-looking bracket from a sharp one, with no idea where its
        // edges are.
        //
        // The last dilate uses the general MinkowskiSum, not the Dilate shortcut. Growing a
        // shape that already has curved (faceted) surfaces, TryDilateByConvex left three
        // zero-volume slivers floating beside the bracket: genus −3 instead of 0. The general
        // method gives one clean solid with the same volume, in about the same time.
        Manifold rounded = Timed("both  ", () => Erode(closed, ball).MinkowskiSum(ball, null, null));
        view.Step("Closing, then opening: rounded all over", rounded, ghost: bracket);

        foreach (Manifold m in new[] { grown, shrunk, opened, closed, rounded })
        {
            if (m.Status() != Error.NoError)
            {
                throw new InvalidOperationException(m.Status().ToStr());
            }
        }

        // ── Stage 6: sanity-check ─────────────────────────────────────────────────────
        // Sizes: dilating adds r on every side, eroding takes it off, and opening and
        // closing land back on the original size.
        //
        // Volume: closing adds just the fillet in the inside corner. In cross-section,
        // that's an r × r square minus a quarter-circle of radius r, run along the depth:
        //   depth × r² × (1 − π/4)  =  20 × 4 × 0.215  ≈  17.2 mm³
        double fillet = depth * radius * radius * (1.0 - Math.PI / 4.0);

        Console.WriteLine();
        Report("sharp L ", bracket);
        Report("dilated ", grown);
        Report("eroded  ", shrunk);
        Report("opened  ", opened);
        Report("closed  ", closed);
        Report("rounded ", rounded);
        Console.WriteLine($"closing added {closed.Volume() - bracket.Volume():F1} mm³   (a perfect fillet: {fillet:F1})");

        // ── Stage 7: export and view ──────────────────────────────────────────────────
        StlExporter.Export(rounded.GetMeshGL(-1), "result.stl");
        view.Show();
    }

    // Grow by a convex brush. TryDilateByConvex is a faster route for exactly this case (a
    // convex brush like a ball); it says false when it can't help, and then the general
    // MinkowskiSum does the job.
    static Manifold Dilate(Manifold shape, Manifold brush) =>
        shape.TryDilateByConvex(brush, null, null, out Manifold result) ? result : shape.MinkowskiSum(brush, null, null);

    // Shrink by a convex brush, the same way round.
    static Manifold Erode(Manifold shape, Manifold brush) =>
        shape.TryErodeByConvex(brush, null, null, out Manifold result) ? result : shape.MinkowskiDifference(brush, null, null);

    // Minkowski on a non-convex shape is one of the slowest operations there is, so time it.
    static Manifold Timed(string name, Func<Manifold> op)
    {
        var clock = Stopwatch.StartNew();
        Manifold result = op();
        Console.WriteLine($"{name}  {clock.ElapsedMilliseconds,6} ms   {result.NumTri(),6} triangles");
        return result;
    }

    static void Report(string name, Manifold m)
    {
        Box b = m.BoundingBox();
        Console.WriteLine($"{name}  size {b.Max.X - b.Min.X,5:F1} × {b.Max.Y - b.Min.Y,5:F1} × {b.Max.Z - b.Min.Z,5:F1}" +
                          $"   volume {m.Volume(),8:F1}   genus {m.Genus()}");
    }
}
