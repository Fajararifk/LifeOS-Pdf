using System;
using System.Collections.Generic;
using LifeOS.Pdf.Objects;
using LifeOS.Pdf.Parsing;

namespace LifeOS.Pdf.Extraction.Engine
{
    /// <summary>A shown glyph in device (default user) space.</summary>
    internal sealed class GlyphInfo
    {
        public string Text = string.Empty;

        /// <summary>Corners of the glyph box: baseline start, baseline end, top start (device space).</summary>
        public double X0, Y0, X1, Y1, TopX, TopY;

        public double FontSize;
        public FontReader Font = null!;
        public int RenderMode;

        /// <summary>Position in the content: operation index, element of a TJ array, byte offset, byte count.</summary>
        public int Op, Element, Offset, Length;

        /// <summary>Advance in text space units along the baseline (used to blank out glyphs when redacting).</summary>
        public double Advance;

        /// <summary>Horizontal scaling factor and font size at the time (for TJ adjustments).</summary>
        public double Th, Tfs;
    }

    internal sealed class ImageInfo
    {
        public PdfStream? Stream;
        public PdfDictionary Dictionary = null!;
        public byte[]? InlineData;
        public string? Name;
        public double[] Ctm = null!;
        public int Op;
    }

    /// <summary>A painted path: its subpaths (operation ranges and device boxes) and the painting operation.</summary>
    internal sealed class PathInfo
    {
        public List<(int Start, int End, double X0, double Y0, double X1, double Y1)> Subpaths = new List<(int, int, double, double, double, double)>();
        public int Paint;
        public bool Clips;
    }

    internal sealed class FormInfo
    {
        public string Name = string.Empty;
        public PdfStream Stream = null!;
        public double[] Matrix = null!;
        public PdfDictionary Resources = null!;
        public double X0, Y0, X1, Y1;
        public int Op;
    }

    /// <summary>
    /// Interprets content streams (§8–9): graphics state, text state and positioning, form
    /// XObjects (recursively, with depth, cycle and total-work limits), images and the straight
    /// lines tables are drawn with.
    /// </summary>
    internal sealed class ContentInterpreter
    {
        private const int MaxFormDepth = 12;
        private const long MaxTotalOperations = 5000000;
        private const int MaxSegments = 200000;
        private const int MaxGlyphs = 2000000;

        private readonly PdfParser? _parser;
        private readonly long _streamLimit;
        private readonly Dictionary<PdfDictionary, FontReader> _fonts = new Dictionary<PdfDictionary, FontReader>();
        private readonly HashSet<PdfStream> _activeForms = new HashSet<PdfStream>();
        private long _operations;

        public ContentInterpreter(PdfParser? parser, long streamLimit)
        {
            _parser = parser;
            _streamLimit = streamLimit;
        }

        public bool FollowForms { get; set; } = true;

        public List<GlyphInfo> Glyphs { get; } = new List<GlyphInfo>();

        public List<ImageInfo> Images { get; } = new List<ImageInfo>();

        /// <summary>Axis-aligned straight lines (device space): x1, y1, x2, y2.</summary>
        public List<double[]> Segments { get; } = new List<double[]>();

        /// <summary>Form XObjects drawn directly by the top-level content.</summary>
        public List<FormInfo> Forms { get; } = new List<FormInfo>();

        /// <summary>Record painted paths per subpath (redaction needs them).</summary>
        public bool TrackPaths { get; set; }

        public List<PathInfo> Paths { get; } = new List<PathInfo>();

        public void Run(IList<ContentOperation> ops, PdfDictionary? resources, double[] ctm) => Run(ops, resources, ctm, 0);

        private sealed class State
        {
            public double[] Ctm = { 1, 0, 0, 1, 0, 0 };
            public double Tc, Tw, Th = 1, TL, Ts, Tfs;
            public int Tr;
            public FontReader? Font;

            public State Clone() => (State)MemberwiseClone();
        }

