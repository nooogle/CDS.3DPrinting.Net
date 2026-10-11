using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

/// <summary>
/// Turns any STL into an interactive 3D page you can spin in a browser - the C#
/// twin of view_stl.py. Drag = rotate, scroll = zoom, right-drag = pan,
/// double-click = reset.
///
/// How it works: the STL is read into the same two arrays Trimesh gives you
/// (points + triangle indices), and Plotly's Mesh3d hands them to WebGL in the
/// browser, so the graphics card re-renders every time you move the mouse.
///
/// The page also has a slicer: pick an axis and drag the slider to move a cut
/// plane through the model, with the cross-section drawn flat alongside.
/// </summary>
public static class StlViewer
{
    /// <summary>Writes &lt;name&gt;.html next to the STL and returns its path.</summary>
    /// <param name="embedPlotly">
    /// True writes plotly.js into the page so it works offline (~4.6 MB);
    /// false links to the CDN instead (a few KB, but needs internet to open).
    /// </param>
    public static string Render(string stlPath, string colour = "#55565e", bool open = true, bool embedPlotly = false)
    {
        var (v, f) = Load(stlPath);
        int numTri = f.Length / 3;

        var (min, max) = Bounds(v);
        var extents = string.Join(", ", Enumerable.Range(0, 3).Select(k => (max[k] - min[k]).ToString("0.0", CultureInfo.InvariantCulture)));
        Console.WriteLine($"{stlPath}: {numTri} triangles, [{extents}] mm, watertight={IsWatertight(f)}");

        // Smooth curved parts (lots of small triangles) look better with smooth shading;
        // a box with 12 triangles looks right with flat shading.
        bool flat = numTri < 500;

        // The look of the model. The geometry (x/y/z points, i/j/k triangles) is added
        // in the browser, because slicing rebuilds it every time the slider moves.
        var style = new
        {
            type = "mesh3d",
            color = colour,
            flatshading = flat,
            lighting = new { ambient = 0.35, diffuse = 0.8, specular = 0.25, roughness = 0.6, fresnel = 0.1 },
            lightposition = new { x = 200, y = -300, z = 400 },
            hovertemplate = "x %{x:.1f}<br>y %{y:.1f}<br>z %{z:.1f} mm<extra></extra>",
        };
        var model = new { v, f, min, max, style };
        string title = $"{Path.GetFileName(stlPath)}  ({numTri} triangles)";

        // $$$ so that JavaScript's own {{ and }} pass through; C# values go in {{{ }}}.
        string html = $$$"""
            <!doctype html>
            <html>
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>{{{Path.GetFileName(stlPath)}}}</title>
              {{{PlotlyScriptTag(embedPlotly)}}}
              <style>
                html, body { height: 100%; margin: 0; font: 14px system-ui, sans-serif; }
                body { display: flex; flex-direction: column; }
                #bar { display: flex; flex-wrap: wrap; gap: 12px; align-items: center; padding: 8px 12px; border-bottom: 1px solid #ddd; }
                #bar h1 { font-size: 15px; margin: 0 12px 0 0; }
                #cut { flex: 1; min-width: 160px; }
                #pos { font-variant-numeric: tabular-nums; min-width: 9ch; }
                #views { flex: 1; display: flex; min-height: 0; }
                #view3d { flex: 2; min-width: 0; }
                #view2d { flex: 1; min-width: 0; border-left: 1px solid #ddd; }
                @media (max-width: 700px) {
                  #views { flex-direction: column; }
                  #view2d { border-left: 0; border-top: 1px solid #ddd; }
                }
              </style>
            </head>
            <body>
              <div id="bar">
                <h1>{{{title}}}</h1>
                <label>Slice along
                  <select id="axis"><option value="0">X</option><option value="1">Y</option><option value="2" selected>Z</option></select>
                </label>
                <input id="cut" type="range" min="0" max="1000" value="1000">
                <span id="pos"></span>
                <label><input id="clip" type="checkbox" checked> Hide above cut</label>
              </div>
              <div id="views">
                <div id="view3d"></div>
                <div id="view2d"></div>
              </div>
              <script>
                const M = {{{JsonSerializer.Serialize(model)}}};
                const N = M.v.length / 3;
                const AXES = ["x", "y", "z"];
                // The two axes left on screen in the flat 2D view, for each slicing axis.
                const PLANE = [[1, 2], [0, 2], [0, 1]];
                const SECTION = "#0a84ff";

                // Cut the mesh with the plane "coordinate a = h". Triangles wholly below are
                // kept, ones that straddle it are trimmed to the part below, and each trimmed
                // triangle contributes one line segment to the cross-section outline.
                function slice(a, h, clip) {
                  const P = [[], [], []];                  // point columns, x / y / z
                  for (let n = 0; n < N; n++) for (let c = 0; c < 3; c++) P[c].push(M.v[n * 3 + c]);
                  const I = [], J = [], K = [];
                  const seg = [[], [], []];                // outline segments, null between them
                  const cuts = new Map();                  // edge -> new point, so neighbours share it

                  const below = p => M.v[p * 3 + a] <= h;
                  const cutPoint = (p, q) => {
                    const key = p < q ? p * N + q : q * N + p;
                    if (!cuts.has(key)) {
                      const t = (h - M.v[p * 3 + a]) / (M.v[q * 3 + a] - M.v[p * 3 + a]);
                      for (let c = 0; c < 3; c++) P[c].push(M.v[p * 3 + c] + t * (M.v[q * 3 + c] - M.v[p * 3 + c]));
                      cuts.set(key, P[0].length - 1);
                    }
                    return cuts.get(key);
                  };
                  const tri = (p, q, r) => { I.push(p); J.push(q); K.push(r); };

                  for (let t = 0; t < M.f.length; t += 3) {
                    const tv = [M.f[t], M.f[t + 1], M.f[t + 2]];
                    const inside = tv.map(below);
                    const count = inside.filter(Boolean).length;
                    if (count === 3 || count === 0) {
                      if (count === 3 || !clip) tri(...tv);
                      continue;
                    }
                    // Walk the edges, keeping corners below the plane and adding a point
                    // wherever an edge crosses it: gives a triangle or a quad.
                    const poly = [], crossing = [];
                    for (let e = 0; e < 3; e++) {
                      const p = tv[e], q = tv[(e + 1) % 3];
                      if (inside[e]) poly.push(p);
                      if (inside[e] !== inside[(e + 1) % 3]) { const c = cutPoint(p, q); poly.push(c); crossing.push(c); }
                    }
                    if (clip) for (let e = 1; e + 1 < poly.length; e++) tri(poly[0], poly[e], poly[e + 1]);
                    else tri(...tv);
                    for (let c = 0; c < 3; c++) seg[c].push(P[c][crossing[0]], P[c][crossing[1]], null);
                  }
                  return { P, I, J, K, seg };
                }

                // Fixed axis ranges, so the view doesn't jump as the clipped model shrinks.
                const pad = k => 0.02 * (M.max[k] - M.min[k] || 1);
                const range = k => [M.min[k] - pad(k), M.max[k] + pad(k)];
                const span = AXES.map((_, k) => range(k)[1] - range(k)[0]);
                const biggest = Math.max(...span);

                const layout3d = {
                  uirevision: "keep",                        // keep the camera when redrawing
                  showlegend: false,
                  margin: { l: 0, r: 0, t: 0, b: 0 },
                  scene: {
                    aspectmode: "manual",                    // true proportions, mm on every axis
                    aspectratio: { x: span[0] / biggest, y: span[1] / biggest, z: span[2] / biggest },
                    xaxis: { title: { text: "x (mm)" }, range: range(0) },
                    yaxis: { title: { text: "y (mm)" }, range: range(1) },
                    zaxis: { title: { text: "z (mm)" }, range: range(2) },
                  },
                };

                const axisEl = document.getElementById("axis");
                const cutEl = document.getElementById("cut");
                const clipEl = document.getElementById("clip");
                const posEl = document.getElementById("pos");

                function draw() {
                  const a = +axisEl.value;
                  const h = M.min[a] + (M.max[a] - M.min[a]) * cutEl.value / 1000;
                  posEl.textContent = `${AXES[a]} = ${h.toFixed(2)} mm`;
                  const s = slice(a, h, clipEl.checked);

                  const mesh = { ...M.style, x: s.P[0], y: s.P[1], z: s.P[2], i: s.I, j: s.J, k: s.K };
                  const outline = { type: "scatter3d", mode: "lines", x: s.seg[0], y: s.seg[1], z: s.seg[2],
                                    line: { color: SECTION, width: 6 }, hoverinfo: "skip" };
                  Plotly.react("view3d", [mesh, outline], layout3d, { responsive: true });

                  const [u, w] = PLANE[a];
                  Plotly.react("view2d", [{
                    type: "scatter", mode: "lines", x: s.seg[u], y: s.seg[w],
                    line: { color: SECTION, width: 2 }, hoverinfo: "skip",
                  }], {
                    uirevision: a,                           // reset zoom only when the axis changes
                    title: { text: `Cross-section at ${AXES[a]} = ${h.toFixed(2)} mm`, font: { size: 14 } },
                    margin: { l: 50, r: 20, t: 40, b: 40 },
                    xaxis: { title: { text: `${AXES[u]} (mm)` }, range: range(u) },
                    yaxis: { title: { text: `${AXES[w]} (mm)` }, range: range(w), scaleanchor: "x" },
                  }, { responsive: true });
                }

                axisEl.addEventListener("change", draw);
                cutEl.addEventListener("input", draw);
                clipEl.addEventListener("change", draw);
                draw();
              </script>
            </body>
            </html>
            """;

        string outPath = Path.GetFullPath(Path.ChangeExtension(stlPath, ".html"));
        File.WriteAllText(outPath, html);
        Console.WriteLine($"Saved {outPath}");

        // Open in the default browser - UseShellExecute is what makes the OS pick the app.
        if (open)
        {
            Process.Start(new ProcessStartInfo(outPath) { UseShellExecute = true });
        }
        return outPath;
    }

