using ManifoldSharp;
using ManifoldSharp.Linalg;

// Builds a donut (a "torus") by spinning a circle round the Z axis.
//
// The big idea: like Demo_ChamferedPot, this is a revolve. A donut looks the same from
// every angle, so slice through one side and all you see is a circle. Draw that circle
// off to one side of the centre line, spin it 360°, and it sweeps out the ring.
//
// The slice, seen from the side. (Horizontal = distance from the centre line,
// vertical = height.)
//
//   Z (height)
//   │
//   │             ╭───╮
//   │            │  +  │   ← the tube's cross-section: a circle of radius `tube`,
//   │             ╰───╯       its centre `ring` away from the centre line
//   └──────────────────────── R (distance from the centre line)
//   centre        ↑
//   line        ring
//
// The circle must stay to the right of the centre line (ring > tube). If it crossed it,
// the spun shape would pass through itself and the hole would close up.
class Demo_Donut
{
    public static void Run()
    {
        // ── Stage 1: dimensions (mm) ──────────────────────────────────────────────────
        double ring = 30.0;  // centre line → middle of the dough
        double tube = 12.0;  // thickness of the dough (radius of the slice)

        // Two separate smoothness settings. `tubeSegments` makes the slice round;
        // `ringSegments` makes the ring round. The ring is much longer, so it needs more.
        int tubeSegments = 48;
        int ringSegments = 128;

        // ── Stage 2: draw the slice ───────────────────────────────────────────────────
        // A circle centred on the origin, then slid out sideways by `ring`.
        CrossSection slice = CrossSection.Circle(tube, tubeSegments)
                                         .Translate(new Vec2(ring, 0.0));

        // ── Stage 3: spin it into a solid ─────────────────────────────────────────────
        // Revolve wants the raw outlines, which ToPolygons hands over.
        Manifold donut = Manifold.Revolve(slice.ToPolygons(), ringSegments, 360.0);

        // The slice was centred on Z = 0, so half the donut is below the build plate.
        // Lift it up by the tube radius so it sits on the plate.
        donut = donut.Translate(new Vec3(0.0, 0.0, tube));

        if (donut.Status() != Error.NoError)
        {
            throw new InvalidOperationException(donut.Status().ToStr());
        }

        // ── Stage 4: sanity-check ─────────────────────────────────────────────────────
        // A perfect torus has volume 2π² × ring × tube²:
        //
        //   2 × 9.87 × 30 × 12²  ≈ 85 273 mm³
        //
        // Expect slightly less: the flat facets sit just inside the true curves.
        //
        // Genus counts holes that go right through a shape. A donut is THE example of
        // genus 1.
        double expected = 2.0 * Math.PI * Math.PI * ring * tube * tube;

        Console.WriteLine($"volume    = {donut.Volume()}   (perfect torus: {expected:F0})");
        Console.WriteLine($"area      = {donut.SurfaceArea()}");
        Console.WriteLine($"triangles = {donut.NumTri()}");
        Console.WriteLine($"genus     = {donut.Genus()}   (expect 1)");

        // ── Stage 5: export and view ──────────────────────────────────────────────────
        MeshGL mesh = donut.GetMeshGL(-1);

        StlExporter.Export(mesh, "result.stl");
        StlViewer.Render("result.stl", colour: "#d9a066", embedPlotly: true);
    }
}