        private void Run(IList<ContentOperation> ops, PdfDictionary? resources, double[] ctm, int depth)
        {
            var gs = new State { Ctm = ctm };
            var stack = new Stack<State>();
            double[] tm = Identity(), tlm = Identity();
            var path = new List<double[]>(); // current subpaths as polylines in user space
            List<double>? current = null;
            PathInfo? tracked = null;
            void Extend(int opIndex, params double[] userPoints)
            {
                if (!TrackPaths || tracked == null || tracked.Subpaths.Count == 0)
                {
                    return;
                }

                var last = tracked.Subpaths[tracked.Subpaths.Count - 1];
                for (var p = 0; p + 1 < userPoints.Length; p += 2)
                {
                    Transform(gs.Ctm, userPoints[p], userPoints[p + 1], out var x, out var y);
                    last.X0 = Math.Min(last.X0, x);
                    last.Y0 = Math.Min(last.Y0, y);
                    last.X1 = Math.Max(last.X1, x);
                    last.Y1 = Math.Max(last.Y1, y);
                }

                last.End = opIndex;
                tracked.Subpaths[tracked.Subpaths.Count - 1] = last;
            }

            void Begin(int opIndex, double x, double y)
            {
                if (!TrackPaths)
                {
                    return;
                }

                tracked = tracked ?? new PathInfo();
                Transform(gs.Ctm, x, y, out var dx, out var dy);
                tracked.Subpaths.Add((opIndex, opIndex, dx, dy, dx, dy));
            }
            for (var index = 0; index < ops.Count; index++)
            {
                if (++_operations > MaxTotalOperations)
                {
                    throw new PdfLimitException("The page draws more than " + MaxTotalOperations + " operations (nested forms); it is too complex to analyse.");
                }

                var op = ops[index];
                var a = op.Operands;
                switch (op.Op)
                {
                    case "q":
                        if (stack.Count < 256)
                        {
                            stack.Push(gs.Clone());
                        }

                        break;
                    case "Q":
                        if (stack.Count > 0)
                        {
                            gs = stack.Pop();
                        }

                        break;
                    case "cm":
                        if (a.Length >= 6)
                        {
                            gs.Ctm = Mul(Matrix(a, 0), gs.Ctm);
                        }

                        break;
                    case "BT":
                        tm = Identity();
                        tlm = Identity();
                        break;
                    case "Tc": gs.Tc = Num(a, 0); break;
                    case "Tw": gs.Tw = Num(a, 0); break;
                    case "Tz": gs.Th = Num(a, 0) / 100; break;
                    case "TL": gs.TL = Num(a, 0); break;
                    case "Ts": gs.Ts = Num(a, 0); break;
                    case "Tr": gs.Tr = (int)Num(a, 0); break;
                    case "Tf":
                        gs.Tfs = Num(a, 1);
                        gs.Font = a.Length >= 1 && a[0] is PdfName fn ? Font(resources, fn.Value) : null;
                        break;
                    case "Td":
                        tlm = Mul(new[] { 1, 0, 0, 1, Num(a, 0), Num(a, 1) }, tlm);
                        tm = tlm;
                        break;
                    case "TD":
                        gs.TL = -Num(a, 1);
                        tlm = Mul(new[] { 1, 0, 0, 1, Num(a, 0), Num(a, 1) }, tlm);
                        tm = tlm;
                        break;
                    case "Tm":
                        if (a.Length >= 6)
                        {
                            tlm = Matrix(a, 0);
                            tm = tlm;
                        }

                        break;
                    case "T*":
                        tlm = Mul(new[] { 1, 0, 0, 1, 0, -gs.TL }, tlm);
                        tm = tlm;
                        break;
                    case "Tj":
                        if (a.Length >= 1 && a[0] is PdfString s)
                        {
                            tm = Show(s.Bytes, gs, tm, index, 0);
                        }

                        break;
                    case "'":
                        tlm = Mul(new[] { 1, 0, 0, 1, 0, -gs.TL }, tlm);
                        tm = tlm;
                        if (a.Length >= 1 && a[0] is PdfString s1)
                        {
                            tm = Show(s1.Bytes, gs, tm, index, 0);
                        }

                        break;
                    case "\"":
                        gs.Tw = Num(a, 0);
                        gs.Tc = Num(a, 1);
                        tlm = Mul(new[] { 1, 0, 0, 1, 0, -gs.TL }, tlm);
                        tm = tlm;
                        if (a.Length >= 3 && a[2] is PdfString s2)
                        {
                            tm = Show(s2.Bytes, gs, tm, index, 2);
                        }

                        break;
                    case "TJ":
                        if (a.Length >= 1 && a[0] is PdfArray items)
                        {
                            for (var e = 0; e < items.Items.Count; e++)
                            {
                                switch (items.Items[e])
                                {
                                    case PdfString str:
                                        tm = Show(str.Bytes, gs, tm, index, e);
                                        break;
                                    case PdfInteger _:
                                    case PdfReal _:
                                        var tx = -NumOf(items.Items[e]) / 1000 * gs.Tfs * gs.Th;
                                        tm = Mul(new[] { 1, 0, 0, 1, tx, 0 }, tm);
                                        break;
                                }
                            }
                        }

                        break;
                    case "Do":
                        if (a.Length >= 1 && a[0] is PdfName xn)
                        {
                            DoXObject(resources, xn.Value, gs.Ctm, index, depth);
                        }

                        break;
                    case "BI":
                        Images.Add(new ImageInfo { Dictionary = op.InlineDictionary!, InlineData = op.InlineData, Ctm = gs.Ctm, Op = index });
                        break;

                    // Paths: only straight lines and rectangles matter (table rulings).
                    case "m":
                        if (current != null)
                        {
                            Flush(path, current, gs.Ctm);
                        }

                        current = new List<double> { Num(a, 0), Num(a, 1) };
                        Begin(index, Num(a, 0), Num(a, 1));
                        break;
                    case "l":
                        if (current != null)
                        {
                            current.Add(Num(a, 0));
                            current.Add(Num(a, 1));
                        }

                        Extend(index, Num(a, 0), Num(a, 1));
                        break;
                    case "c":
                    case "v":
                    case "y":
                        // A curve ends this straight run; continue from its end point.
                        if (current != null && a.Length >= 2)
                        {
                            Flush(path, current, gs.Ctm);
                            current = new List<double> { Num(a, a.Length - 2), Num(a, a.Length - 1) };
                        }

                        var curve = new double[a.Length];
                        for (var k = 0; k < a.Length; k++)
                        {
                            curve[k] = Num(a, k);
                        }

                        Extend(index, curve);
                        break;
                    case "re":
                        if (a.Length >= 4)
                        {
                            double x = Num(a, 0), y = Num(a, 1), w = Num(a, 2), h = Num(a, 3);
                            if (current != null)
                            {
                                Flush(path, current, gs.Ctm);
                            }

                            current = null;
                            path.Add(Rect(x, y, w, h, gs.Ctm));
                            Begin(index, x, y);
                            Extend(index, x + w, y, x + w, y + h, x, y + h);
                        }

                        break;
                    case "h":
                        if (current != null && current.Count >= 4)
                        {
                            current.Add(current[0]);
                            current.Add(current[1]);
                        }

                        Extend(index);
                        break;
                    case "W":
                    case "W*":
                        if (tracked != null)
                        {
                            tracked.Clips = true;
                        }

                        break;
                    case "S":
                    case "s":
                    case "B":
                    case "B*":
                    case "b":
                    case "b*":
                    case "f":
                    case "F":
                    case "f*":
                    case "n":
                        if (current != null)
                        {
                            Flush(path, current, gs.Ctm);
                            current = null;
                        }

                        if (op.Op != "n")
                        {
                            Paint(path, stroke: op.Op != "f" && op.Op != "F" && op.Op != "f*");
                        }

                        path.Clear();
                        if (tracked != null)
                        {
                            tracked.Paint = index;
                            if (op.Op != "n" && !tracked.Clips)
                            {
                                Paths.Add(tracked);
                            }

                            tracked = null;
                        }

                        break;
                }
            }
        }