    /// <summary>
    /// Reads a binary or ASCII STL and welds identical corners together, so each
    /// point is stored once and triangles share it (needed for smooth shading).
    /// </summary>
    public static (float[] Vertices, int[] Faces) Load(string path)
    {
        var corners = IsBinary(path) ? ReadBinary(path) : ReadAscii(path);

        var index = new Dictionary<(float, float, float), int>();
        var verts = new List<float>();
        var faces = new int[corners.Count];
        for (int c = 0; c < corners.Count; c++)
        {
            if (!index.TryGetValue(corners[c], out int id))
            {
                id = index.Count;
                index[corners[c]] = id;
                verts.Add(corners[c].Item1); verts.Add(corners[c].Item2); verts.Add(corners[c].Item3);
            }
            faces[c] = id;
        }
        return (verts.ToArray(), faces);
    }

    // An ASCII file starts with "solid", but so do some binary ones - the reliable
    // test is whether the size matches 84 + 50 bytes per triangle.
    static bool IsBinary(string path)
    {
        long length = new FileInfo(path).Length;
        if (length < 84) return false;
        using var reader = new BinaryReader(File.OpenRead(path));
        reader.ReadBytes(80);
        return 84 + 50L * reader.ReadUInt32() == length;
    }

    static List<(float, float, float)> ReadBinary(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        reader.ReadBytes(80);                    // header
        uint numTri = reader.ReadUInt32();
        var corners = new List<(float, float, float)>((int)numTri * 3);
        for (int t = 0; t < numTri; t++)
        {
            reader.ReadBytes(12);                // facet normal - recomputed by the browser
            for (int c = 0; c < 3; c++)
            {
                corners.Add((reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()));
            }
            reader.ReadUInt16();                 // attribute byte count
        }
        return corners;
    }

