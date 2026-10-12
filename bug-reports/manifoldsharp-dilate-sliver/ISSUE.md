# TryDilateByConvex can leave a zero-volume sliver part beside the solid

**Version:** ManifoldSharp 0.5.0 from NuGet (built from c2a80c2); also reproduces on main at 7951bfd. .NET 10, macOS (Apple silicon).

## Summary

`TryDilateByConvex` returns `true` and the correct main solid, plus an extra disconnected part with 4 triangles and zero volume. `MinkowskiSum` on the same input gives one clean part. The volumes match exactly, but the genus is −1 instead of 0, which goes against the "volume and genus agree" note in `ConvexDilation.cs`.

## Reproduction

```csharp
using ManifoldSharp;
using ManifoldSharp.Linalg;

// An L-shaped bracket, "closed" by a ball (grown, then shrunk back): a valid,
// single-part, genus-0, non-convex solid with a faceted fillet in its inside corner.
Manifold ball = Manifold.Sphere(2.0, 12);
Manifold bracket = Manifold.Cube(new Vec3(40, 20, 6), false) + Manifold.Cube(new Vec3(6, 20, 30), false);
Manifold input = bracket.MinkowskiSum(ball, null, null).MinkowskiDifference(ball, null, null);

bool applied = input.TryDilateByConvex(ball, null, null, out Manifold fast);  // true
Manifold general = input.MinkowskiSum(ball, null, null);

// fast.Genus() == -1, fast.Decompose().Count == 2
// general.Genus() == 0, general.Decompose().Count == 1
```

Output:

```
input: status NoError, 228 triangles, volume 7699.8533, genus 0
TryDilateByConvex returned True
TryDilateByConvex: status NoError, 750 triangles, volume 15901.8214, genus -1
    part: 746 triangles, volume 15901.821367, box (-2, -2, -2) .. (42, 22, 32)
    part: 4 triangles, volume 0.000000, box (3.9E-10, 21.99999999998828, 9.7E-12) .. (39.99999999788534, 22, 4.9E-11)
MinkowskiSum: status NoError, 994 triangles, volume 15901.8214, genus 0
    part: 994 triangles, volume 15901.821367, box (-2, -2, -2) .. (42, 22, 32)
```

The sliver is a collapsed tetrahedron lying along the line y = 22, z = 0, from x = 0 to 40. That is the seam where the dilated back face (y = 22) meets the rounded bottom-back edge. Its coordinates are slightly off the exact values (e.g. 21.99999999998828), which suggests it is left over from a union rather than built directly.

## What we found in the source

We built c2a80c2 from source, replayed `TryReduce` one union at a time (via `InternalsVisibleTo`), and tried some changes.

**1. The input has sliver triangles.** The input comes from `MinkowskiDifference`, and it contains near-degenerate triangles. For example, triangle 206 is 40 mm long with an area of 2e-9, and triangle 227 has an area of 2e-20 (both with Epsilon = 4.4e-11).

**2. A flat patch gets through check (1).** `ConvexPatches.Build` puts triangles 206 and 227 into one patch (unit 130). All four of its vertices lie on the plane y = 20, to within about 1e-11. Because the two triangles are slivers, roundoff tilts their planes, so `Orient3d` finds one patch vertex strictly below each plane. As a result, the strict half of condition (1) passes, and so does `FacesSupport`. The patch's hull has a volume of 4.7e-14: it is flat, which the header says should be refused ("a flat patch is refused").

```
triangle 206: area 1.985E-009   (1.59e-11, 19.99999999999205, 2.4e-16) (40, 19.999999999974047, 5.999999999997894) (39.99999999990494, 20, 5.9999999998879225)
triangle 227: area 2.351E-020   (40, 19.999999999974047, 5.999999999997894) (39.99999999990494, 20, 5.999999999410808) (39.99999999990494, 20, 5.9999999998879225)
sides of patch verts vs plane of 206: Zero Zero Zero Neg
sides of patch verts vs plane of 227: Neg Zero Zero Zero
```

**3. A union of two clean groups makes the sliver.** Replaying the tree, every leaf and every lower node is clean. The first bad node is level 1, node 4: the union of leaf 8 (units 112–127) and leaf 9 (units 128–143), both clean on their own. Shrinking that union while keeping its two-group shape leaves 11 hulls: units 112–114, 120–124 and 127 on the left (`BatchUnion`), and units 129 and 130 on the right, joined with one `BooleanDispatch(Add)`. Folding the same 32 hulls one at a time in index order comes out clean, so the union order matters.

**4. The patches aren't the only trigger.** With `PatchSizeOverride = 1` (one hull per triangle), the reported input comes out clean. However, the same bracket closed and then eroded once more with a 24-segment ball still gives 3 parts (genus −2) through the per-triangle tree, while `MinkowskiSum` gives 1 part. So unioning hulls whose faces nearly coincide in the balanced tree can leave zero-volume shells even without patches.

