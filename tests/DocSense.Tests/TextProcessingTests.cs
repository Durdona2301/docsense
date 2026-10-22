using System.Text;
using DocSense.Web;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace DocSense.Tests;

public sealed class TextProcessingTests
{
    [Fact]
    public void PdfExtractionPreservesActualPages()
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        builder.AddPage(PageSize.A4).AddText("Trial lasts fourteen days.", 12, new PdfPoint(50, 700), font);
        builder.AddPage(PageSize.A4).AddText("Support is available on weekdays.", 12, new PdfPoint(50, 700), font);
        var pages = TextProcessing.Extract("test.pdf", builder.Build(), default);
        Assert.Equal(2, pages.Count);
        Assert.Equal(1, pages[0].Number);
        Assert.Equal(2, pages[1].Number);
        Assert.Contains("fourteen", pages[0].Text);
        Assert.Contains("weekdays", pages[1].Text);
    }
    [Theory]
    [InlineData("report.md")]
    [InlineData("report.TXT")]
    public void ReadsUtf8Text(string name)
    {
        var pages = TextProcessing.Extract(name, Encoding.UTF8.GetBytes("Salom\n\n dunyo — ҳуҷҷат"), default);
        Assert.Equal("Salom\n\n dunyo — ҳуҷҷат", Assert.Single(pages).Text);
    }

    [Theory]
    [InlineData("photo.png")]
    [InlineData("index.html")]
    [InlineData("run.exe")]
    public void RejectsUnsupportedTypes(string name) => Assert.Equal(415,
        Assert.Throws<InputException>(() => TextProcessing.Extract(name, [1, 2], default)).Status);

    [Fact]
    public void RejectsInvalidUtf8() => Assert.Throws<InputException>(() => TextProcessing.Extract("a.txt", [0xff, 0xfe], default));
    [Fact]
    public void RejectsBinaryText() => Assert.Throws<InputException>(() => TextProcessing.Extract("a.txt", [65, 0, 66], default));
    [Fact]
    public void RejectsEmptyText() => Assert.Throws<InputException>(() => TextProcessing.Extract("a.md", [32, 10], default));
    [Fact]
    public void RejectsOversizedFile() => Assert.Equal(413,
        Assert.Throws<InputException>(() => TextProcessing.Extract("a.txt", new byte[TextProcessing.MaxBytes + 1], default)).Status);
    [Fact]
    public void RejectsExcessiveExtractedText() => Assert.Throws<InputException>(() =>
        TextProcessing.Extract("a.txt", Encoding.UTF8.GetBytes(new string('a', TextProcessing.MaxCharacters + 1)), default));
    [Fact]
    public void RejectsCorruptPdfWithHelpfulError() => Assert.Contains("PDF", Assert.Throws<InputException>(() =>
        TextProcessing.Extract("a.pdf", Encoding.UTF8.GetBytes("not a PDF"), default)).Message);

    [Theory]
    [InlineData("../../private/report.md", "report.md")]
    [InlineData("C:\\private\\report.txt", "report.txt")]
    [InlineData("my\nreport.md", "myreport.md")]
    public void StripsPathsAndControls(string raw, string expected) => Assert.Equal(expected, TextProcessing.SafeName(raw));

    [Fact]
    public void ChunksPreservePageNumbersAndCoverAllText()
    {
        var text = string.Join(' ', Enumerable.Range(0, 1000).Select(i => $"word{i}"));
        var id = Guid.NewGuid();
        var chunks = TextProcessing.Chunk(id, [new(1, text), new(2, "Second page")]);
        Assert.All(chunks, c => { Assert.Equal(id, c.DocumentId); Assert.InRange(c.Content.Length, 1, 1200); });
        Assert.Equal(Enumerable.Range(1, chunks.Count), chunks.Select(c => c.Ordinal));
        Assert.Equal(2, chunks[^1].Page);
        Assert.Equal("Second page", chunks[^1].Content);
        for (var i = 0; i < 1000; i++) Assert.Contains(chunks, c => c.Content.Contains($"word{i}"));
        Assert.EndsWith("word999", chunks[^2].Content);
    }

    [Fact]
    public void QueryIsBoundedAndRemovesOperators()
    {
        Assert.Equal(["trial", "period"], TextProcessing.Terms("How long is the trial period?"));
        Assert.Equal("'drop' | 'table'", TextProcessing.Query("DROP'; TABLE --"));
        Assert.Empty(TextProcessing.Terms("the and what is"));
        Assert.True(TextProcessing.Terms(string.Join(' ', Enumerable.Range(10, 100))).Length <= 24);
    }

    [Fact]
    public void MarkdownSectionsDoNotMixUnrelatedTopics()
    {
        var pages = TextProcessing.Extract("handbook.md", Encoding.UTF8.GetBytes(
            "## Trial\n\nThe trial lasts 14 days.\n\n## Export\n\nExports use CSV."), default);
        var chunks = TextProcessing.Chunk(Guid.NewGuid(), pages);
        Assert.Equal(2, chunks.Count);
        Assert.Contains("## Trial", chunks[0].Content);
        Assert.DoesNotContain("CSV", chunks[0].Content);
        Assert.Contains("## Export", chunks[1].Content);
    }
}
