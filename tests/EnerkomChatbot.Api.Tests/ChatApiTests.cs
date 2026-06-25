using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using EnerkomChatbot.Core.Models;
using Xunit;

namespace EnerkomChatbot.Api.Tests;

public sealed class ChatApiTests(ChatApiFactory factory) : IClassFixture<ChatApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static SearchResult Hit(string uri = "https://www.enerkomhp.cz/dobrovolnici", string type = "web") => new()
    {
        Id = 1,
        SourceType = type,
        SourceUri = uri,
        Title = "Pro dobrovolníky",
        Content = "Dobrovolníkem se stanete vyplněním formuláře.",
        Similarity = 0.82,
    };

    [Fact]
    public async Task Json_WithHits_ReturnsAnswerAndSources()
    {
        factory.GivenHits(Hit());
        factory.GivenCompleteAnswer("Dobrovolníkem se můžete stát vyplněním formuláře. [1]");
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/chat?stream=false", new { question = "Jak se stát dobrovolníkem?" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(dto.GetProperty("answered").GetBoolean());
        Assert.Contains("formuláře", dto.GetProperty("answer").GetString());
        Assert.Equal(1, dto.GetProperty("sources").GetArrayLength());
    }

    [Fact]
    public async Task Json_NoHits_StillAnswersViaLlmWithoutSources()
    {
        factory.GivenHits(); // prázdné
        factory.GivenCompleteAnswer("Dobrý den, rád pomůžu. 🙂");
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/chat?stream=false", new { question = "Ahoj" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(dto.GetProperty("answered").GetBoolean());
        Assert.Equal(0, dto.GetProperty("sources").GetArrayLength());
        Assert.Contains("pomůžu", dto.GetProperty("answer").GetString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Json_EmptyQuestion_Returns400(string question)
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/chat?stream=false", new { question });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Sse_StreamsTokensSourcesAndDone()
    {
        factory.GivenHits(Hit());
        factory.GivenStreamedAnswer("Dobro", "volníkem", " se stanete…");
        var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
        {
            Content = new StringContent("""{"question":"Jak se stát dobrovolníkem?"}""", Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.ParseAdd("text/event-stream");

        var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead);
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.OK, $"status={response.StatusCode} body={body}");
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("event: token", body);
        Assert.Contains("event: sources", body);
        Assert.Contains("event: done", body);
        Assert.Contains("\"answered\":true", body);
    }

    [Fact]
    public async Task Sse_NoHits_StreamsLlmAnswerWithoutSources()
    {
        factory.GivenHits();
        factory.GivenStreamedAnswer("Dobrý ", "den, ", "rád pomůžu.");
        var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
        {
            Content = new StringContent("""{"question":"ahoj"}""", Encoding.UTF8, "application/json"),
        };

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("event: token", body);
        Assert.Contains("\"answered\":true", body);
    }

    [Fact]
    public async Task Cors_Preflight_FromAllowedOrigin_AllowsOrigin()
    {
        var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/chat");
        request.Headers.Add("Origin", "https://www.enerkomhp.cz");
        request.Headers.Add("Access-Control-Request-Method", "POST");

        var response = await client.SendAsync(request);

        Assert.True(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.Contains("https://www.enerkomhp.cz", response.Headers.GetValues("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task RateLimit_ExceedingWindow_Returns429()
    {
        // Vlastní instance — burst requestů by jinak vyčerpal sdílené okno ostatním testům.
        await using var rateLimitFactory = new ChatApiFactory();
        rateLimitFactory.GivenHits(Hit());
        rateLimitFactory.GivenCompleteAnswer("ok");
        var client = rateLimitFactory.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 25; i++)
        {
            var response = await client.PostAsJsonAsync("/api/chat?stream=false", new { question = "dotaz" });
            statuses.Add(response.StatusCode);
        }

        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
    }
}