**What we tried in `ConvexPatches.Accept`/`Grow`** (results for 8 bracket inputs: ball segments 8/12/16/24, closed and closed+eroded):

| Change | Mismatches with `MinkowskiSum` |
|---|---|
| none | 2 of 8 |
| refuse a patch whose hull volume < Epsilon × hull surface area | 1 of 8 (fixes the reported case) |
| keep sliver triangles (height < Epsilon) out of patches | 1 of 8 (fixes the reported case) |
| both | 2 of 8 (a previously clean case breaks) |
| no patches at all (`PatchSizeOverride = 1`) | 1 of 8 |

So refusing flat or sliver patches fixes a real gap in condition (1), but on its own it moves the problem around. The underlying fault looks like the exact union of nearly coincident hull faces leaving zero-volume components, which the balanced tree runs into while `MinkowskiSum`'s serial batches happened not to. A dilation can't legitimately contain a zero-volume component, so a closing pass in `TryReduce` that drops them seemed like the robust fix. We tried it (below).

## Proposed fix

Two commits on [nooogle/manifold-sharp `fix/dilation-degenerate-components`](https://github.com/nooogle/manifold-sharp/tree/fix/dilation-degenerate-components), on top of main (7951bfd). They are separate so either can be taken alone. A pull request will follow this issue.

1. **`ConvexDilation` drops stray shells the union tree leaves behind.** A closing sequential pass at the end of the dilation tree (new file `ConvexDilation.Degenerate.cs`) drops any outward-wound shell with less than half the tool's volume.
   - **Why this rule:** every component of material in a dilation contains a whole copy of the tool. A geometric "is it thin?" rule can't separate strays from real thin parts.
   - **Why cavity shells are kept:** a cavity's shell is wound inward and can shrink to almost nothing (a 4.2 mm cavity dilated by a 4 mm ball leaves 0.008 mm³), so inward shells are never dropped. A stray that comes out inward-wound is therefore kept too; we saw one only with patches off.
   - **Only for a tool that contains the origin:** the tree (like `Minkowski.Compute`) unions the solid itself in, so with a translated tool the solid can be a legitimate component smaller than the tool.
   - **Why erosion skips it:** eroding a bar 2r + 1e-6 across by radius r leaves a real rod 1e-6 thick.
   - **Details:** the pass returns the tree's mesh untouched when there's nothing to drop, resets the epsilon when it does drop, and checks the token again afterwards.
2. **`ConvexPatches` keeps sliver triangles out of patches.** A triangle whose height is below Epsilon neither seeds nor joins a patch, so it is swept as its own hull. This closes the gap in condition (1) described above. On its own it repairs the reported case, but not the shells that appear without patches, which is why commit 1 is needed too.

Each commit updates its file header and ledger entry 6, and adds C#-only adaptation tests:
- **`ConvexDilationDegenerateTests`:** a component smaller than the tool is dropped and one at the bound is kept; the bracket, a near-closing cavity, a translated tool and a thin rod's erosion each match the sweep.
- **`ConvexDilationPatchTests.SliverTrianglesStayOutOfPatches`:** checks that the bracket has sliver triangles and that none of them joins a patch.

Both new tests fail without their fix.

**Results.** Each comparison checks part count, genus and volume (within 1e-9) against `MinkowskiSum` / `MinkowskiDifference`, on 10 parts: the 8 bracket inputs, an open pot and a plate with two holes, each dilated and eroded.

| | Mismatches (of 20) | Hulls |
|---|---|---|
| main | 2 | 2834 |
| commit 1 only | 0 | 2834 |
| commit 2 only | 1 | 2845 |
| both (the branch) | 0 | 2845 |

With patches switched off (`PatchSizeOverride = 1`, a test-only setting), the branch gives 1 mismatch of 20: a stray that came out inward-wound, which the pass keeps because it can't be told from a real cavity.

**Speed.** We didn't measure it carefully. The extra work is small: 11 more hulls across the 10 parts, and a component count that returns at once for a single-part result. In our trial runs, total dilation time went from 8.78 s (main) to 9.04 s (both fixes), +3%. One paired run of main against the branch, on a machine that was busy with other work, gave 53.5 s against 55.4 s, +3.5%. Identical runs varied by several percent, so treat the cost as a few percent at most.

**Test suite.** Run with `dotnet test --project ManifoldSharp.Tests/ManifoldSharp.Tests.csproj` in Release and Debug, each with `MANIFOLD_PARALLEL=0` and `=1`. All four runs gave 976 total, 966 passed, 0 failed and the 10 documented skips. The whole-solution build and the browser gate have 0 warnings, and the oracle lane passes 34/34.

## Workaround until fixed

Call `MinkowskiSum` directly, or drop zero-volume parts from `Decompose()` and `Compose` the rest.
