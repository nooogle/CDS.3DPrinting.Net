using System.Diagnostics;
using System.Text.Json;
using ManifoldSharp;

/// <summary>
/// Captures a model at each stage of building it, then shows the stages as a page you can
/// step through in a browser - the C# twin of devview.py.
///
///     var view = new DevView("Chamfered pot");
///     view.Step("Revolved outline", pot);
///     view.Step("Hole cutter", pot, tool: cutter);   // cutter drawn in translucent red
///     pot = pot - cutter;
///     view.Step("Holes cut", pot);                   // previous stage drawn as a faint ghost
///     view.Show();                                   // writes the page and opens it
///
/// In the page: Left/Right (or the Stage slider) steps through the stages. A stage without
/// a tool shows the stage before it as a faint grey ghost, so you can see what changed.
///
/// Slicing, as in StlViewer: pick a cut axis and drag the Cut slider (or s, Up/Down, f) to
/// move a plane through the model. The cut edge is outlined in the 3D view, and the
/// cross-section is drawn flat alongside: model in blue, tool in red, the ghost (the
/// previous stage) in grey, so you can see exactly what a step removed. The plane stays at
/// the same height in mm as you step through the stages, so they compare like for like.
/// Edges (or w) draws the triangles' edges over the model and tool: all of them, or only
/// the "feature" edges where the surface bends by more than 20°.
/// c hides tools, r resets the view, h hides the key help.
///
/// Differences from the Python version: the page is written once, at Show(), rather than
/// redrawn live as each step runs; the cut face is outlined rather than capped; and there's
/// no animate() yet.
/// </summary>
public class DevView
{
    readonly string _title;
    readonly string _colour;
    readonly List<object> _stages = new();

    public DevView(string title = "devview", string colour = "#c9ccd6")
    {
        _title = title;
        _colour = colour;
    }

    /// <summary>
    /// Adds a stage. <paramref name="tool"/> is an optional shape, such as a cutter about
    /// to be subtracted, drawn in translucent red over the model.
    /// </summary>
    public void Step(string label, Manifold model, Manifold? tool = null)
    {
        // Snapshot the triangles now: later stages don't change this one.
        _stages.Add(new { label, model = ToMesh(model), tool = tool is null ? null : ToMesh(tool) });
        Console.WriteLine($"devview: {_stages.Count,2}. {label}  ({model.NumTri()} triangles{(tool is null ? "" : $", tool {tool.NumTri()}")})");
    }

