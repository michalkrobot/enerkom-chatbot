using System.Text;
using System.Text.RegularExpressions;
using EnerkomChatbot.Core.Abstractions;
using EnerkomChatbot.Core.Models;
using EnerkomChatbot.Core.Options;
using Microsoft.Extensions.Options;

namespace EnerkomChatbot.Core.Rag;

/// <summary>
/// Dělí text na chunky po odstavcích s překryvem. Odhad tokenů heuristikou (≈ znaky/4).
/// Příliš dlouhý odstavec se dělí po větách, příliš dlouhá věta tvrdě po znacích.
/// Viz docs/03-indexer.md (sekce Chunker).
/// </summary>
public sealed partial class Chunker : IChunker
{
    private readonly int _maxTokens;
    private readonly int _overlapTokens;
    private readonly int _bodyBudget;

    public Chunker(ChunkOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _maxTokens = Math.Max(1, options.MaxTokens);
        _overlapTokens = Math.Clamp(options.OverlapTokens, 0, _maxTokens - 1);
        _bodyBudget = Math.Max(1, _maxTokens - _overlapTokens);
    }

    public Chunker(IOptions<ChunkOptions> options) : this(options.Value) { }

    /// <summary>Odhad počtu tokenů (≈ znaky / 4).</summary>
    public static int EstimateTokens(string text) =>
        string.IsNullOrEmpty(text) ? 0 : (int)Math.Ceiling(text.Length / 4.0);

    public IReadOnlyList<Chunk> Chunk(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var normalized = Normalize(text);
        var units = SplitIntoUnits(normalized);
        if (units.Count == 0)
        {
            return [];
        }

        var chunks = new List<Chunk>();
        var body = new StringBuilder();
        var index = 0;
        string? previousContent = null;

        foreach (var unit in units)
        {
            var candidate = body.Length == 0 ? unit : body + "\n\n" + unit;
            if (EstimateTokens(candidate) > _bodyBudget && body.Length > 0)
            {
                previousContent = Emit(chunks, ref index, body.ToString(), previousContent);
                body.Clear();
            }

            if (body.Length > 0)
            {
                body.Append("\n\n");
            }

            body.Append(unit);
        }

        if (body.Length > 0)
        {
            Emit(chunks, ref index, body.ToString(), previousContent);
        }

        return chunks;
    }

    /// <summary>Sestaví obsah chunku (overlap z předchozího + tělo), přidá do seznamu, vrátí jeho obsah pro další overlap.</summary>
    private string Emit(List<Chunk> chunks, ref int index, string bodyText, string? previousContent)
    {
        var overlap = _overlapTokens > 0 && previousContent is not null
            ? TakeOverlap(previousContent)
            : "";

        var content = overlap.Length > 0 ? overlap + "\n\n" + bodyText : bodyText;
        chunks.Add(new Chunk
        {
            Index = index++,
            Content = content,
            TokenCount = EstimateTokens(content),
        });
        return content;
    }

    /// <summary>Posledních ~OverlapTokens textu předchozího chunku, zarovnáno na hranici slova/věty.</summary>
    private string TakeOverlap(string previous)
    {
        var chars = _overlapTokens * 4;
        if (previous.Length <= chars)
        {
            return previous.Trim();
        }

        var start = previous.Length - chars;
        // Posun na nejbližší konec věty před cut, jinak na mezeru — ať overlap nezačíná uprostřed slova.
        var sentenceBreak = previous.LastIndexOfAny(['.', '!', '?', '\n'], start);
        if (sentenceBreak >= 0 && sentenceBreak + 1 < previous.Length)
        {
            start = sentenceBreak + 1;
        }
        else
        {
            var space = previous.IndexOf(' ', start);
            if (space >= 0)
            {
                start = space + 1;
            }
        }

        return previous[start..].Trim();
    }

    private List<string> SplitIntoUnits(string text)
    {
        var units = new List<string>();
        foreach (var paragraph in ParagraphSplit().Split(text))
        {
            var trimmed = paragraph.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (EstimateTokens(trimmed) <= _bodyBudget)
            {
                units.Add(trimmed);
            }
            else
            {
                units.AddRange(SplitLongParagraph(trimmed));
            }
        }

        return units;
    }

    private IEnumerable<string> SplitLongParagraph(string paragraph)
    {
        var buffer = new StringBuilder();
        foreach (var sentence in SentenceSplit().Split(paragraph))
        {
            var s = sentence.Trim();
            if (s.Length == 0)
            {
                continue;
            }

            if (EstimateTokens(s) > _bodyBudget)
            {
                // Příliš dlouhá i jediná věta → tvrdé dělení po znacích.
                if (buffer.Length > 0)
                {
                    yield return buffer.ToString();
                    buffer.Clear();
                }

                foreach (var window in HardSplit(s))
                {
                    yield return window;
                }

                continue;
            }

            var candidate = buffer.Length == 0 ? s : buffer + " " + s;
            if (EstimateTokens(candidate) > _bodyBudget && buffer.Length > 0)
            {
                yield return buffer.ToString();
                buffer.Clear();
                buffer.Append(s);
            }
            else
            {
                if (buffer.Length > 0)
                {
                    buffer.Append(' ');
                }

                buffer.Append(s);
            }
        }

        if (buffer.Length > 0)
        {
            yield return buffer.ToString();
        }
    }

    private IEnumerable<string> HardSplit(string text)
    {
        var window = _bodyBudget * 4;
        for (var i = 0; i < text.Length; i += window)
        {
            yield return text.Substring(i, Math.Min(window, text.Length - i));
        }
    }

    private static string Normalize(string text)
    {
        var unified = text.Replace("\r\n", "\n").Replace('\r', '\n');
        unified = TrailingSpaces().Replace(unified, "");
        unified = MultiNewline().Replace(unified, "\n\n");
        return unified.Trim();
    }

    [GeneratedRegex(@"\n\s*\n")]
    private static partial Regex ParagraphSplit();

    [GeneratedRegex(@"(?<=[.!?…])\s+")]
    private static partial Regex SentenceSplit();

    [GeneratedRegex(@"[ \t]+\n")]
    private static partial Regex TrailingSpaces();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex MultiNewline();
}
