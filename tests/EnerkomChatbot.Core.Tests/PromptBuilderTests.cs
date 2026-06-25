using EnerkomChatbot.Core.Models;
using EnerkomChatbot.Core.Options;
using EnerkomChatbot.Core.Rag;
using Microsoft.Extensions.Options;
using Xunit;

namespace EnerkomChatbot.Core.Tests;

public sealed class PromptBuilderTests
{
    private static PromptBuilder CreateBuilder(string name = "Enerkom HP", string contact = "info@enerkomhp.cz") =>
        new(Microsoft.Extensions.Options.Options.Create(new OrgOptions { Name = name, Contact = contact }));

    private static SearchResult Hit(
        string type = "web",
        string uri = "https://enerkomhp.cz/page",
        string? title = "Titulek",
        string content = "Obsah úryvku.",
        long id = 1,
        double similarity = 0.9) =>
        new()
        {
            Id = id,
            SourceType = type,
            SourceUri = uri,
            Title = title,
            Content = content,
            Similarity = similarity,
        };

    [Fact]
    public void BuildSources_OneSourcePerHit_PreservesOrderAndMapsFields()
    {
        // Arrange
        IReadOnlyList<SearchResult> hits =
        [
            Hit(type: "web", uri: "https://a.cz", title: "Áčko", id: 1),
            Hit(type: "pdf", uri: "doc.pdf", title: "Dokument", id: 2),
        ];

        // Act
        var sources = PromptBuilder.BuildSources(hits);

        // Assert
        Assert.Equal(2, sources.Count);

        Assert.Equal("web", sources[0].Type);
        Assert.Equal("https://a.cz", sources[0].Uri);
        Assert.Equal("Áčko", sources[0].Title);

        Assert.Equal("pdf", sources[1].Type);
        Assert.Equal("doc.pdf", sources[1].Uri);
        Assert.Equal("Dokument", sources[1].Title);
    }

    [Fact]
    public void BuildSources_NullOrBlankTitle_FallsBackToUri()
    {
        // Arrange
        IReadOnlyList<SearchResult> hits =
        [
            Hit(uri: "https://a.cz", title: null),
            Hit(uri: "https://b.cz", title: "   "),
        ];

        // Act
        var sources = PromptBuilder.BuildSources(hits);

        // Assert
        Assert.Equal("https://a.cz", sources[0].Title);
        Assert.Equal("https://b.cz", sources[1].Title);
    }

    [Fact]
    public void BuildContext_NumbersHeadersSequentially()
    {
        // Arrange
        IReadOnlyList<SearchResult> hits =
        [
            Hit(uri: "https://a.cz"),
            Hit(uri: "https://b.cz"),
        ];

        // Act
        var context = PromptBuilder.BuildContext(hits);

        // Assert
        Assert.Contains("[1]", context, StringComparison.Ordinal);
        Assert.Contains("[2]", context, StringComparison.Ordinal);
        Assert.DoesNotContain("[3]", context, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildContext_WebHit_IncludesUri()
    {
        // Arrange
        IReadOnlyList<SearchResult> hits = [Hit(type: "web", uri: "https://enerkomhp.cz/faq")];

        // Act
        var context = PromptBuilder.BuildContext(hits);

        // Assert
        Assert.Contains("https://enerkomhp.cz/faq", context, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildContext_NonWebHit_DoesNotIncludeUriInHeader()
    {
        // Arrange
        IReadOnlyList<SearchResult> hits = [Hit(type: "pdf", uri: "tajny-soubor.pdf", title: "Dokument")];

        // Act
        var context = PromptBuilder.BuildContext(hits);

        // Assert — only the title appears in the header, the uri is not embedded.
        Assert.Contains("[1] (pdf — Dokument)", context, StringComparison.Ordinal);
        Assert.DoesNotContain("tajny-soubor.pdf", context, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildSystemPrompt_SubstitutesOrgNameContactAndContext()
    {
        // Arrange
        var builder = CreateBuilder(name: "Moje Nezisková", contact: "kontakt@example.org");
        IReadOnlyList<SearchResult> hits = [Hit(content: "Důležitý fakt o energii.")];

        // Act
        var prompt = builder.BuildSystemPrompt(hits);

        // Assert
        Assert.Contains("Moje Nezisková", prompt, StringComparison.Ordinal);
        Assert.Contains("kontakt@example.org", prompt, StringComparison.Ordinal);
        Assert.Contains("[1]", prompt, StringComparison.Ordinal);
        Assert.Contains("Důležitý fakt o energii.", prompt, StringComparison.Ordinal);
        // No unfilled placeholders remain.
        Assert.DoesNotContain("{ORG_NAME}", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("{CONTACT}", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("{CONTEXT}", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void FallbackAnswer_ContainsConfiguredContact()
    {
        // Arrange
        var builder = CreateBuilder(contact: "pomoc@example.org");

        // Act
        var fallback = builder.FallbackAnswer();

        // Assert
        Assert.Contains("pomoc@example.org", fallback, StringComparison.Ordinal);
    }
}
