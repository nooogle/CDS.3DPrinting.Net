# TryDilateByConvex: drop zero-volume components, keep sliver triangles out of patches

Fixes #ISSUE_NUMBER.

`TryDilateByConvex` can return the correct solid plus a disconnected zero-volume shell beside it. On the reproduction in the issue (an L-bracket closed by a 12-segment ball, then dilated by the same ball), it returns a 4-triangle sliver 40 mm long and genus −1, where `MinkowskiSum` gives one part with genus 0. The volume is the same either way. The issue has the full analysis.

There are two commits, so either can be taken alone. Both stay within ledger entry 6.

### 1. `ConvexDilation` drops stray shells the union tree leaves behind

Where two subtrees' hulls have nearly coincident faces, their exact union can keep a collapsed shell beside the real solid. This happens with and without patches. A new file, `ConvexDilation.Degenerate.cs`, adds one sequential pass at the end of the **dilation** tree. It drops any outward-wound shell with less than half the tool's volume, and the token is checked again after it.

- **Why this rule:** every component of material in a dilation contains a whole copy of the tool, so a smaller outward shell can't be real. The rule needs no epsilon and no measure of thickness. That matters because thickness can't separate a stray from a real thin part: a stray on the bracket was about 4e-8 thick, as thick as a real rod can be.
- **Why cavity shells are kept:** `Decompose` splits shells, not material, and a cavity's shell is wound inward (negative signed volume). A sealed cavity just wider than the tool dilates to a void of almost nothing (a 4.2 mm cavity in a 20 mm cube, dilated by a radius-2 ball, leaves 0.008 mm³), and the sweep keeps it. So an inward shell is never dropped, whatever its size.
- **Known limitation:** a stray that happens to come out inward-wound is kept too. We saw one, a 4-triangle shell of volume −2.4e-19, with patches off (`PatchSizeOverride = 1`); none with patches on. Keeping it is no worse than having no pass, where filling a real cavity would change the part.
- **Only for a tool that contains the origin:** `Minkowski.Compute` and the tree union the solid itself in, which is only part of solid ⊕ tool when the tool contains the origin. With a tool translated off it, the solid can come out as a component of its own, smaller than the tool, and the sweep keeps it. So the pass is skipped for such tools (checked exactly against the tool's faces).
- **Why erosion skips it:** an erosion's components have no lower bound. A bar 2r + 1e-6 across, eroded by radius r, leaves a real rod 1e-6 thick, which the sweep keeps. No erosion in our tests left a stray.
- **When nothing qualifies:** the tree's mesh is returned as the same instance, untouched, so the determinism argument is unchanged.
- **When something is dropped:** the kept shells are recomposed, their epsilon is set again with the tree's epsilon as the floor, and the tree's tolerance is kept. `Decompose` leaves it unset (−1), and composing a single part only clones it.
- **Refactor:** `HasOverlappingComponents` now shares the component labelling.

### 2. `ConvexPatches` keeps sliver triangles out of patches

Condition (1) is exact about each triangle's plane, but a sliver's plane (height below the solid's epsilon) is set by roundoff. Two slivers on the bracket's flat back face each found the other's far vertex strictly below its tilted plane. As a result, (1) and the guard accepted a patch whose hull had volume 4.7e-14 — the flat patch (1) exists to refuse.

A sliver now neither seeds nor joins a patch, so it is swept as its own hull. That alone repairs the bracket; commit 1 covers the shells that appear without patches.

### Tests

New C#-only adaptation tests. The bracket, cavity, translated-tool, thin-rod and sliver tests each fail without their fix, or with an earlier version of it; the two rule tests pin the pass on hand-built meshes:

- **`ConvexDilationDegenerateTests`:**
  - `AComponentSmallerThanTheToolIsDropped`
  - `AComponentAsLargeAsTheBoundIsKept` (the same instance comes back)
  - `TheClosedBracketDilatesToOnePiece`, against the sweep
  - `ANearlyClosedCavitySurvives`, against the sweep
  - `ATranslatedToolKeepsTheSolidsOwnComponent`, against the sweep
  - `ErosionKeepsAThinRod`, against the sweep
- **`ConvexDilationPatchTests.SliverTrianglesStayOutOfPatches`:** checks that the bracket has sliver triangles and that none of them joins a patch.

Checks, on this branch:
- **CI's steps, run locally:** `dotnet build ManifoldSharp.sln -c Release` (0 warnings), the test suite and the oracle lane (34/34).
- **The browser gate:** `-p:BrowserCompatGate=true` on the library (0 warnings).
- **The full suite in Release and Debug,** each with `MANIFOLD_PARALLEL=0` and `=1`: 976 total, 966 passed, 0 failed, 10 skipped (the documented ignores).

As an extra check outside the suite, we compared part count, genus and volume (to 1e-9) against `MinkowskiSum` / `MinkowskiDifference` on 10 parts, dilated and eroded:
- the bracket closed, and closed then eroded, by 8-, 12-, 16- and 24-segment balls;
- an open pot;
- a plate with two holes.

On main there are 2 mismatches; on this branch there are 0 with patches on. With patches off there is 1: the inward-wound stray described above. The cost is 11 more hulls across those parts and a few percent of dilation time at most (not carefully benchmarked; see the issue).

`FileComplianceTests` passes: `ConvexDilation.cs` is now 718 lines, `ConvexPatches.cs` 427 and the new `ConvexDilation.Degenerate.cs` 165.

I haven't built a differential harness against a compiled manifold-rust. `TryDilateByConvex` has no Rust counterpart, and the ported entry points (`Minkowski.Sum` and `Minkowski.Difference`) are untouched. `BENCHMARKS.md` wasn't regenerated.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