    /// <summary>Writes the page (default: the title as a file name, .html) and opens it.</summary>
    public string Show(string? path = null, bool open = true, bool embedPlotly = true)
    {
        path ??= string.Concat(_title.Select(ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '_')) + ".html";

        var data = new { title = _title, colour = _colour, stages = _stages };

        // $$$ so that JavaScript's own {{ and }} pass through; C# values go in {{{ }}}.
        string html = $$$"""
            <!doctype html>
            <html>
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>{{{_title}}}</title>
              {{{StlViewer.PlotlyScriptTag(embedPlotly)}}}
              <style>
                html, body { height: 100%; margin: 0; font: 14px system-ui, sans-serif; background: #fff; color: #222; }
                body { display: flex; flex-direction: column; }
                .bar { display: flex; flex-wrap: wrap; gap: 6px 14px; align-items: center; padding: 6px 12px; border-bottom: 1px solid #ddd; }
                .bar h1 { font-size: 15px; margin: 0 12px 0 0; }
                .bar input[type=range] { flex: 1; min-width: 140px; }
                #pos { font-variant-numeric: tabular-nums; min-width: 11ch; }
                #views { flex: 1; display: flex; min-height: 0; }
                #wrap3d { flex: 2; position: relative; min-width: 0; }
                #view3d { position: absolute; inset: 0; }
                #view2d { flex: 1; min-width: 0; border-left: 1px solid #ddd; }
                #label { position: absolute; top: 10px; left: 12px; font-size: 16px; font-weight: 600; pointer-events: none; }
                #help { position: absolute; bottom: 10px; left: 12px; font: 12px ui-monospace, Menlo, monospace;
                        white-space: pre; color: #444; background: #fffd; padding: 6px 8px; border-radius: 4px; pointer-events: none; }
                @media (max-width: 700px) {
                  #views { flex-direction: column; }
                  #view2d { border-left: 0; border-top: 1px solid #ddd; }
                }
              </style>
            </head>
            <body>
              <div class="bar">
                <h1>{{{_title}}}</h1>
                <label for="stage">Stage</label>
                <input id="stage" type="range" min="0" step="1">
                <label><input id="tools" type="checkbox" checked> Tools</label>
                <label>Edges
                  <select id="wire"><option value="0">off</option><option value="1">all triangles</option><option value="2">features only</option></select>
                </label>
              </div>
              <div class="bar">
                <label>Cut along
                  <select id="axis"><option value="-1">off</option><option value="0">X</option><option value="1">Y</option><option value="2">Z</option></select>
                </label>
                <input id="cut" type="range" min="0" max="1000">
                <span id="pos"></span>
                <label><input id="flip" type="checkbox"> Flip side</label>
                <label><input id="clip" type="checkbox" checked> Hide cut-away part</label>
              </div>
              <div id="views">
                <div id="wrap3d">
                  <div id="view3d"></div>
                  <div id="label"></div>
                  <div id="help"></div>
                </div>
                <div id="view2d"></div>
              </div>
              <script>
                const D = {{{JsonSerializer.Serialize(data)}}};
                const N = D.stages.length;
                const AXES = ["x", "y", "z"];
                // The two axes left on screen in the flat 2D view, for each cut axis.
                const PLANE = [[1, 2], [0, 2], [0, 1]];
                const GHOST = "#9a9a9a", TOOL = "#d6402a", SECTION = "#0a84ff";

                // Each mesh gets its point columns and per-axis bounds worked out once.
                const meshes = [];
                for (const s of D.stages) for (const m of [s.model, s.tool]) if (m) prepare(m);
                function prepare(m) {
                  const n = m.v.length / 3;
                  m.n = n;
                  m.cols = [[], [], []];
                  m.min = [Infinity, Infinity, Infinity];
                  m.max = [-Infinity, -Infinity, -Infinity];
                  for (let p = 0; p < n; p++) for (let c = 0; c < 3; c++) {
                    const x = m.v[p * 3 + c];
                    m.cols[c].push(x);
                    m.min[c] = Math.min(m.min[c], x);
                    m.max[c] = Math.max(m.max[c], x);
                  }
                  m.I = []; m.J = []; m.K = [];
                  for (let t = 0; t < m.f.length; t += 3) { m.I.push(m.f[t]); m.J.push(m.f[t + 1]); m.K.push(m.f[t + 2]); }
                  meshes.push(m);
                }

                // Fixed axis ranges covering every stage and tool, so the view never jumps.
                const lo = [0, 1, 2].map(c => Math.min(...meshes.map(m => m.min[c])));
                const hi = [0, 1, 2].map(c => Math.max(...meshes.map(m => m.max[c])));
                const pad = c => 0.02 * (hi[c] - lo[c] || 1);
                const range = c => [lo[c] - pad(c), hi[c] + pad(c)];
                const span = [0, 1, 2].map(c => range(c)[1] - range(c)[0]);
                const biggest = Math.max(...span);

                const st = { i: N - 1, axis: -1, frac: 0.5, flip: false, clip: true, tools: true, help: true, wire: 0 };

                // Edges where the surface bends by more than this count as "features".
                const FEATURE_COS = Math.cos(20 * Math.PI / 180);

                // The plane "coordinate a = h", a fraction of the way across everything, so it
                // stays put in mm as you step between stages.
                const planeAt = () => lo[st.axis] + st.frac * (hi[st.axis] - lo[st.axis]);

                // Cut a mesh with the plane. With clip, triangles on the cut-away side are
                // dropped and ones that straddle the plane are trimmed; without, the mesh is
                // kept whole. Either way each straddling triangle adds one outline segment.
                function slice(m, a, h, flip, clip) {
                  const P = m.cols.map(col => col.slice());
                  const I = [], J = [], K = [], seg = [[], [], []];
                  const cuts = new Map();
                  const keep = p => flip ? m.v[p * 3 + a] >= h : m.v[p * 3 + a] <= h;
                  const cutPoint = (p, q) => {
                    const key = p < q ? p * m.n + q : q * m.n + p;
                    if (!cuts.has(key)) {
                      const t = (h - m.v[p * 3 + a]) / (m.v[q * 3 + a] - m.v[p * 3 + a]);
                      for (let c = 0; c < 3; c++) P[c].push(m.v[p * 3 + c] + t * (m.v[q * 3 + c] - m.v[p * 3 + c]));
                      cuts.set(key, P[0].length - 1);
                    }
                    return cuts.get(key);
                  };
                  const tri = (p, q, r) => { I.push(p); J.push(q); K.push(r); };
                  for (let t = 0; t < m.f.length; t += 3) {
                    const tv = [m.f[t], m.f[t + 1], m.f[t + 2]];
                    const inside = tv.map(keep);
                    const count = inside.filter(Boolean).length;
                    if (count === 3 || count === 0) {
                      if (count === 3 || !clip) tri(...tv);
                      continue;
                    }
                    // Walk the edges, keeping corners on the kept side and adding a point
                    // wherever an edge crosses the plane: gives a triangle or a quad.
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

                // The triangle edges of a mesh as line segments (null between them), each edge
                // drawn once even though two triangles share it. With features, only edges
                // where the two triangles meet at more than 20° are kept, plus any edge with
                // just one triangle (such as along a cut).
                function edges(P, I, J, K, features) {
                  const n = P[0].length;
                  const faces = new Map();                     // edge -> the triangles using it
                  const normals = [];
                  for (let t = 0; t < I.length; t++) {
                    const v = [I[t], J[t], K[t]];
                    if (features) {
                      const d = (p, q, c) => P[c][q] - P[c][p];
                      const ux = d(v[0], v[1], 0), uy = d(v[0], v[1], 1), uz = d(v[0], v[1], 2);
                      const wx = d(v[0], v[2], 0), wy = d(v[0], v[2], 1), wz = d(v[0], v[2], 2);
                      const nx = uy * wz - uz * wy, ny = uz * wx - ux * wz, nz = ux * wy - uy * wx;
                      const len = Math.hypot(nx, ny, nz) || 1;
                      normals.push([nx / len, ny / len, nz / len]);
                    }
                    for (let e = 0; e < 3; e++) {
                      const p = v[e], q = v[(e + 1) % 3];
                      const key = p < q ? p * n + q : q * n + p;
                      if (!faces.has(key)) faces.set(key, []);
                      faces.get(key).push(t);
                    }
                  }
                  const seg = [[], [], []];
                  for (const [key, ts] of faces) {
                    if (features && ts.length === 2) {
                      const [a, b] = [normals[ts[0]], normals[ts[1]]];
                      if (a[0] * b[0] + a[1] * b[1] + a[2] * b[2] > FEATURE_COS) continue;
                    }
                    const p = Math.floor(key / n), q = key % n;
                    for (let c = 0; c < 3; c++) seg[c].push(P[c][p], P[c][q], null);
                  }
                  return seg;
                }

                // Uncut meshes don't change, so their edges are only worked out once per mode.
                function cachedEdges(m) {
                  m.edges ??= {};
                  return m.edges[st.wire] ??= edges(m.cols, m.I, m.J, m.K, st.wire === 2);
                }

                // One mesh's 3D trace, its cut outline (null when not cutting) and its
                // triangle edges (null when edges are off).
                function part(m, colour, opacity, withEdges) {
                  const style = {
                    type: "mesh3d", color: colour, opacity, flatshading: m.f.length / 3 < 500,
                    lighting: { ambient: 0.4, diffuse: 0.8, specular: 0.2, roughness: 0.6, fresnel: 0.1 },
                    lightposition: { x: 200, y: -300, z: 400 },
                    hoverinfo: "skip",
                  };
                  const wire = withEdges && st.wire > 0;
                  if (st.axis < 0) return {
                    mesh: { ...style, x: m.cols[0], y: m.cols[1], z: m.cols[2], i: m.I, j: m.J, k: m.K },
                    seg: null,
                    edges: wire ? cachedEdges(m) : null,
                  };
                  const s = slice(m, st.axis, planeAt(), st.flip, st.clip);
                  return {
                    mesh: { ...style, x: s.P[0], y: s.P[1], z: s.P[2], i: s.I, j: s.J, k: s.K },
                    seg: s.seg,
                    edges: wire ? edges(s.P, s.I, s.J, s.K, st.wire === 2) : null,
                  };
                }

                const layout3d = {
                  uirevision: "keep",                          // keep the camera between redraws
                  showlegend: false,
                  margin: { l: 0, r: 0, t: 0, b: 0 },
                  // No hover at all: the hover marker and its spike lines to the axes leave
                  // trails behind them when the scene has see-through ghost and tool layers.
                  hovermode: false,
                  scene: {
                    hovermode: false,
                    aspectmode: "manual",                      // true proportions, mm on every axis
                    aspectratio: { x: span[0] / biggest, y: span[1] / biggest, z: span[2] / biggest },
                    xaxis: { title: { text: "x (mm)" }, range: range(0), showspikes: false },
                    yaxis: { title: { text: "y (mm)" }, range: range(1), showspikes: false },
                    zaxis: { title: { text: "z (mm)" }, range: range(2), showspikes: false },
                  },
                };

                const HELP = [
                  "Left/Right   previous / next stage",
                  "s            cut axis: off > X > Y > Z",
                  "Up/Down      move cut plane",
                  "f            flip cut side",
                  "c            show/hide cutter tool",
                  "w            edges: off > all > features",
                  "r           reset view",
                  "h            hide this help",
                ].join("\n");

                const el = id => document.getElementById(id);
                const stageEl = el("stage"), axisEl = el("axis"), cutEl = el("cut");
                stageEl.max = N - 1;

                // The controls and the keys change the same state, then redraw everything.
                stageEl.addEventListener("input", () => { st.i = +stageEl.value; draw(); });
                axisEl.addEventListener("change", () => { st.axis = +axisEl.value; draw(); });
                cutEl.addEventListener("input", () => { st.frac = cutEl.value / 1000; draw(); });
                el("flip").addEventListener("change", e => { st.flip = e.target.checked; draw(); });
                el("clip").addEventListener("change", e => { st.clip = e.target.checked; draw(); });
                el("tools").addEventListener("change", e => { st.tools = e.target.checked; draw(); });
                el("wire").addEventListener("change", e => { st.wire = +e.target.value; draw(); });

                let shown2d = null;
                function draw() {
                  const s = D.stages[st.i];
                  // A stage with a tool shows the same model as before, so only ghost the others.
                  const parts = [];
                  if (st.i > 0 && !s.tool) parts.push({ ...part(D.stages[st.i - 1].model, GHOST, 0.12, false), line: GHOST, dash: "dot" });
                  parts.push({ ...part(s.model, D.colour, 1, true), line: SECTION, dash: "solid", wire: "#222" });
                  if (s.tool && st.tools) parts.push({ ...part(s.tool, TOOL, 0.6, true), line: TOOL, dash: "solid", wire: "#7a1a0e" });

                  const data3d = parts.map(p => p.mesh);
                  for (const p of parts) if (p.edges) data3d.push({
                    type: "scatter3d", mode: "lines", x: p.edges[0], y: p.edges[1], z: p.edges[2],
                    line: { color: p.wire, width: 1.5 }, hoverinfo: "skip" });
                  for (const p of parts) if (p.seg && p.line !== GHOST) data3d.push({
                    type: "scatter3d", mode: "lines", x: p.seg[0], y: p.seg[1], z: p.seg[2],
                    line: { color: p.line, width: 5 }, hoverinfo: "skip" });
                  Plotly.react("view3d", data3d, layout3d, { responsive: true });

                  // The flat cross-section, only while cutting.
                  const cutting = st.axis >= 0;
                  if (cutting) {
                    const [u, w] = PLANE[st.axis];
                    const h = planeAt();
                    Plotly.react("view2d", parts.map(p => ({
                      type: "scatter", mode: "lines", x: p.seg[u], y: p.seg[w],
                      line: { color: p.line, width: 2, dash: p.dash }, hoverinfo: "skip",
                    })), {
                      uirevision: st.axis,                     // reset zoom only when the axis changes
                      showlegend: false,
                      title: { text: `Cross-section at ${AXES[st.axis]} = ${h.toFixed(2)} mm`, font: { size: 14 } },
                      margin: { l: 50, r: 20, t: 40, b: 40 },
                      xaxis: { title: { text: `${AXES[u]} (mm)` }, range: range(u) },
                      yaxis: { title: { text: `${AXES[w]} (mm)` }, range: range(w), scaleanchor: "x" },
                    }, { responsive: true });
                  }
                  if (cutting !== shown2d) {                   // panel appeared or went: refit both
                    el("view2d").style.display = cutting ? "" : "none";
                    shown2d = cutting;
                    Plotly.Plots.resize("view3d");
                    if (cutting) Plotly.Plots.resize("view2d");
                  }

                  // Bring the controls in line (keys change the state too).
                  stageEl.value = st.i;
                  axisEl.value = st.axis;
                  cutEl.value = Math.round(st.frac * 1000);
                  cutEl.disabled = el("flip").disabled = el("clip").disabled = !cutting;
                  el("flip").checked = st.flip;
                  el("clip").checked = st.clip;
                  el("tools").checked = st.tools;
                  el("wire").value = st.wire;
                  el("pos").textContent = cutting ? `${AXES[st.axis]} = ${planeAt().toFixed(2)} mm` : "";
                  el("label").textContent = `${st.i + 1}/${N}  ${s.label}`;
                  el("help").textContent = st.help ? HELP : "h = help";
                }

                const KEYS = {
                  ArrowRight: () => st.i = (st.i + 1) % N,
                  ArrowLeft:  () => st.i = (st.i - 1 + N) % N,
                  s:          () => st.axis = st.axis === 2 ? -1 : st.axis + 1,
                  ArrowUp:    () => st.frac = Math.min(1, st.frac + 0.02),
                  ArrowDown:  () => st.frac = Math.max(0, st.frac - 0.02),
                  f:          () => st.flip = !st.flip,
                  c:          () => st.tools = !st.tools,
                  w:          () => st.wire = (st.wire + 1) % 3,
                  h:         () => st.help = !st.help,
                  r:          () => Plotly.relayout("view3d", { "scene.camera": { eye: { x: 1.25, y: 1.25, z: 1.25 },
                                                                              up: { x: 0, y: 0, z: 1 }, center: { x: 0, y: 0, z: 0 } } }),
                };
                document.addEventListener("keydown", e => {
                  if (e.metaKey || e.ctrlKey || e.altKey || !(e.key in KEYS)) return;
                  e.preventDefault();                          // stop a focused slider or list moving too
                  KEYS[e.key]();
                  draw();
                });

                draw();
              </script>
            </body>
            </html>
            """;

        string outPath = Path.GetFullPath(path);
        File.WriteAllText(outPath, html);
        Console.WriteLine($"Saved {outPath}");

        if (open)
        {
            Process.Start(new ProcessStartInfo(outPath) { UseShellExecute = true });
        }
        return outPath;
    }

    // Just the positions (the first 3 properties of each vertex) and the triangle indices.
    static object ToMesh(Manifold m)
    {
        MeshGL mesh = m.GetMeshGL(-1);
        int numProp = (int)mesh.NumProp;
        var props = mesh.VertProperties;
        int numVert = props.Count / numProp;

        var v = new float[numVert * 3];
        for (int p = 0; p < numVert; p++)
        {
            for (int k = 0; k < 3; k++)
            {
                v[p * 3 + k] = props[p * numProp + k];
            }
        }
        return new { v, f = mesh.TriVerts };
    }
}
