using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Services.Extraction;

// One filing slice sent to a worker. Section identifies its source, Item groups it in the widget,
// and Titles lists any sub-headings bundled into the call.
public record FilingChunk(string Section, string Text, string Item = "", IReadOnlyList<string>? Titles = null);

// A bold sub-heading within a target Item and its body text.
public record FilingHeading(string Section, string Title, string Body);

// Converts raw SEC filings into targeted, size-limited plain-text extraction chunks.
public static class FilingSections
{
    // Maps each node to its relevant annual-report SEC Items, ordered so high-priority sections receive
    // chunk capacity first.
    public static string[] ItemsFor(ExtractionNode node) => node switch
    {
        ExtractionNode.RISK => ["1A", "7A"],
        // COST includes Item 1 because supplier and raw-material disclosures may not appear in the notes.
        ExtractionNode.COST => ["1", "7", "8"],
        // Revenue spans Items 1, 1A, 7, and 8 because named customers and commercial relationships
        // can appear in the business, concentration-risk, MD&A, and financial-note narratives.
        _ => ["1", "1A", "7", "8"],
    };

    public const int MaxChunkChars = 4000;         // ~1k tokens/chunk — the per-worker text budget
    private const int MaxChunksPerSection = 12;    // keep one giant section from hogging every slot
    // Caps worker calls so later high-value Items are not starved and malformed filings cannot create
    // unbounded scans.
    public const int MaxScanChunks = 48;

    public static List<FilingChunk> Build(string raw, string[] items)
    {
        var text = ToText(raw);
        var chunks = new List<FilingChunk>();
        foreach (var item in items)
        {
            var body = SectionBody(text, item);
            if (body is null) continue;
            int n = 0;
            foreach (var chunk in Paragraphs(body))
            {
                chunks.Add(new FilingChunk($"Item {item}", chunk, $"Item {item}"));
                if (++n >= MaxChunksPerSection) break;          // fair share to the next section
                if (chunks.Count >= MaxScanChunks) return chunks;
            }
        }
        return chunks;
    }

    // Builds sequential chunks when headings are unreliable. Ranks excess chunks because important
    // financial tables often appear late in an Item.
    public static List<FilingChunk> BuildSection(string raw, string item, ExtractionNode node, int maxChunks = 40)
    {
        var body = SectionBody(ToText(raw), item);
        if (body is null) return [];
        var chunks = Paragraphs(body)
            .Select(text => new FilingChunk($"Item {item}", text, $"Item {item}"))
            .ToList();
        return RankChunks(chunks, node, maxChunks);
    }

    // Prefer table chunks over keyword-heavy prose because extraction targets usually live in tables.

    // Keeps the most relevant chunks, then restores document order. Deterministic scoring avoids an
    // extra model call and keeps plans testable.
    public static List<FilingChunk> RankChunks(IReadOnlyList<FilingChunk> chunks, ExtractionNode node, int take)
    {
        if (chunks.Count <= take) return chunks.ToList();
        return chunks
            .Select((chunk, index) => (chunk, index, score: Relevance(chunk.Text, node)))
            .OrderByDescending(x => x.score)
            .ThenBy(x => x.index)        // ties fall back to document order, so ranking is stable
            .Take(take)
            .OrderBy(x => x.index)       // ...but the workers still read the survivors in filing order
            .Select(x => x.chunk)
            .ToList();
    }

