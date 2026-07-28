using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Teleprompter.Core.Text;

/// <summary>
/// Extracts plain text from a Word document (.docx) for use as a script.
///
/// A .docx is a zip containing word/document.xml; each w:p element is a
/// paragraph. Every paragraph becomes one line, so an empty paragraph in the
/// document yields a blank line - which is exactly what the prompter's
/// paragraph navigation keys off. No external Word libraries needed.
/// </summary>
public static class DocxReader
{
    private static readonly XNamespace W =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public static string ExtractText(string path)
    {
        using ZipArchive zip = OpenArchive(path);
        ZipArchiveEntry entry = zip.GetEntry("word/document.xml")
            ?? throw new InvalidDataException(
                "This file is not a Word document (word/document.xml is missing).");

        using Stream stream = entry.Open();
        XDocument document = XDocument.Load(stream);

        var text = new StringBuilder();
        foreach (XElement paragraph in document.Descendants(W + "p"))
        {
            // Nested paragraphs occur inside tables; taking only the outermost
            // avoids emitting the same text twice.
            if (paragraph.Ancestors(W + "p").Any())
            {
                continue;
            }

            foreach (XElement node in paragraph.Descendants())
            {
                if (node.Name == W + "t")
                {
                    text.Append(node.Value);
                }
                else if (node.Name == W + "tab")
                {
                    text.Append(' ');
                }
                else if (node.Name == W + "br" || node.Name == W + "cr")
                {
                    text.Append('\n');
                }
            }

            text.Append('\n');
        }

        return text.ToString().Replace("\r", string.Empty).TrimEnd();
    }

    private static ZipArchive OpenArchive(string path)
    {
        try
        {
            return ZipFile.OpenRead(path);
        }
        catch (InvalidDataException)
        {
            throw new InvalidDataException(
                "This file could not be read as a Word document. " +
                "If it is an old .doc file or password protected, save it as .docx or .txt first.");
        }
    }
}
