using System;
using System.Collections.Generic;
using System.Text;
using LifeOS.Pdf.Fonts;

namespace LifeOS.Pdf.Layout.Engine
{
    /// <summary>A measured run of text in one font and style.</summary>
    internal sealed class Piece
    {
        public Piece(ResolvedStyle style, PdfFont font, string text, TextRun run, bool isSpace)
        {
            Style = style;
            Font = font;
            Text = text;
            Run = run;
            IsSpace = isSpace;
        }

        public ResolvedStyle Style { get; }

        public PdfFont Font { get; }

        public string Text { get; }

        public TextRun Run { get; }

        public bool IsSpace { get; }

        public double Width => Run.Width * Font.Size / 1000.0;
    }

    internal enum AtomKind
    {
        Word,
        Space,
        Break,
    }

    /// <summary>The unit of line breaking: lines may only break between atoms.</summary>
    internal sealed class Atom
    {
        public Atom(AtomKind kind, ResolvedStyle style)
        {
            Kind = kind;
            Style = style;
        }

        public AtomKind Kind { get; }

        /// <summary>Style used for line height when the atom has no pieces (a line break).</summary>
        public ResolvedStyle Style { get; }

        public List<Piece> Pieces { get; } = new List<Piece>();

        /// <summary>Ends in a soft hyphen: show "-" if the line breaks here.</summary>
        public bool HyphenIfBroken { get; set; }

        public double Width
        {
            get
            {
                double w = 0;
                foreach (var p in Pieces)
                {
                    w += p.Width;
                }

                return w;
            }
        }
    }

    /// <summary>One laid-out line of a paragraph.</summary>
    internal sealed class TextLine
    {
        public List<Piece> Pieces { get; } = new List<Piece>();

        /// <summary>Last line of the paragraph or ended by a line break: never justified.</summary>
        public bool IsHardEnd { get; set; }

        public bool IsFirst { get; set; }

        public double Width
        {
            get
            {
                double w = 0;
                foreach (var p in Pieces)
                {
                    w += p.Width;
                }

                return w;
            }
        }

        public double Height { get; set; }

        /// <summary>Distance from the top of the line to its baseline.</summary>
        public double Baseline { get; set; }
    }

    /// <summary>
    /// Turns styled text into atoms with break opportunities (a practical subset of
    /// Unicode UAX #14): spaces, after hyphens, soft hyphen, zero-width space, between
    /// CJK characters; no break at no-break space or before closing punctuation.
    /// </summary>
    internal sealed class AtomBuilder
    {
        private readonly LayoutContext _ctx;
        private readonly List<Atom> _atoms = new List<Atom>();
        private readonly StringBuilder _pieceText = new StringBuilder();
        private Atom? _word;
        private ResolvedStyle? _pieceStyle;
        private PdfFontFace? _pieceFace;
        private bool _pieceIsSpace;

        public AtomBuilder(LayoutContext ctx)
        {
            _ctx = ctx;
        }

        public List<Atom> Atoms => _atoms;

        public void Add(string text, ResolvedStyle style)
        {
            for (var i = 0; i < text.Length; i++)
            {
                int cp = text[i];
                var length = 1;
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    cp = char.ConvertToUtf32(text[i], text[i + 1]);
                    length = 2;
                }

                AddCodePoint(cp, text, i, length, style);
                i += length - 1;
            }
        }

        public void AddBreak(ResolvedStyle style)
        {
            FlushWord();
            _atoms.Add(new Atom(AtomKind.Break, style));
        }

        public List<Atom> Finish()
        {
            FlushWord();
            return _atoms;
        }

        private void AddCodePoint(int cp, string s, int start, int length, ResolvedStyle style)
        {
            switch (cp)
            {
                case '\r':
                    return;
                case '\n':
                    AddBreak(style);
                    return;
                case ' ':
                case '\t':
                    AddSpace(style);
                    return;
                case 0x200B: // zero-width space: break opportunity, nothing drawn
                    FlushWord();
                    return;
                case 0x00AD: // soft hyphen
                    if (_word != null)
                    {
                        _word.HyphenIfBroken = true;
                    }

                    FlushWord();
                    return;
            }

            if (IsCjk(cp))
            {
                if (NoBreakBefore(cp))
                {
                    // Closing punctuation stays with the previous character.
                    AppendToWord(cp, s, start, length, style);
                    FlushWord();
                    return;
                }

                FlushWord();
                AppendToWord(cp, s, start, length, style);
                FlushWord();
                return;
            }

            if (NoBreakBefore(cp) && _word == null && _atoms.Count > 0 && _atoms[_atoms.Count - 1].Kind == AtomKind.Word)
            {
                // e.g. "。" or ")" right after a CJK atom: reopen it.
                _word = _atoms[_atoms.Count - 1];
                _atoms.RemoveAt(_atoms.Count - 1);
            }

            AppendToWord(cp, s, start, length, style);
            if (cp == '-' || cp == 0x2010 || cp == 0x2013 || cp == 0x2014)
            {
                FlushWord(); // break allowed after a hyphen or dash
            }
        }

