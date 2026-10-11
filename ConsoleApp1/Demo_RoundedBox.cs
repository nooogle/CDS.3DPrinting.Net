using ManifoldSharp;
using ManifoldSharp.Linalg;

// Builds a box with every edge and corner rounded to a chosen radius.
//
// The big idea: we never make a sharp box and round it off. Instead we put a ball in each
// of the 8 corners and wrap a tight skin round them with Hull (the rubber-skin trick from
// Demo_Pyramid, but around balls instead of points). The skin goes flat between the balls
// and curves over them, so the rounded box comes out directly. No cube, no subtraction.
//
// Each ball is pulled IN from the corner by its own radius, so it just touches the three
// faces that meet there. That keeps the finished box exactly the size we asked for.
//
// One corner, seen from the side:
//
//      sharp corner we want to avoid
//      ┌─────────────
//      │   ╭───╮
//      │  │  ●  │    ● = ball centre, `r` in from each face
//      │   ╰───╯     the ball touches both faces but doesn't poke out
//      │
//
// To see HOW the skin builds the shape, the demo captures the in-between stages with
// DevView, which you can step through in the browser:
//
//   ① sharp box     ② 8 balls     ③ hull of 2    ④ hull of 4    ⑤ hull of 8
//     (for            (nothing      = one          = one          = the rounded
//     comparison)     joined yet)   rounded edge   rounded slab   box
class Demo_RoundedBox
{
    public static void Run()
    {
        // ── Stage 1: dimensions (mm) ──────────────────────────────────────────────────
        // Different sizes on each axis, to show this isn't just for cubes.
        Vec3   size     = new Vec3(30.0, 20.0, 15.0);
        double radius   = 4.0;   // edge rounding; must be no more than half the smallest side
        int    segments = 48;    // smoothness of each ball, and so of the rounded edges

        // ── Stage 2: one ball in each corner ──────────────────────────────────────────
        // The box runs from 0 to size on each axis. Each ball centre sits `radius` in from
        // the faces, so the centres are at either `radius` or `size − radius` on each axis.
        // Two choices on three axes gives 2 × 2 × 2 = 8 balls.
        double[] xs = { radius, size.X - radius };
        double[] ys = { radius, size.Y - radius };
        double[] zs = { radius, size.Z - radius };

        Manifold ball = Manifold.Sphere(radius, segments);

        // Listed bottom layer first, then the top: front-left, front-right, back-left, back-right.
        var balls = new List<Manifold>();
        foreach (double z in zs)
            foreach (double y in ys)
                foreach (double x in xs)
                    balls.Add(ball.Translate(new Vec3(x, y, z)));

        // ── Stage 3: wrap the skin round them ─────────────────────────────────────────
        // HullManifolds is Hull for shapes instead of points: it takes every vertex of every
        // ball and finds the tightest convex skin round the lot.
        Manifold box = Manifold.HullManifolds(balls);

        if (box.Status() != Error.NoError)
        {
            throw new InvalidOperationException(box.Status().ToStr());
        }

        // ── Stage 4: sanity-check ─────────────────────────────────────────────────────
        // The bounding box should be exactly the size we asked for: the balls touch the
        // faces but never poke out.
        //
        // Volume: picture the rounded box taken apart. With a, b, c = the sides minus 2r
        // (the box the ball centres sit on):
        //   • a solid core               a × b × c
        //   • six flat slabs, r thick    2r × (ab + ac + bc)
        //   • twelve quarter-cylinders   which add up to 3 whole cylinders: πr² × (a + b + c)
        //   • eight corner eighths       which add up to 1 whole ball:      4/3 πr³
        // Expect slightly less: the faceted balls sit just inside the true curves.
        double a = size.X - 2 * radius, b = size.Y - 2 * radius, c = size.Z - 2 * radius;
        double expected = a * b * c
                        + 2 * radius * (a * b + a * c + b * c)
                        + Math.PI * radius * radius * (a + b + c)
                        + 4.0 / 3.0 * Math.PI * radius * radius * radius;

        Box bounds = box.BoundingBox();
        Vec3 extent = new Vec3(bounds.Max.X - bounds.Min.X, bounds.Max.Y - bounds.Min.Y, bounds.Max.Z - bounds.Min.Z);

        Console.WriteLine($"size      = {extent.X:F3} × {extent.Y:F3} × {extent.Z:F3}   (asked for {size.X} × {size.Y} × {size.Z})");
        Console.WriteLine($"volume    = {box.Volume():F0}   (perfect rounded box: {expected:F0}, sharp box: {size.X * size.Y * size.Z:F0})");
        Console.WriteLine($"triangles = {box.NumTri()}");
        Console.WriteLine($"genus     = {box.Genus()}   (expect 0)");

        // The printable part on its own.
        StlExporter.Export(box.GetMeshGL(-1), "result.stl");

        // ── Stage 5: step through how it's built ──────────────────────────────────────
        // Each stage is built from the same balls, so you can watch the skin fill in. Each
        // stage shows the one before it as a faint ghost:
        //   ② the balls, with the sharp box ghosted round them: each one tucked in a corner
        //   ③ two balls along one edge → the skin between them is a rounded edge (a capsule)
        //   ④ the four bottom balls    → edges all round, plus a flat face across the middle
        //   ⑤ all eight                → the finished box
        //   ⑥ the sharp box as a red "tool" over it: the red corners are what never got made
        //
        // Compose (not +) bundles the balls into one shape without a boolean. That's safe
        // precisely because they don't overlap.
        Manifold sharp = Manifold.Cube(size, center: false);

        var view = new DevView("Rounded box", colour: "#4a7fc1");
        view.Step("Sharp box (for comparison only)", sharp);
        view.Step("A ball tucked into each corner", Manifold.Compose(balls));
        view.Step("Hull of 2 balls: one rounded edge", Manifold.HullManifolds(balls.GetRange(0, 2)));
        view.Step("Hull of 4 balls: a rounded slab", Manifold.HullManifolds(balls.GetRange(0, 4)));
        view.Step("Hull of all 8: the rounded box", box);
        view.Step("Compared with the sharp box", box, tool: sharp);
        view.Show();
    }
}
