using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace DocSense.Web;

public static partial class TextProcessing
{
    public const int MaxBytes = 8 * 1024 * 1024;
    public const int MaxCharacters = 500_000;
    private static readonly HashSet<string> StopWords = new(
        "a an and are as at be been by can could did do does for from had has have how i in into is it its me my of on or our should that the their them there these they this to us was we were what when where which who why will with would you your long please tell about".Split(' '));

    public static string SafeName(string name)
    {
        var clean = Path.GetFileName(name.Replace('\\', '/'));
        clean = new string(clean.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (clean.Length is 0 or > 180) throw new InputException("Use a filename between 1 and 180 characters.");
        return clean;
    }

    public static IReadOnlyList<TextPage> Extract(string name, byte[] bytes, CancellationToken ct)
    {
        if (bytes.Length is 0 or > MaxBytes) throw new InputException("Choose a non-empty file up to 8 MB.", 413);
        var extension = Path.GetExtension(name).ToLowerInvariant();
        if (extension is not (".pdf" or ".md" or ".txt"))
            throw new InputException("Use a PDF, Markdown (.md), or UTF-8 text (.txt) file.", 415);
        List<TextPage> pages = [];
        if (extension == ".pdf")
        {
            try
            {
                using var pdf = PdfDocument.Open(bytes);
                if (pdf.NumberOfPages > 100) throw new InputException("PDFs can contain up to 100 pages.");
                var total = 0;
                foreach (var page in pdf.GetPages())
                {
                    ct.ThrowIfCancellationRequested();
                    var text = ContentOrderTextExtractor.GetText(page);
                    total += text.Length;
                    if (total > MaxCharacters) throw new InputException("The extracted text exceeds 500,000 characters.");
                    pages.Add(new(page.Number, Normalize(text)));
                }
            }
            catch (InputException) { throw; }
            catch (OperationCanceledException) { throw; }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                throw new InputException("This PDF could not be read. Try an unencrypted PDF containing selectable text.");
            }
        }
        else
        {
            string text;
            try { text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'); }
            catch (DecoderFallbackException) { throw new InputException("Save this file as UTF-8 text and try again."); }
            if (text.Contains('\0')) throw new InputException("This file contains binary data. Choose a text document.");
            if (text.Length > MaxCharacters) throw new InputException("Text files can contain up to 500,000 characters.");
            pages.Add(new(null, Normalize(text)));
        }
        if (pages.All(p => string.IsNullOrWhiteSpace(p.Text)))
            throw new InputException("No readable text found. Scanned PDFs need OCR before upload.");
        return pages;
    }

    public static IReadOnlyList<DocumentChunk> Chunk(Guid documentId, IReadOnlyList<TextPage> pages)
    {
        List<DocumentChunk> chunks = [];
        foreach (var page in pages)
        {
            var blocks = ParagraphRegex().Split(page.Text);
            for (var blockIndex = 0; blockIndex < blocks.Length; blockIndex++)
            {
                var text = blocks[blockIndex];
                // Keep a Markdown heading with its paragraph, so retrieval returns the relevant section.
                if (HeadingRegex().IsMatch(text) && blockIndex + 1 < blocks.Length)
                    text += "\n\n" + blocks[++blockIndex];
                var start = 0;
                while (start < text.Length)
                {
                    var end = Math.Min(start + 1200, text.Length);
                    if (end < text.Length)
                    {
                        var boundary = text.LastIndexOf(' ', end - 1, end - start);
                        if (boundary > start + 800) end = boundary;
                    }
                    var content = text[start..end].Trim();
                    if (content.Length > 0) chunks.Add(new(Guid.NewGuid(), documentId, chunks.Count + 1, page.Number, content));
                    if (chunks.Count > 600) throw new InputException("This document creates too many text passages. Split it into smaller files.");
                    if (end == text.Length) break;
                    start = Math.Max(start + 1, end - 160);
                }
            }
        }
        return chunks;
    }

    public static string[] Terms(string question) => WordRegex().Matches(question.ToLowerInvariant())
        .Select(m => m.Value).Where(t => t.Length > 1 && !StopWords.Contains(t)).Distinct().Take(24).ToArray();
    public static string Query(string question) => string.Join(" | ", Terms(question).Select(t => $"'{t}'"));
    private static string Normalize(string text) => WhitespaceRegex().Replace(text.Replace("\r\n", "\n").Replace('\r', '\n'), " ").Trim();
    [GeneratedRegex(@"[\p{L}\p{Nd}]+")]
    private static partial Regex WordRegex();
    [GeneratedRegex(@"[^\S\n]+")]
    private static partial Regex WhitespaceRegex();
    [GeneratedRegex(@"\n[ \t]*\n+")]
    private static partial Regex ParagraphRegex();
    [GeneratedRegex(@"^#{1,6} [^\n]+$")]
    private static partial Regex HeadingRegex();
}