        private static void Flush(List<double[]> path, List<double> points, double[] ctm)
        {
            if (points.Count < 4)
            {
                return;
            }

            var device = new double[points.Count];
            for (var i = 0; i + 1 < points.Count; i += 2)
            {
                Transform(ctm, points[i], points[i + 1], out device[i], out device[i + 1]);
            }

            path.Add(device);
        }

        private static double[] Rect(double x, double y, double w, double h, double[] ctm)
        {
            var p = new double[10];
            Transform(ctm, x, y, out p[0], out p[1]);
            Transform(ctm, x + w, y, out p[2], out p[3]);
            Transform(ctm, x + w, y + h, out p[4], out p[5]);
            Transform(ctm, x, y + h, out p[6], out p[7]);
            p[8] = p[0];
            p[9] = p[1];
            return p;
        }

        /// <summary>Stroked straight edges and thin filled rectangles become ruling segments.</summary>
        private void Paint(List<double[]> path, bool stroke)
        {
            foreach (var poly in path)
            {
                if (Segments.Count >= MaxSegments)
                {
                    return;
                }

                if (stroke)
                {
                    for (var i = 0; i + 3 < poly.Length; i += 2)
                    {
                        AddSegment(poly[i], poly[i + 1], poly[i + 2], poly[i + 3]);
                    }
                }
                else if (poly.Length == 10)
                {
                    double minX = Math.Min(Math.Min(poly[0], poly[2]), Math.Min(poly[4], poly[6]));
                    double maxX = Math.Max(Math.Max(poly[0], poly[2]), Math.Max(poly[4], poly[6]));
                    double minY = Math.Min(Math.Min(poly[1], poly[3]), Math.Min(poly[5], poly[7]));
                    double maxY = Math.Max(Math.Max(poly[1], poly[3]), Math.Max(poly[5], poly[7]));
                    if (maxY - minY <= 3 && maxX - minX > 3)
                    {
                        AddSegment(minX, (minY + maxY) / 2, maxX, (minY + maxY) / 2);
                    }
                    else if (maxX - minX <= 3 && maxY - minY > 3)
                    {
                        AddSegment((minX + maxX) / 2, minY, (minX + maxX) / 2, maxY);
                    }
                }
            }
        }