        private void AddSpace(ResolvedStyle style)
        {
            FlushWord();
            var last = _atoms.Count > 0 ? _atoms[_atoms.Count - 1] : null;
            var atom = last != null && last.Kind == AtomKind.Space ? last : null;
            if (atom == null)
            {
                atom = new Atom(AtomKind.Space, style);
                _atoms.Add(atom);
            }

            atom.Pieces.Add(Measure(" ", style, style.PrimaryFace, isSpace: true));
        }

        private void AppendToWord(int cp, string s, int start, int length, ResolvedStyle style)
        {
            if (_word == null)
            {
                _word = new Atom(AtomKind.Word, style);
            }

            var face = style.FaceFor(cp);
            if (_pieceText.Length > 0 && (!ReferenceEquals(_pieceStyle, style) || !ReferenceEquals(_pieceFace, face) || _pieceIsSpace))
            {
                FlushPiece();
            }

            _pieceStyle = style;
            _pieceFace = face;
            _pieceIsSpace = false;
            _pieceText.Append(s, start, length);
        }

        private void FlushPiece()
        {
            if (_pieceText.Length > 0 && _word != null)
            {
                _word.Pieces.Add(Measure(_pieceText.ToString(), _pieceStyle!, _pieceFace!, isSpace: false));
            }

            _pieceText.Clear();
        }

        private void FlushWord()
        {
            FlushPiece();
            if (_word != null && _word.Pieces.Count > 0)
            {
                _atoms.Add(_word);
            }

            _word = null;
        }

        private Piece Measure(string text, ResolvedStyle style, PdfFontFace face, bool isSpace)
        {
            var font = _ctx.Font(face, style.Size);
            return new Piece(style, font, text, _ctx.Map(font, text, style.Kerning), isSpace);
        }

        internal static bool IsCjk(int cp) =>
            (cp >= 0x3000 && cp <= 0x303F) || // CJK punctuation
            (cp >= 0x3040 && cp <= 0x30FF) || // Hiragana, Katakana
            (cp >= 0x3400 && cp <= 0x4DBF) ||
            (cp >= 0x4E00 && cp <= 0x9FFF) ||
            (cp >= 0xAC00 && cp <= 0xD7AF) || // Hangul syllables
            (cp >= 0xF900 && cp <= 0xFAFF) ||
            (cp >= 0xFF00 && cp <= 0xFFEF) || // full-width forms
            (cp >= 0x20000 && cp <= 0x2FFFF);

