using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using FluentAssertions;
using Teleprompter.Core.Text;

namespace Teleprompter.Core.Tests;

public sealed class DocxReaderTests : IDisposable
{
    private readonly string _tempPath = Path.Combine(
        Path.GetTempPath(), $"docx-reader-test-{Guid.NewGuid():N}.docx");

    public void Dispose()
    {
        if (File.Exists(_tempPath))
        {
            File.Delete(_tempPath);
        }
    }

    private const string WordNs = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private string WriteDocx(string documentXml)
    {
        using FileStream fs = File.Create(_tempPath);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);

        ZipArchiveEntry types = zip.CreateEntry("[Content_Types].xml");
        using (var writer = new StreamWriter(types.Open(), Encoding.UTF8))
        {
            writer.Write("""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="xml" ContentType="application/xml"/>
                </Types>
                """);
        }

        ZipArchiveEntry doc = zip.CreateEntry("word/document.xml");
        using (var writer = new StreamWriter(doc.Open(), Encoding.UTF8))
        {
            writer.Write(documentXml);
        }

        return _tempPath;
    }

    [Fact]
    public void ExtractText_ReadsParagraphsAsLines()
    {
        string path = WriteDocx($"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="{WordNs}"><w:body>
              <w:p><w:r><w:t>Hello there.</w:t></w:r></w:p>
              <w:p><w:r><w:t>Second </w:t></w:r><w:r><w:t>paragraph.</w:t></w:r></w:p>
            </w:body></w:document>
            """);

        DocxReader.ExtractText(path).Should().Be("Hello there.\nSecond paragraph.");
    }

    [Fact]
    public void ExtractText_EmptyParagraph_BecomesBlankLine_ForSectionJumps()
    {
        string path = WriteDocx($"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="{WordNs}"><w:body>
              <w:p><w:r><w:t>Intro part.</w:t></w:r></w:p>
              <w:p/>
              <w:p><w:r><w:t>Main part.</w:t></w:r></w:p>
            </w:body></w:document>
            """);

        string text = DocxReader.ExtractText(path);

        text.Should().Be("Intro part.\n\nMain part.");
        ScriptModel.Build(text).ParagraphStartTokens.Should().HaveCount(2,
            "the Word blank line must produce a second prompter section");
    }

    [Fact]
    public void ExtractText_NonZipFile_ThrowsHelpfulError()
    {
        File.WriteAllText(_tempPath, "this is not a zip");

        Action act = () => DocxReader.ExtractText(_tempPath);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*save it as .docx or .txt*");
    }
}