        private void AddSegment(double x1, double y1, double x2, double y2)
        {
            var horizontal = Math.Abs(y1 - y2) < 0.5 && Math.Abs(x1 - x2) > 1;
            var vertical = Math.Abs(x1 - x2) < 0.5 && Math.Abs(y1 - y2) > 1;
            if (horizontal || vertical)
            {
                Segments.Add(new[] { Math.Min(x1, x2), Math.Min(y1, y2), Math.Max(x1, x2), Math.Max(y1, y2) });
            }
        }

        private double[] Show(byte[] bytes, State gs, double[] tmIn, int op, int element)
        {
            var font = gs.Font;
            if (font == null || bytes.Length == 0)
            {
                return tmIn;
            }

            // Tm changes only by its translation while showing a string (§9.4.4): update it in place.
            var tm = (double[])tmIn.Clone();
            var c = gs.Ctm;
            var size = Math.Abs(gs.Tfs) * Math.Sqrt(Math.Abs((tm[0] * tm[3] - tm[1] * tm[2]) * (c[0] * c[3] - c[1] * c[2])));
            var descent = font.Descent / 1000.0 * gs.Tfs + gs.Ts;
            var top = (font.Ascent / 1000.0) * gs.Tfs + gs.Ts;
            var pos = 0;
            while (pos < bytes.Length)
            {
                var length = font.ReadCode(bytes, pos, out var code);
                var w0 = font.Width(code, length) / 1000.0;
                var spacing = gs.Tc + (font.IsWordSpace(code, length) ? gs.Tw : 0);
                var advance = (w0 * gs.Tfs + spacing) * gs.Th;
                if (Glyphs.Count < MaxGlyphs)
                {
                    var g = new GlyphInfo
                    {
                        Text = font.Unicode(code, length),
                        Font = font,
                        RenderMode = gs.Tr,
                        Op = op,
                        Element = element,
                        Offset = pos,
                        Length = length,
                        Advance = advance,
                        Th = gs.Th,
                        Tfs = gs.Tfs,
                        FontSize = size,
                    };
                    // Text space points (already scaled by Tfs/Th, plus rise) → device space via Tm then CTM.
                    TextToDevice(tm, c, 0, descent, out g.X0, out g.Y0);
                    TextToDevice(tm, c, advance, descent, out g.X1, out g.Y1);
                    TextToDevice(tm, c, 0, top, out g.TopX, out g.TopY);
                    Glyphs.Add(g);
                }

                tm[4] += advance * tm[0];
                tm[5] += advance * tm[1];
                pos += length;
            }

            return tm;
        }

        private static void TextToDevice(double[] tm, double[] c, double x, double y, out double dx, out double dy)
        {
            var ux = tm[0] * x + tm[2] * y + tm[4];
            var uy = tm[1] * x + tm[3] * y + tm[5];
            dx = c[0] * ux + c[2] * uy + c[4];
            dy = c[1] * ux + c[3] * uy + c[5];
        }