    // Distinct keyword hits (not occurrences — a paragraph repeating "segment" ten times is not ten
    // times as relevant as one naming both a segment and a customer), plus the table bonus.
    private static int Relevance(string text, ExtractionNode node)
    {
        var score = 0;
        foreach (var keyword in Keywords(node))
            if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase)) score++;
        return score;
    }

    // Shared topic words used to rank chunks and select node-relevant detail reports.
    private static string[] Keywords(ExtractionNode node) => node switch
    {
        // Omit "risk" because it matches all of Item 1A; specific exposure terms provide useful ranking.
        ExtractionNode.RISK =>
            ["Concentration", "Depend", "Single Source", "Sole Source", "Litigation", "Regulat",
             "Interest Rate", "Foreign Currency", "Exchange Rate", "Cyber", "Supply", "Tariff"],
        ExtractionNode.COST =>
            ["Supplier", "Vendor", "Manufactur", "Foundry", "Purchase", "Supply", "License", "Service Provider"],
        _ =>
            ["Customer", "Buyer", "Distributor", "Reseller", "Licensee", "Commercial Partner",
             "Joint Venture", "Concentration", "Depend"],
    };


    // Checks whether the input looks like HTML rather than a plain-text filing.
    private static bool LooksHtml(string raw) =>
        Regex.IsMatch(raw[..Math.Min(raw.Length, 2000)], "<html|<body|<div|<p|<table", RegexOptions.IgnoreCase);
    // Converts HTML to readable text without interpreting table rows or columns. Cell boundaries get
    // whitespace only so layout-table prose remains readable while financial grids receive no special treatment.
    private static string ToText(string raw)
    {
        if (LooksHtml(raw))
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(raw);
            doc.DocumentNode.SelectNodes("//script|//style|//head")?.ToList().ForEach(n => n.Remove());

            // Preserve ordinary visual boundaries, but do not reconstruct or classify tables.
            var marked = Regex.Replace(doc.DocumentNode.OuterHtml, "(?i)</(td|th)>", " ");
            marked = Regex.Replace(marked, "(?i)</(p|div|tr|li|h[1-6]|table)>", "\n");
            marked = Regex.Replace(marked, "(?i)<br\\s*/?>", "\n");
            var flat = new HtmlDocument();
            flat.LoadHtml(marked);
            raw = HtmlEntity.DeEntitize(flat.DocumentNode.InnerText) ?? "";
        }

        var lines = raw.Replace("\r", "").Split('\n')
            .Select(line => Regex.Replace(line, "[ \t ]+", " ").Trim());
        var sb = new StringBuilder();
        var blanks = 0;
        foreach (var line in lines)
        {
            if (line.Length == 0)
            {
                blanks++;
                if (blanks <= 1) sb.Append('\n');
                continue;
            }
            blanks = 0;
            sb.Append(line).Append('\n');
        }
        return sb.ToString();
    }

    // Maps canonical Reg S-K titles to Items because body headings may omit Item numbers. Item 1 is
    // excluded because the generic title "Business" creates too many false matches.
    private static readonly (string Num, string Title)[] ItemTitles =
    [
        ("1A", @"Risk\s+Factors"),
        ("7A", @"Quantitative\s+and\s+Qualitative\s+Disclosures?\s+About\s+Market\s+Risk"),
        ("7",  @"Management'?.?s\s+Discussion\s+and\s+Analysis(\s+of\s+Financial\s+Condition.*)?"),
        ("8",  @"Financial\s+Statements\s+and\s+Supplementary\s+Data"),
    ];

    // Matches an Item heading at the start of a line, including decimal 8-K numbers such as 2.02.
    private const string ItemHeadingPattern = @"^[#>*_\s]*Item\s+(\d+(?:\.\d+)?[A-Z]?)\b";

    // Returns the Item headed by this line. Title matches must occupy the full line so inline references
    // are not treated as section boundaries.
    private static string? ItemNumberOf(string line)
    {
        var m = Regex.Match(line, ItemHeadingPattern, RegexOptions.IgnoreCase);
        if (m.Success) return m.Groups[1].Value.ToUpperInvariant();

        foreach (var (num, title) in ItemTitles)
            if (Regex.IsMatch(line, $@"^[#>*_\s]*{title}[.:\s]*$", RegexOptions.IgnoreCase))
                return num;
        return null;
    }

    // Every Item boundary in the document, in order — by number and by canonical title.
    private static List<(string Num, int Start, int End)> Boundaries(string text)
    {
        var found = new List<(string Num, int Start, int End)>();

        foreach (Match m in Regex.Matches(text, $"(?im){ItemHeadingPattern}"))
            found.Add((m.Groups[1].Value.ToUpperInvariant(), m.Index, m.Index + m.Length));

        foreach (var (num, title) in ItemTitles)
            foreach (Match m in Regex.Matches(text, $@"(?im)^[#>*_\s]*{title}[.:\s]*$"))
                found.Add((num, m.Index, m.Index + m.Length));

        return found.OrderBy(h => h.Start).ToList();
    }

    // Choose the longest occurrence to distinguish the body from TOC entries. Stop only at a different
    // Item because filings may repeat the current title as a running header.
    private static string? SectionBody(string text, string item)
    {
        var headings = Boundaries(text);
        if (headings.Count == 0) return null;

        string? best = null;
        for (int i = 0; i < headings.Count; i++)
        {
            if (headings[i].Num != item) continue;
            var bodyStart = headings[i].End;
            var next = headings.FindIndex(i + 1, h => h.Num != item);
            var bodyEnd = next >= 0 ? headings[next].Start : text.Length;
            var body = text[bodyStart..bodyEnd].Trim();
            if (best is null || body.Length > best.Length) best = body;
        }
        return string.IsNullOrWhiteSpace(best) ? null : best;
    }

    // Packs plain-text paragraphs into bounded chunks. Oversized paragraphs are split instead of truncated.
    private static IEnumerable<string> Paragraphs(string body)
    {
        var paragraphs = Regex.Split(body, "\n\\s*\n")
            .Select(paragraph => paragraph.Trim())
            .Where(paragraph => paragraph.Length > 0);
        var current = new StringBuilder();

        foreach (var paragraph in paragraphs)
        {
            for (var offset = 0; offset < paragraph.Length; offset += MaxChunkChars)
            {
                var length = Math.Min(MaxChunkChars, paragraph.Length - offset);
                var piece = paragraph.Substring(offset, length);
                if (current.Length > 0 && current.Length + 2 + piece.Length > MaxChunkChars)
                {
                    yield return current.ToString();
                    current.Clear();
                }
                if (current.Length > 0) current.Append("\n\n");
                current.Append(piece);
            }
        }

        if (current.Length > 0) yield return current.ToString();
    }

    // ── Heading-level view: bold sub-headings inside Items 7/8/1A + the paragraphs under each ──

    private const int HeadingMaxChars = 400;       // a heading is a full bold line (often a sentence)
    private const int HeadingBodyMaxChars = 6000;  // ~1.5k tokens for the worker that reads it
    private const int MaxHeadings = 120;           // safety cap on how many we surface

    // Tags that start a new visual line; a heading is one such line whose text is entirely bold.
    private static readonly HashSet<string> BlockTags =
        ["p", "div", "li", "tr", "table", "ul", "ol", "h1", "h2", "h3", "h4", "h5", "h6"];

    // Extracts bold sub-headings and their bodies for focused worker calls. Plain-text filings use the
    // line-based fallback because they lack bold markup.
    public static List<FilingHeading> BuildHeadings(string raw, string[] items)
    {
        // Plain-text filings use line-based headings because they have no bold markup.
        if (!LooksHtml(raw)) return BuildHeadingsFromMarkdown(raw, items);

        var doc = new HtmlDocument();
        doc.LoadHtml(raw);
        doc.DocumentNode.SelectNodes("//script|//style|//head")?.ToList().ForEach(n => n.Remove());

        // Flatten the document into visual lines, each tagged with whether its whole text is bold.
        var lines = new List<(string Text, bool Bold)>();
        var acc = new LineAcc();
        CollectLines(doc.DocumentNode, lines, acc);
        FlushLine(lines, acc);

        var result = new List<FilingHeading>();
        string? section = null;            // current Item (null when outside the target items)
        string? title = null;
        var body = new StringBuilder();

        void Flush()
        {
            if (title is not null && section is not null && body.Length > 0)
                result.Add(new FilingHeading(section, title, body.ToString().Trim()));
            title = null;
            body.Clear();
        }

        foreach (var (text, bold) in lines)
        {
            // An Item line — by number or by canonical title — is a section boundary, not a
            // selectable sub-heading.
            if (ItemNumberOf(text) is { } num)
            {
                Flush();
                if (Array.IndexOf(items, num) >= 0)
                {
                    section = $"Item {num}";
                    title = text;   // capture the lead-in before the first sub-heading (e.g. Item 8 tables)
                }
                else section = null;
                continue;
            }

            if (section is null) continue;   // outside the revenue-relevant items

            if (bold && text.Length <= HeadingMaxChars && text.Any(char.IsLetter))
            {
                Flush();
                title = text;
            }
            else if (title is not null && body.Length < HeadingBodyMaxChars)
            {
                body.Append(text).Append('\n');
            }
        }
        Flush();

        // Dedupe (TOC + body can both yield a heading); keep the one with the longer body.
        return result
            .GroupBy(h => $"{h.Section}|{h.Title}")
            .Select(g => g.OrderByDescending(h => h.Body.Length).First())
            .Take(MaxHeadings)
            .ToList();
    }

    // Plain-text heading extraction mirrors the HTML path using Markdown heading markers.
    private static List<FilingHeading> BuildHeadingsFromMarkdown(string raw, string[] items)
    {
        var result = new List<FilingHeading>();
        string? section = null;            // current Item (null when outside the target items)
        string? title = null;
        var body = new StringBuilder();

        void Flush()
        {
            if (title is not null && section is not null && body.Length > 0)
                result.Add(new FilingHeading(section, title, body.ToString().Trim()));
            title = null;
            body.Clear();
        }

        foreach (var rawLine in raw.Replace("\r", "").Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;

            // An Item line — by number or by canonical title — is a section boundary, not a sub-heading.
            if (ItemNumberOf(line) is { } num)
            {
                Flush();
                if (Array.IndexOf(items, num) >= 0)
                {
                    section = $"Item {num}";
                    // Seed the Item title so content before the first sub-heading is retained.
                    title = StripInline(line);
                }
                else section = null;
                continue;
            }

            if (section is null) continue;   // outside the revenue-relevant items

            var heading = MarkdownHeadingText(line);
            if (heading is not null && heading.Length <= HeadingMaxChars && heading.Any(char.IsLetter))
            {
                Flush();
                title = heading;
            }
            else if (title is not null && body.Length < HeadingBodyMaxChars)
            {
                body.Append(line).Append('\n');
            }
        }
        Flush();

        // Dedupe (a TOC and the body can both yield a heading); keep the one with the longer body.
        return result
            .GroupBy(h => $"{h.Section}|{h.Title}")
            .Select(g => g.OrderByDescending(h => h.Body.Length).First())
            .Take(MaxHeadings)
            .ToList();
    }

    // The heading text if this markdown line is a heading — an ATX line ("#…# Title") or a line that
    // is entirely bold ("**Title**") — else null. Inline markers are stripped so triage sees a clean title.
    private static string? MarkdownHeadingText(string line)
    {
        var atx = Regex.Match(line, @"^#{1,6}\s+(.+?)\s*#*$");
        if (atx.Success) return StripInline(atx.Groups[1].Value);

        var bold = Regex.Match(line, @"^\*\*(.+?)\*\*$");
        if (bold.Success && !bold.Groups[1].Value.Contains("**")) return StripInline(bold.Groups[1].Value);

        return null;
    }

    private static string StripInline(string s) => Regex.Replace(s, @"[*_`]", "").Trim();

    private sealed class LineAcc
    {
        public readonly StringBuilder Sb = new();
        public bool AllBold = true;   // ANDed with each text run; a line with one non-bold run isn't a heading
        public bool HasText;
    }

    // Depth-first walk that emits visual lines at block boundaries and line breaks while tracking boldness.
    private static void CollectLines(HtmlNode node, List<(string, bool)> lines, LineAcc acc)
    {
        foreach (var child in node.ChildNodes)
        {
            if (child.NodeType == HtmlNodeType.Text)
            {
                var t = HtmlEntity.DeEntitize(child.InnerText) ?? "";
                if (t.Trim().Length == 0) { acc.Sb.Append(' '); continue; }
                acc.Sb.Append(t);
                acc.AllBold &= HasBoldAncestor(child);
                acc.HasText = true;
            }
            else if (string.Equals(child.Name, "br", StringComparison.OrdinalIgnoreCase))
            {
                FlushLine(lines, acc);
            }
            else if (BlockTags.Contains(child.Name))
            {
                FlushLine(lines, acc);
                CollectLines(child, lines, acc);
                FlushLine(lines, acc);
            }
            else
            {
                CollectLines(child, lines, acc);   // inline element (span, b, font, i, a…)
            }
        }
    }

    private static void FlushLine(List<(string, bool)> lines, LineAcc acc)
    {
        var text = Regex.Replace(acc.Sb.ToString(), "\\s+", " ").Trim();
        if (text.Length > 0 && acc.HasText) lines.Add((text, acc.AllBold));
        acc.Sb.Clear();
        acc.AllBold = true;
        acc.HasText = false;
    }

    private static bool HasBoldAncestor(HtmlNode textNode)
    {
        for (var a = textNode.ParentNode; a is not null; a = a.ParentNode)
        {
            if (a.Name is "b" or "strong") return true;
            var style = a.GetAttributeValue("style", "").ToLowerInvariant();
            if (style.Contains("font-weight") &&
                (style.Contains("bold") || style.Contains("600") || style.Contains("700") ||
                 style.Contains("800") || style.Contains("900")))
                return true;
        }
        return false;
    }
}
