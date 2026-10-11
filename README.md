# CDS.3DPrinting.Net

> **Work in progress.** This is a personal learning project, not a finished library. Expect things to change, move or break.

Experiments in designing 3D-printable parts in C# with [ManifoldSharp](https://www.nuget.org/packages/ManifoldSharp), a .NET port of the [Manifold](https://github.com/elalish/manifold) geometry library. It's a .NET companion to an earlier Python version of the same work.

## What's here

All in `ConsoleApp1`:

- **Demos**, each building one model step by step, with plenty of comments explaining the ideas:
  - `Demo_Cube`, `Demo_Pyramid`: first shapes, and wrapping a hull round points.
  - `Demo_Pot`, `Demo_PotWithHoles`: subtracting shapes, and patterning a cutter.
  - `Demo_ChamferedPot`: revolving a 2D outline, with chamfered edges and countersunk holes.
  - `Demo_Donut`: a torus from a revolved circle.
  - `Demo_RoundedBox`: a box with rounded edges, from the hull of 8 spheres.
- **`DevView`**: captures a model at each stage of building it and writes a page you can step through in the browser. It shows cutting tools in red, the previous stage as a ghost, a movable cut plane with a flat cross-section, and optional triangle edges.
- **`StlViewer`**: an interactive browser view of any STL file, with a slicer.
- **`StlExporter`**: writes a Manifold mesh to a binary STL file.

## Running

Needs the .NET 10 SDK.

```sh
cd ConsoleApp1
dotnet run
```

Pick which demo runs by uncommenting it in `Program.cs`. Each one writes `result.stl` for slicing and printing, and opens a viewer page in your browser. The pages load plotly.js from its CDN, so opening them needs an internet connection.
