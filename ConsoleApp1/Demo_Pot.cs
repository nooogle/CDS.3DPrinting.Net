using ManifoldSharp;
using ManifoldSharp.Linalg;

// Builds a simple round pot: a solid cylinder with a slightly smaller cylinder cut out of it.
//
// The big idea: this is "constructive solid geometry" (CSG). Instead of describing the
// pot's surface directly, we combine simple solids with booleans — here a difference:
//
//     pot = outer − inner
//
// Seen from the side, cut in half:
//
//            ┊       ┊   ← inner pokes out the top (the "overshoot")
//        ┌───┼───────┼───┐
//        │   │       │   │
//        │ ▓ │ inner │ ▓ │   ▓ = wall that's left behind
//        │ ▓ │       │ ▓ │
//        │ ▓ └───────┘ ▓ │   ← inner starts `floor` above the bottom...
//        │ ▓▓▓▓▓▓▓▓▓▓▓▓▓ │   ← ...so this slab stays behind as the floor
//        └───────────────┘   Z = 0 (build plate)
//
class Demo_Pot
{
    public static void Run()
    {
        // ── Stage 1: choose the dimensions (mm) ───────────────────────────────────────
        double radius   = 30.0;  // outside radius → 60 mm across
        double height   = 50.0;  // outside height
        double wall     = 2.0;   // side wall thickness
        double floor    = 5.0;   // bottom thickness

        // How many flat sides approximate the circle. A 3D printer can't print a true
        // curve from an STL anyway; everything is triangles. 128 sides on a 60 mm pot
        // makes each facet ~1.5 mm wide, which looks perfectly round once printed.
        int segments = 128;

        // ── Stage 2: the outer solid ──────────────────────────────────────────────────
        // Cylinder(height, radiusLow, radiusHigh, segments) stands on Z = 0 and is centred
        // on the Z axis. radiusHigh = -1 means "same as the bottom", i.e. straight sides.
        // (Make radiusHigh bigger than radiusLow and you get a tapered flower pot.)
        Manifold outer = Manifold.Cylinder(height, radius, -1, segments);

        // ── Stage 3: the inner solid — the hollow we'll remove ────────────────────────
        // Smaller by `wall` all round, so the sides end up `wall` thick.
        //
        // Its height is the clever bit. It must:
        //   • start `floor` above the bottom, so a floor is left behind, and
        //   • end ABOVE the rim, so the top is cut fully open.
        //
        // Why not end it exactly at the rim? Then the inner and outer top faces would sit in
        // exactly the same plane, and the boolean has to decide whether a paper-thin skin
        // survives there. Overshooting by 1 mm removes the question entirely. This
        // "make the cutter a bit bigger than it needs to be" trick is used all the time
        // in CAD.
        double overshoot   = 1.0;
        double innerHeight = height - floor + overshoot;

        Manifold inner = Manifold.Cylinder(innerHeight, radius - wall, -1, segments);

        // Cylinder always starts at Z = 0, so lift it up by the floor thickness.
        // Remember: Translate returns a NEW shape — keep it by assigning it back.
        inner = inner.Translate(new Vec3(0.0, 0.0, floor));

        // ── Stage 4: subtract ─────────────────────────────────────────────────────────
        // The `-` operator is a boolean difference: everything in `outer` that isn't also
        // in `inner`. (`+` would be union, `^` intersection.)
        Manifold pot = outer - inner;

        // ── Stage 5: check it worked ──────────────────────────────────────────────────
        if (pot.Status() != Error.NoError)
        {
            throw new InvalidOperationException(pot.Status().ToStr());
        }

        // ── Stage 6: sanity-check the numbers ─────────────────────────────────────────
        // A perfect cylinder's volume is π r² h, so the pot should hold roughly:
        //
        //   outer      = π × 30² × 50         ≈ 141 372 mm³
        //   hollow     = π × 28² × 48         ≈ 118 224 mm³   (only the part inside the pot counts)
        //   pot (wall) = outer − hollow        ≈  23 148 mm³   ≈ 23 cm³ of plastic
        //
        // Expect slightly less (~23 138): the 128 flat sides sit just inside the true circle.
        //
        // Genus counts holes that go right THROUGH a shape (a doughnut has genus 1). A pot
        // is just a dented solid, so it should be 0. If you ever see 1, the floor is
        // missing and you've made a tube.
        Console.WriteLine($"volume    = {pot.Volume()}");
        Console.WriteLine($"area      = {pot.SurfaceArea()}");
        Console.WriteLine($"triangles = {pot.NumTri()}");
        Console.WriteLine($"genus     = {pot.Genus()}");

        // ── Stage 7: export and view ──────────────────────────────────────────────────
        MeshGL mesh = pot.GetMeshGL(-1);

        StlExporter.Export(mesh, "result.stl");
        StlViewer.Render("result.stl", colour: "#c4622d");
    }
}