    static List<(float, float, float)> ReadAscii(string path)
    {
        var corners = new List<(float, float, float)>();
        foreach (var line in File.ReadLines(path))
        {
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 4 && parts[0] == "vertex")
            {
                corners.Add((Parse(parts[1]), Parse(parts[2]), Parse(parts[3])));
            }
        }
        return corners;

        static float Parse(string s) => float.Parse(s, CultureInfo.InvariantCulture);
    }

    // Watertight = every edge is shared by exactly two triangles.
    static bool IsWatertight(int[] f)
    {
        var edges = new Dictionary<(int, int), int>();
        for (int t = 0; t < f.Length; t += 3)
        {
            for (int e = 0; e < 3; e++)
            {
                int a = f[t + e], b = f[t + (e + 1) % 3];
                var key = a < b ? (a, b) : (b, a);
                edges[key] = edges.GetValueOrDefault(key) + 1;
            }
        }
        return edges.Count > 0 && edges.Values.All(n => n == 2);
    }

    static (float[] Min, float[] Max) Bounds(float[] v)
    {
        float[] min = [float.MaxValue, float.MaxValue, float.MaxValue];
        float[] max = [float.MinValue, float.MinValue, float.MinValue];
        for (int p = 0; p < v.Length; p += 3)
        {
            for (int k = 0; k < 3; k++)
            {
                min[k] = Math.Min(min[k], v[p + k]);
                max[k] = Math.Max(max[k], v[p + k]);
            }
        }
        return (min, max);
    }

    const string PlotlyUrl = "https://cdn.plot.ly/plotly-2.35.2.min.js";

    // Either a link to the CDN (small file, needs internet) or the whole of plotly.js
    // inline (~4.6 MB, works offline - Python's include_plotlyjs=True).
    internal static string PlotlyScriptTag(bool embed)
    {
        if (!embed) return $"<script src=\"{PlotlyUrl}\"></script>";

        // Downloaded once, then reused from the local cache.
        string cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                    "StlViewer", Path.GetFileName(PlotlyUrl));
        if (!File.Exists(cache))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
            using var http = new HttpClient();
            File.WriteAllText(cache, http.GetStringAsync(PlotlyUrl).GetAwaiter().GetResult());
        }
        // A literal "</script" inside the code would end the tag early.
        return $"<script>{File.ReadAllText(cache).Replace("</script", "<\\/script")}</script>";
    }
}
