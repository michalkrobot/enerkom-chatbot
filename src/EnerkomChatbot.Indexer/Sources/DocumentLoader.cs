using System.Runtime.CompilerServices;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using EnerkomChatbot.Core.Models;
using EnerkomChatbot.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UglyToad.PdfPig;

namespace EnerkomChatbot.Indexer.Sources;

/// <summary>
/// Načítá dokumenty z lokální složky (<see cref="IndexerOptions.DocumentsPath"/>) podle přípony.
/// PDF (PdfPig), DOCX (OpenXml), MD/TXT (text). Skenované PDF (bez textu) → warning + skip.
/// Chyba jednoho souboru neshodí běh.
/// </summary>
public sealed class DocumentLoader(IOptions<IndexerOptions> options, ILogger<DocumentLoader> logger) : ISourceLoader
{
    private static readonly string[] SupportedExtensions = [".pdf", ".docx", ".md", ".txt"];

    public string Name => "docs";

    public async IAsyncEnumerable<RawSource> LoadAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var path = options.Value.DocumentsPath;
        if (!Directory.Exists(path))
        {
            logger.LogWarning("Složka s dokumenty {Path} neexistuje — přeskakuji dokumenty.", path);
            yield break;
        }

        var files = Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories)
            .Where(f => SupportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()));

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            RawSource? source = null;
            try
            {
                source = Load(file);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Nepodařilo se načíst dokument {File} — přeskakuji.", file);
            }

            if (source is not null)
            {
                yield return source;
            }

            await Task.Yield();
        }
    }

    private RawSource? Load(string file)
    {
        var extension = Path.GetExtension(file).ToLowerInvariant();
        var fileName = Path.GetFileName(file);
        var title = Path.GetFileNameWithoutExtension(file);

        var text = extension switch
        {
            ".pdf" => ExtractPdf(file),
            ".docx" => ExtractDocx(file),
            _ => File.ReadAllText(file),
        };

        if (string.IsNullOrWhiteSpace(text))
        {
            logger.LogWarning("Dokument {File} neobsahuje text (možná sken) — přeskakuji.", fileName);
            return null;
        }

        return new RawSource
        {
            SourceType = extension.TrimStart('.'),
            Uri = fileName,
            Title = title,
            Text = text,
        };
    }

    private static string ExtractPdf(string file)
    {
        using var pdf = PdfDocument.Open(file);
        var sb = new StringBuilder();
        foreach (var page in pdf.GetPages())
        {
            var pageText = page.Text;
            if (!string.IsNullOrWhiteSpace(pageText))
            {
                sb.AppendLine(pageText);
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    private static string ExtractDocx(string file)
    {
        using var doc = WordprocessingDocument.Open(file, isEditable: false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body is null)
        {
            return "";
        }

        var sb = new StringBuilder();
        foreach (var paragraph in body.Descendants<Paragraph>())
        {
            var text = paragraph.InnerText;
            if (!string.IsNullOrWhiteSpace(text))
            {
                sb.AppendLine(text);
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }
}