        private void DoXObject(PdfDictionary? resources, string name, double[] ctm, int op, int depth)
        {
            if (!(R(resources?.Get("XObject")) is PdfDictionary xobjects) || !(R(xobjects.Get(name)) is PdfStream stream))
            {
                return;
            }

            var subtype = (R(stream.Dictionary.Get("Subtype")) as PdfName)?.Value;
            if (subtype == "Image")
            {
                Images.Add(new ImageInfo { Stream = stream, Dictionary = stream.Dictionary, Name = name, Ctm = ctm, Op = op });
                return;
            }

            if (subtype != "Form")
            {
                return;
            }

            var matrix = R(stream.Dictionary.Get("Matrix")) is PdfArray m && m.Items.Count == 6 ? Matrix(m.Items.ToArray(), 0) : Identity();
            var formCtm = Mul(matrix, ctm);
            var formResources = R(stream.Dictionary.Get("Resources")) as PdfDictionary ?? resources;
            if (depth == 0)
            {
                var info = new FormInfo { Name = name, Stream = stream, Matrix = formCtm, Resources = formResources ?? new PdfDictionary(), Op = op };
                if (R(stream.Dictionary.Get("BBox")) is PdfArray bb && bb.Items.Count == 4)
                {
                    Bounds(formCtm, NumOf(bb.Items[0]), NumOf(bb.Items[1]), NumOf(bb.Items[2]), NumOf(bb.Items[3]), out info.X0, out info.Y0, out info.X1, out info.Y1);
                }

                Forms.Add(info);
            }

            if (!FollowForms || depth >= MaxFormDepth || !_activeForms.Add(stream))
            {
                return;
            }

            try
            {
                var data = StreamDecoder.Decode(stream, _parser, _streamLimit);
                Run(ContentReader.Read(data), formResources, formCtm, depth + 1);
            }
            catch (PdfReadException ex) when (!(ex is PdfLimitException) && !(ex is PdfEncryptedException))
            {
                // A form whose content cannot be decoded is skipped.
            }
            finally
            {
                _activeForms.Remove(stream);
            }
        }

        private FontReader? Font(PdfDictionary? resources, string name)
        {
            if (!(R(resources?.Get("Font")) is PdfDictionary fonts) || !(R(fonts.Get(name)) is PdfDictionary dict))
            {
                return null;
            }

            if (!_fonts.TryGetValue(dict, out var reader))
            {
                reader = new FontReader(dict, _parser);
                _fonts[dict] = reader;
            }

            return reader;
        }

        private PdfObject? R(PdfObject? o) => o is PdfSourceReference && _parser != null ? _parser.Resolve(o) : o;

        // ------------------------------------------------------------------ matrix helpers

        public static double[] Identity() => new double[] { 1, 0, 0, 1, 0, 0 };

        /// <summary>Product A × B (apply A, then B), row-vector convention of §8.3.4.</summary>
        public static double[] Mul(double[] a, double[] b) => new[]
        {
            a[0] * b[0] + a[1] * b[2],
            a[0] * b[1] + a[1] * b[3],
            a[2] * b[0] + a[3] * b[2],
            a[2] * b[1] + a[3] * b[3],
            a[4] * b[0] + a[5] * b[2] + b[4],
            a[4] * b[1] + a[5] * b[3] + b[5],
        };

        public static void Transform(double[] m, double x, double y, out double tx, out double ty)
        {
            tx = m[0] * x + m[2] * y + m[4];
            ty = m[1] * x + m[3] * y + m[5];
        }

        public static void Bounds(double[] m, double x0, double y0, double x1, double y1, out double minX, out double minY, out double maxX, out double maxY)
        {
            Transform(m, x0, y0, out var ax, out var ay);
            Transform(m, x1, y0, out var bx, out var by);
            Transform(m, x1, y1, out var cx, out var cy);
            Transform(m, x0, y1, out var dx, out var dy);
            minX = Math.Min(Math.Min(ax, bx), Math.Min(cx, dx));
            maxX = Math.Max(Math.Max(ax, bx), Math.Max(cx, dx));
            minY = Math.Min(Math.Min(ay, by), Math.Min(cy, dy));
            maxY = Math.Max(Math.Max(ay, by), Math.Max(cy, dy));
        }

        private static double Det(double[] m) => m[0] * m[3] - m[1] * m[2];

        private static double[] Matrix(PdfObject[] a, int start)
        {
            var m = new double[6];
            for (var i = 0; i < 6; i++)
            {
                m[i] = start + i < a.Length ? NumOf(a[start + i]) : 0;
            }

            return m;
        }

        private static double Num(PdfObject[] a, int i) => i < a.Length ? NumOf(a[i]) : 0;

        public static double NumOf(PdfObject? o)
        {
            switch (o)
            {
                case PdfInteger n: return n.Value;
                case PdfReal r: return double.IsNaN(r.Value) || double.IsInfinity(r.Value) ? 0 : r.Value;
                default: return 0;
            }
        }
    }
}
