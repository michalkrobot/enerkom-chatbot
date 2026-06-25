using EnerkomChatbot.Core.Models;
using EnerkomChatbot.Core.Options;
using EnerkomChatbot.Core.Rag;
using Xunit;

namespace EnerkomChatbot.Core.Tests;

public sealed class ChunkerTests
{
    private static Chunker CreateChunker(int maxTokens = 500, int overlap = 80) =>
        new(new ChunkOptions { MaxTokens = maxTokens, OverlapTokens = overlap });

    // A long multi-paragraph Czech text used across several tests.
    private const string LongCzechText = """
        Energetika a obnovitelné zdroje jsou klíčovým tématem pro neziskovou organizaci Enerkom HP.
        Naším cílem je pomáhat domácnostem snižovat spotřebu energie a zlepšovat tepelnou izolaci budov.
        Poskytujeme bezplatné poradenství v oblasti fotovoltaiky, tepelných čerpadel a zateplení.

        Mnoho lidí netuší, kolik energie zbytečně uniká okny a špatně izolovanou střechou.
        Správná izolace dokáže ušetřit až čtyřicet procent nákladů na vytápění během zimních měsíců.
        Doporučujeme proto začít energetickým auditem, který odhalí největší slabiny domu.

        Fotovoltaické panely se v posledních letech staly dostupnější než kdykoli předtím.
        Díky státním dotacím a klesajícím cenám technologie se investice často vrátí během několika let.
        Naši poradci vám pomohou spočítat návratnost a vybrat vhodné řešení pro vaši domácnost.

        Tepelná čerpadla představují moderní a úsporný způsob vytápění i ohřevu vody.
        Fungují efektivně i v chladnějším podnebí a lze je kombinovat s fotovoltaikou.
        Pokud máte zájem o nezávaznou konzultaci, neváhejte se na nás obrátit prostřednictvím kontaktního formuláře.

        Kromě technického poradenství pořádáme také vzdělávací semináře pro veřejnost.
        Na těchto akcích se dozvíte praktické tipy, jak hospodařit s energií v běžném životě.
        Sledujte náš web a sociální sítě, kde pravidelně zveřejňujeme termíny nadcházejících událostí.
        """;

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\n\t  \n")]
    public void Chunk_EmptyOrWhitespaceInput_ReturnsEmptyList(string input)
    {
        // Arrange
        var chunker = CreateChunker();

        // Act
        var chunks = chunker.Chunk(input);

        // Assert
        Assert.Empty(chunks);
    }

    [Fact]
    public void Chunk_ShortSingleParagraph_ReturnsExactlyOneChunkAtIndexZero()
    {
        // Arrange
        var chunker = CreateChunker();
        const string text = "Krátký odstavec o energii.";

        // Act
        var chunks = chunker.Chunk(text);

        // Assert
        var chunk = Assert.Single(chunks);
        Assert.Equal(0, chunk.Index);
        Assert.Contains("Krátký odstavec", chunk.Content);
    }

    [Fact]
    public void Chunk_LongMultiParagraphText_EveryChunkTokenCountWithinMaxTokens()
    {
        // Arrange
        const int maxTokens = 500;
        var chunker = CreateChunker(maxTokens, overlap: 80);

        // Act
        var chunks = chunker.Chunk(LongCzechText);

        // Assert
        Assert.NotEmpty(chunks);
        Assert.All(chunks, c => Assert.True(
            c.TokenCount <= maxTokens,
            $"Chunk {c.Index} has TokenCount {c.TokenCount} > {maxTokens}."));
    }

    [Fact]
    public void Chunk_WithOverlap_ConsecutiveChunksShareOverlappingText()
    {
        // Arrange — small budget so the text splits into several chunks with real overlap.
        var chunker = CreateChunker(maxTokens: 120, overlap: 40);

        // Act
        var chunks = chunker.Chunk(LongCzechText);

        // Assert
        Assert.True(chunks.Count >= 2, "Expected multiple chunks for this input.");
        for (var i = 1; i < chunks.Count; i++)
        {
            var previous = chunks[i - 1].Content;
            var current = chunks[i].Content;

            // The overlap prefix of the current chunk is the leading run before the body separator.
            var separatorIndex = current.IndexOf("\n\n", StringComparison.Ordinal);
            Assert.True(separatorIndex > 0, $"Chunk {i} has no overlap prefix.");

            var overlapPrefix = current[..separatorIndex].Trim();
            Assert.NotEmpty(overlapPrefix);

            // The overlap prefix must come from the tail of the previous chunk.
            Assert.Contains(overlapPrefix, previous, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Chunk_SingleParagraphLongerThanMaxTokens_SplitsIntoMultipleChunksEachWithinLimit()
    {
        // Arrange — a single paragraph (no blank lines) made of many sentences, far over the budget.
        const int maxTokens = 60;
        var chunker = CreateChunker(maxTokens, overlap: 0);

        var sentences = Enumerable
            .Range(1, 40)
            .Select(i => $"Toto je věta číslo {i} o úspoře energie a zateplení domu.");
        var longParagraph = string.Join(" ", sentences);

        // Act
        var chunks = chunker.Chunk(longParagraph);

        // Assert
        Assert.True(chunks.Count > 1, "A paragraph over the budget should split into multiple chunks.");
        Assert.All(chunks, c => Assert.True(
            c.TokenCount <= maxTokens,
            $"Chunk {c.Index} has TokenCount {c.TokenCount} > {maxTokens}."));
    }

    [Fact]
    public void Chunk_MultipleChunks_IndexesAreSequentialStartingAtZero()
    {
        // Arrange
        var chunker = CreateChunker(maxTokens: 100, overlap: 20);

        // Act
        var chunks = chunker.Chunk(LongCzechText);

        // Assert
        Assert.True(chunks.Count >= 2, "Expected multiple chunks for this input.");
        for (var i = 0; i < chunks.Count; i++)
        {
            Assert.Equal(i, chunks[i].Index);
        }
    }

    [Fact]
    public void EstimateTokens_EmptyString_ReturnsZero()
    {
        // Arrange & Act
        var tokens = Chunker.EstimateTokens("");

        // Assert
        Assert.Equal(0, tokens);
    }

    [Fact]
    public void EstimateTokens_NonEmptyString_ApproximatesCharsOverFour()
    {
        // Arrange
        var text = new string('a', 16);

        // Act
        var tokens = Chunker.EstimateTokens(text);

        // Assert
        Assert.Equal(4, tokens);
    }
}
