using ManifoldSharp;
using ManifoldSharp.Linalg;

// The pot from Demo_Pot, with rows of holes punched through its wall.
//
// The big idea: make ONE hole-shaped "cutter", then copy it into a pattern by moving and
// rotating it. Gather all the copies into a single shape, and subtract that from the pot
// in one go.
//
// Placing one cutter (looking down from above):
//
//   1. a cylinder, centred      2. tipped on its side      3. slid out to the wall,
//      on the origin, standing     (now points along X)       then swung round Z by
//      upright (along Z)                                      the hole's angle
//
//            ┌┐                                                ╭─────╮
//            ││                       ═══                     ╱   ═══ ← rotated copy
//            └┘                                              │       ═══ ← first copy
//                                                             ╲       ╱
//                                                              ╰─────╯
class Demo_PotWithHoles
{
    public static void Run()
    {
        // ── Stage 1: the plain pot (exactly as in Demo_Pot) ───────────────────────────
        double radius    = 50.0;
        double height    = 30.0;
        double wall      = 2.0;
        double floor     = 3.0;
        double overshoot = 1.0;
        int    segments  = 128;

        Manifold outer = Manifold.Cylinder(height, radius, -1, segments);
        Manifold inner = Manifold.Cylinder(height - floor + overshoot, radius - wall, -1, segments)
                                 .Translate(new Vec3(0.0, 0.0, floor));
        Manifold pot = outer - inner;

        // ── Stage 2: describe the pattern ─────────────────────────────────────────────
        double holeRadius  = 3.0;   // 6 mm holes
        int    holesPerRow = 8;    // evenly spaced around the pot
        int    rows        = 2;
        double lowestRow   = 12.0;  // Z of the bottom row's centre
        double rowSpacing  = 10.0;  // vertical gap between row centres

        // Sides on each hole cutter. 32 makes round holes. Try 4: you get diamond-shaped
        // holes, which print cleanly with no supports, because no edge leans further than
        // 45° from vertical.
        int holeSides = 32;

        // ── Stage 3: make one cutter ──────────────────────────────────────────────────
        // It has to pass right through the wall, so make it longer than the wall is thick.
        // That's the same overshoot trick as the pot's rim. wall × 3 sticks out 2 mm each
        // side, yet is far too short to reach across and hit the opposite wall.
        double cutterLength = wall * 3.0;

        // CylinderCentered(..., center: true) puts the middle of the cylinder on the
        // origin rather than its base, which makes it easy to centre within the wall later.
        Manifold cutter = Manifold.CylinderCentered(cutterLength, holeRadius, -1, holeSides, center: true);

        // Tip it on its side: 90° about the Y axis swings "up" (Z) round to point along X,
        // i.e. outwards through the wall.
        cutter = cutter.Rotate(0, 90, 0);

        // Slide it out along X so its middle sits in the middle of the wall.
        double wallMiddle = radius - wall / 2.0;
        cutter = cutter.Translate(new Vec3(wallMiddle, 0.0, 0.0));

        // ── Stage 4: copy the cutter into the pattern ─────────────────────────────────
        // For every hole, take the cutter (still sitting at Z = 0, pointing along X),
        // lift it to its row's height, then spin it round the Z axis to its place.
        //
        // Order matters! Rotating around Z spins about the pot's centre line, which is
        // exactly what we want. If we'd rotated BEFORE sliding it out to the wall, it would
        // just spin on the spot.
        //
        // Because Manifold is immutable, `cutter` itself never changes. Each Translate/
        // Rotate makes a fresh copy, so the same cutter can be reused for every hole.
        double degreesBetweenHoles = 360.0 / holesPerRow;
        var cutters = new List<Manifold>();

        for (int row = 0; row < rows; row++)
        {
            double z = lowestRow + row * rowSpacing;

            // Stagger every other row by half a step, like bricks, which keeps the
            // wall stronger than lining the holes up in columns.
            double stagger = (row % 2 == 1) ? degreesBetweenHoles / 2.0 : 0.0;

            for (int i = 0; i < holesPerRow; i++)
            {
                double angle = i * degreesBetweenHoles + stagger;

                Manifold hole = cutter
                    .Translate(new Vec3(0.0, 0.0, z))
                    .Rotate(0, 0, angle);

                cutters.Add(hole);
            }
        }

        // ── Stage 5: subtract all the holes at once ───────────────────────────────────
        // We could do `pot = pot - hole` 48 times, but every boolean re-processes the whole
        // pot. It's faster to join all the cutters into one shape first (BatchBoolean with
        // Add = union), then make a single cut.
        Manifold allCutters = Manifold.BatchBoolean(cutters, OpType.Add);
        pot = pot - allCutters;

        if (pot.Status() != Error.NoError)
        {
            throw new InvalidOperationException(pot.Status().ToStr());
        }

        // ── Stage 6: sanity-check the numbers ─────────────────────────────────────────
        // Genus is the best check here. Every hole that goes right through the wall adds
        // one to the genus, so 4 rows × 12 holes should give genus = 48.
        // Fewer would mean some cutters didn't fully pierce the wall.
        //
        // Volume: each hole removes roughly π × r² × wall = π × 3² × 2 ≈ 56.5 mm³,
        // so 48 holes take out ~2 714 mm³ from the plain pot's ~30 524 mm³ → ~27 810 mm³.
        Console.WriteLine($"volume    = {pot.Volume()}");
        Console.WriteLine($"area      = {pot.SurfaceArea()}");
        Console.WriteLine($"triangles = {pot.NumTri()}");
        Console.WriteLine($"genus     = {pot.Genus()}   (expect {rows * holesPerRow})");

        // ── Stage 7: export and view ──────────────────────────────────────────────────
        MeshGL mesh = pot.GetMeshGL(-1);

        StlExporter.Export(mesh, "result.stl");
        StlViewer.Render("result.stl", colour: "#c4622d");
    }
}
