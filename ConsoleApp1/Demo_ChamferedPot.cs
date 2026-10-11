using ManifoldSharp;
using ManifoldSharp.Linalg;

// The pot with holes, now with chamfered edges, built a different way.
//
// The big idea: a pot looks the same from every angle, so it can be described by a single
// 2D outline — a slice through one side of the wall — spun 360° round the Z axis. That is
// "revolving" (think of a potter's wheel, or a lathe). The chamfers are then just corners
// cut off that 2D outline, which is far easier than chamfering edges in 3D.
//
// The outline, as a slice through the right-hand side of the pot.
// (Horizontal = distance from the centre line, vertical = height. Not to scale.)
//
//   Z (height)
//   │
//   │          ① rim chamfers, inside and outside
//   │            7 ┌──┐ 4
//   │           8 ╱    ╲ 5
//   │            │      │
//   │            │      │      ③ ← the wall
//   │            │      │
//   │         9 ╱       │      ② inside corner: a 45° "fillet" of extra plastic
//   │ 11──────10        │
//   │ │  ← floor        │ 3
//   │ │                ╱       ④ bottom edge chamfer (hides "elephant's foot")
//   │ 1─────────────── 2
//   └─────────────────────── R (distance from the centre line)
//   centre
//
// The holes get chamfered too, by changing the shape of the cutter — see Stage 4.
class Demo_ChamferedPot
{
    public static void Run()
    {
        // ── Stage 1: dimensions (mm) ──────────────────────────────────────────────────
        double radius   = 30.0;
        double height   = 50.0;
        double wall     = 2.0;
        double floor    = 3.0;
        int    segments = 128;

        // Chamfer sizes. Each is the distance cut back along both sides of the corner,
        // which makes a 45° slope.
        double bottomChamfer    = 1.0;  // big enough to hide elephant's foot
        double rimChamfer       = 0.5;  // inside AND outside; keep 2 × this < wall
        double floorWallChamfer = 1.0;  // adds plastic in the inside corner: stronger, easy to clean
        double holeChamfer      = 0.5;  // countersink depth on each side of every hole

        // ── Stage 2: draw the 2D outline ──────────────────────────────────────────────
        // Each point is (distance from centre, height). Go round the outline
        // anticlockwise. Revolve uses the direction to tell inside from outside, and
        // anticlockwise means "this area is solid".
        //
        // The numbers in the comments match the diagram above. Notice how each chamfer
        // replaces ONE square corner with TWO points, each pulled back from the corner
        // by the chamfer size.
        double r = radius;
        double innerR = radius - wall;

        var outline = new List<Vec2>
        {
            new Vec2(0.0,                       0.0),                       //  1 centre, on the build plate
            new Vec2(r - bottomChamfer,         0.0),                       //  2 ┐ bottom outer corner,
            new Vec2(r,                         bottomChamfer),             //  3 ┘ chamfered
            new Vec2(r,                         height - rimChamfer),       //  4 ┐ rim outside corner,
            new Vec2(r - rimChamfer,            height),                    //  5 ┘ chamfered
            new Vec2(innerR + rimChamfer,       height),                    //  7 ┐ rim inside corner,
            new Vec2(innerR,                    height - rimChamfer),       //  8 ┘ chamfered
            new Vec2(innerR,                    floor + floorWallChamfer),  //  9 ┐ floor/wall inside corner:
            new Vec2(innerR - floorWallChamfer, floor),                     // 10 ┘ a 45° fillet
            new Vec2(0.0,                       floor),                     // 11 centre, top of the floor
        };

        // ── Stage 3: spin it into a solid ─────────────────────────────────────────────
        // Revolve takes a LIST of outlines (so you could add holes inside one), hence the
        // extra brackets. 360 = a full turn.
        //
        // This one call replaces Demo_Pot's "outer cylinder − inner cylinder". The floor,
        // the hollow and every chamfer all come straight from the outline.
        Manifold pot = Manifold.Revolve(new List<List<Vec2>> { outline }, segments, 360.0);

        // Capture the stages to step through afterwards (see DevView). A 2D outline isn't a
        // solid, so to show it we extrude it into a thin plate and stand it up in the XZ
        // plane (rotating 90° about X turns its Y into height), right where Revolve spins it.
        var view = new DevView("Chamfered pot", colour: "#c4622d");
        Manifold plate = Manifold.Extrude(new List<List<Vec2>> { outline }, 0.5, 0, 0.0, new Vec2(1.0, 1.0))
                                 .Rotate(90, 0, 0)
                                 .Translate(new Vec3(0.0, 0.25, 0.0));
        view.Step("The 2D outline (shown as a thin plate)", plate);
        view.Step("Revolved 360° round the Z axis", pot);

        // ── Stage 4: a countersunk hole cutter ────────────────────────────────────────
        // Same pattern as Demo_PotWithHoles, but the cutter is now three pieces joined
        // together: a straight bore plus a cone at each end. Each cone flares out at 45° to
        // put a chamfer on the hole edge. Built standing up (along Z) first, then tipped
        // over, exactly as before:
        //
        //       outside of pot (+Z)
        //          ╲          ╱    ← outer cone: radius grows by holeChamfer
        //           │        │       at the wall surface
        //           │  bore  │
        //           │        │
        //          ╱          ╲    ← inner cone (the outer one, flipped)
        //       inside of pot (−Z)
        //
        double holeRadius  = 3.0;
        int    holesPerRow = 8;
        int    rows        = 2;
        double lowestRow   = 12.0;
        double rowSpacing  = 10.0;
        int    holeSides   = 32;
        double overshoot   = 1.0;

        // The straight bore, centred on the origin, as before.
        Manifold bore = Manifold.CylinderCentered(wall * 3.0, holeRadius, -1, holeSides, center: true);

        // The outer cone. Its narrow end sits holeChamfer inside the wall surface (at
        // wall/2 − holeChamfer); it widens at 45° (radius grows 1 mm per 1 mm of height),
        // and carries on past the surface by `overshoot` so it cuts cleanly.
        double coneHeight = holeChamfer + overshoot;
        Manifold outerCone = Manifold.Cylinder(coneHeight, holeRadius, holeRadius + coneHeight, holeSides)
                                     .Translate(new Vec3(0.0, 0.0, wall / 2.0 - holeChamfer));

        // The inner cone is a mirror image: flipping 180° about X turns +Z into −Z.
        Manifold innerCone = outerCone.Rotate(180, 0, 0);

        // Join the three pieces, then place the cutter in the wall as before.
        Manifold cutter = (bore + outerCone + innerCone)
            .Rotate(0, 90, 0)
            .Translate(new Vec3(radius - wall / 2.0, 0.0, 0.0));

        // Show the cutter being built up, as a red tool sitting where the first hole goes.
        Manifold InFirstHole(Manifold piece) =>
            piece.Rotate(0, 90, 0).Translate(new Vec3(radius - wall / 2.0, 0.0, lowestRow));

        view.Step("Hole cutter: a straight bore", pot, tool: InFirstHole(bore));
        view.Step("+ a 45° cone on the outside", pot, tool: InFirstHole(bore + outerCone));
        view.Step("+ the same cone, mirrored, inside", pot, tool: InFirstHole(bore + outerCone + innerCone));

        // ── Stage 5: pattern the cutter and subtract (unchanged) ──────────────────────
        double degreesBetweenHoles = 360.0 / holesPerRow;
        var cutters = new List<Manifold>();

        for (int row = 0; row < rows; row++)
        {
            double z = lowestRow + row * rowSpacing;
            double stagger = (row % 2 == 1) ? degreesBetweenHoles / 2.0 : 0.0;

            for (int i = 0; i < holesPerRow; i++)
            {
                double angle = i * degreesBetweenHoles + stagger;
                cutters.Add(cutter.Translate(new Vec3(0.0, 0.0, z)).Rotate(0, 0, angle));
            }
        }

        Manifold allCutters = Manifold.BatchBoolean(cutters, OpType.Add);
        view.Step($"Copied round: {rows} staggered rows of {holesPerRow}", pot, tool: allCutters);

        pot = pot - allCutters;
        view.Step("Subtracted: chamfered holes", pot);

        if (pot.Status() != Error.NoError)
        {
            throw new InvalidOperationException(pot.Status().ToStr());
        }

        // ── Stage 6: sanity-check ─────────────────────────────────────────────────────
        // Genus should still be one per hole. Chamfers change the edges, not the
        // topology. The size should still be 100 × 100 × 30: chamfers only trim corners.
        Console.WriteLine($"volume    = {pot.Volume()}");
        Console.WriteLine($"area      = {pot.SurfaceArea()}");
        Console.WriteLine($"triangles = {pot.NumTri()}");
        Console.WriteLine($"genus     = {pot.Genus()}   (expect {rows * holesPerRow})");

        // ── Stage 7: export and view ──────────────────────────────────────────────────
        MeshGL mesh = pot.GetMeshGL(-1);

        StlExporter.Export(mesh, "result.stl");
        view.Show();
    }
}
