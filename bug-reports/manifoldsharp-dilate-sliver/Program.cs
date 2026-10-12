using ManifoldSharp;
using ManifoldSharp.Linalg;

// The input: an L-shaped bracket, "closed" by a ball (grown, then shrunk back). That gives a
// valid, single-part, genus-0, non-convex solid with a faceted fillet in its inside corner.
Manifold ball = Manifold.Sphere(2.0, 12);
Manifold bracket = Manifold.Cube(new Vec3(40, 20, 6), false) + Manifold.Cube(new Vec3(6, 20, 30), false);
Manifold input = bracket.MinkowskiSum(ball, null, null).MinkowskiDifference(ball, null, null);

// Dilate it by the same ball, both ways.
bool applied = input.TryDilateByConvex(ball, null, null, out Manifold fast);
Manifold general = input.MinkowskiSum(ball, null, null);

Report("input", input);
Console.WriteLine($"TryDilateByConvex returned {applied}");
Report("TryDilateByConvex", fast);
Report("MinkowskiSum", general);

static void Report(string name, Manifold m)
{
    Console.WriteLine($"{name}: status {m.Status()}, {m.NumTri()} triangles, volume {m.Volume():F4}, genus {m.Genus()}");
    foreach (Manifold part in m.Decompose())
    {
        Box b = part.BoundingBox();
        Console.WriteLine($"    part: {part.NumTri()} triangles, volume {part.Volume():F6}, " +
                          $"box ({b.Min.X}, {b.Min.Y}, {b.Min.Z}) .. ({b.Max.X}, {b.Max.Y}, {b.Max.Z})");
    }
}