        private static bool NoBreakBefore(int cp) =>
            cp == 0x3001 || cp == 0x3002 || cp == 0xFF0C || cp == 0xFF0E || cp == 0xFF01 || cp == 0xFF1F ||
            cp == 0x300D || cp == 0x300F || cp == 0xFF09 || cp == 0x3011 || cp == 0x30FC ||
            cp == 0x3063 || cp == 0x30C3; // 、。，．！？」』）】ー っ ッ
    }

    /// <summary>Greedy first-fit line breaking.</summary>
    internal static class LineBreaker
    {
        public static List<TextLine> Break(List<Atom> atoms, double firstLimit, double limit, LayoutContext ctx)
        {
            var lines = new List<TextLine>();
            var line = new TextLine { IsFirst = true };
            var pendingSpaces = new List<Piece>();
            double width = 0;
            Atom? lastWord = null;

            void Finish(bool hard)
            {
                if (!hard && lastWord != null && lastWord.HyphenIfBroken)
                {
                    var style = lastWord.Pieces[lastWord.Pieces.Count - 1].Style;
                    var font = ctx.Font(style.PrimaryFace, style.Size);
                    line.Pieces.Add(new Piece(style, font, "-", font.Map("-"), false));
                }

                line.IsHardEnd = hard;
                lines.Add(line);
                line = new TextLine();
                pendingSpaces.Clear();
                width = 0;
                lastWord = null;
            }

            foreach (var atom in atoms)
            {
                var current = lines.Count == 0 ? firstLimit : limit;
                switch (atom.Kind)
                {
                    case AtomKind.Break:
                        if (line.Pieces.Count == 0)
                        {
                            // Empty line: remember a font for its height.
                            line.Pieces.Add(EmptyPiece(atom.Style, ctx));
                        }

                        Finish(hard: true);
                        break;

                    case AtomKind.Space:
                        if (line.Pieces.Count > 0)
                        {
                            pendingSpaces.AddRange(atom.Pieces);
                        }

                        break;

                    default:
                        double spaceWidth = 0;
                        foreach (var sp in pendingSpaces)
                        {
                            spaceWidth += sp.Width;
                        }

                        var w = atom.Width;
                        if (line.Pieces.Count > 0 && width + spaceWidth + w > current + 0.001)
                        {
                            Finish(hard: false);
                            current = limit;
                            spaceWidth = 0;
                        }

                        if (line.Pieces.Count == 0 && w > current + 0.001)
                        {
                            // A word wider than the line: split it by characters.
                            var chunks = SplitByCharacters(atom, current, limit, ctx);
                            for (var c = 0; c < chunks.Count; c++)
                            {
                                line.Pieces.AddRange(chunks[c].Pieces);
                                width = chunks[c].Width;
                                if (c < chunks.Count - 1)
                                {
                                    Finish(hard: false);
                                }
                            }

                            lastWord = atom.HyphenIfBroken ? chunks[chunks.Count - 1] : null;
                            if (atom.HyphenIfBroken)
                            {
                                chunks[chunks.Count - 1].HyphenIfBroken = true;
                            }

                            break;
                        }

                        line.Pieces.AddRange(pendingSpaces);
                        pendingSpaces.Clear();
                        line.Pieces.AddRange(atom.Pieces);
                        width += spaceWidth + w;
                        lastWord = atom;
                        break;
                }
            }

            if (line.Pieces.Count > 0 || lines.Count == 0 || lines[lines.Count - 1].IsHardEnd == false)
            {
                if (line.Pieces.Count == 0)
                {
                    line.Pieces.Add(EmptyPiece(atoms.Count > 0 ? atoms[atoms.Count - 1].Style : null, ctx));
                }

                lastWord = null; // a paragraph's last line never gets a soft-hyphen dash
                Finish(hard: true);
            }

            if (lines.Count > 0)
            {
                lines[0].IsFirst = true;
            }

            return lines;
        }

        /// <summary>A zero-width piece that only carries font metrics (empty lines).</summary>
        internal static Piece EmptyPiece(ResolvedStyle? style, LayoutContext ctx)
        {
            var s = style ?? new ResolvedStyle(ctx.BaseStyle);
            var font = ctx.Font(s.PrimaryFace, s.Size);
            return new Piece(s, font, string.Empty, font.Map(string.Empty), false);
        }

        private static List<Atom> SplitByCharacters(Atom atom, double firstLimit, double limit, LayoutContext ctx)
        {
            var result = new List<Atom>();
            var current = new Atom(AtomKind.Word, atom.Style);
            double width = 0;
            var max = firstLimit;
            foreach (var piece in atom.Pieces)
            {
                var text = piece.Text;
                for (var i = 0; i < text.Length; i++)
                {
                    var len = char.IsHighSurrogate(text[i]) && i + 1 < text.Length ? 2 : 1;
                    var ch = text.Substring(i, len);
                    i += len - 1;
                    var font = piece.Font;
                    var run = font.Map(ch, piece.Style.Kerning);
                    var w = run.Width * font.Size / 1000.0;
                    if (current.Pieces.Count > 0 && width + w > max + 0.001)
                    {
                        result.Add(Merge(current, ctx));
                        current = new Atom(AtomKind.Word, atom.Style);
                        width = 0;
                        max = limit;
                    }

                    current.Pieces.Add(new Piece(piece.Style, font, ch, run, false));
                    width += w;
                }
            }

            if (current.Pieces.Count > 0)
            {
                result.Add(Merge(current, ctx));
            }

            return result;
        }

        /// <summary>Joins single-character pieces of the same font back into longer runs.</summary>
        private static Atom Merge(Atom atom, LayoutContext ctx)
        {
            var merged = new Atom(AtomKind.Word, atom.Style);
            var text = new StringBuilder();
            Piece? first = null;
            foreach (var p in atom.Pieces)
            {
                if (first != null && (!ReferenceEquals(first.Style, p.Style) || !ReferenceEquals(first.Font, p.Font)))
                {
                    merged.Pieces.Add(new Piece(first.Style, first.Font, text.ToString(), first.Font.Map(text.ToString(), first.Style.Kerning), false));
                    text.Clear();
                }

                if (text.Length == 0)
                {
                    first = p;
                }

                text.Append(p.Text);
            }

            if (first != null)
            {
                merged.Pieces.Add(new Piece(first.Style, first.Font, text.ToString(), first.Font.Map(text.ToString(), first.Style.Kerning), false));
            }

            return merged;
        }
    }
}
