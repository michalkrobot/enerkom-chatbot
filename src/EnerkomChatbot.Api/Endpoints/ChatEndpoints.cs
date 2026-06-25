using System.Text.Json;
using EnerkomChatbot.Api.Contracts;
using EnerkomChatbot.Core.Exceptions;
using EnerkomChatbot.Core.Models;
using EnerkomChatbot.Core.Rag;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace EnerkomChatbot.Api.Endpoints;

/// <summary>POST /api/chat — SSE (default) i JSON (<c>?stream=false</c>). Viz docs/04-chat-api.md.</summary>
public static class ChatEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapChatEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/chat", HandleChatAsync)
            .RequireRateLimiting("chat")
            .WithName("Chat");

        return app;
    }

    private static async Task HandleChatAsync(
        HttpContext context,
        ChatRequestDto request,
        ChatService chatService,
        ILoggerFactory loggerFactory,
        [FromQuery] bool? stream,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("ChatEndpoint");
        var query = new ChatQuery
        {
            Question = request.Question ?? "",
            History = MapHistory(request.History),
        };

        var useStream = stream != false;

        try
        {
            if (!useStream)
            {
                var answer = await chatService.AnswerAsync(query, cancellationToken);
                await context.Response.WriteAsJsonAsync(new ChatResponseDto
                {
                    Answer = answer.Answer,
                    Sources = [.. answer.Sources.Select(ToDto)],
                    Answered = answer.Answered,
                }, JsonOptions, cancellationToken);
                return;
            }

            // SSE: retrieval (a tedy i případná 429 z embeddings) proběhne před prvním zápisem.
            var prepared = await chatService.PrepareAsync(query, cancellationToken);
            await WriteSseAsync(context, chatService, prepared, logger, cancellationToken);
        }
        catch (InvalidQuestionException ex)
        {
            await WriteProblemAsync(context, StatusCodes.Status400BadRequest, "Neplatný dotaz", ex.Message, cancellationToken);
        }
        catch (RateLimitedException)
        {
            await WriteProblemAsync(context, StatusCodes.Status429TooManyRequests, "Služba vytížená", PromptBuilder.RateLimitMessage, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // klient se odpojil — nic neřešíme
        }
        catch (NpgsqlException ex)
        {
            logger.LogError(ex, "Chyba databáze při zpracování dotazu.");
            await WriteProblemAsync(context, StatusCodes.Status503ServiceUnavailable, "Služba nedostupná", "Služba je dočasně nedostupná, zkuste to prosím za chvíli.", cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Neočekávaná chyba při zpracování dotazu.");
            await WriteProblemAsync(context, StatusCodes.Status503ServiceUnavailable, "Služba nedostupná", "Služba je dočasně nedostupná, zkuste to prosím za chvíli.", cancellationToken);
        }
    }

    private static async Task WriteSseAsync(HttpContext context, ChatService chatService, PreparedChat prepared, ILogger logger, CancellationToken cancellationToken)
    {
        var response = context.Response;
        response.Headers.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers["X-Accel-Buffering"] = "no";

        if (!prepared.Answered)
        {
            await WriteEventAsync(response, "token", new { text = prepared.FallbackAnswer }, cancellationToken);
            await WriteEventAsync(response, "sources", Array.Empty<SourceDto>(), cancellationToken);
            await WriteEventAsync(response, "done", new { answered = false }, cancellationToken);
            return;
        }

        try
        {
            await foreach (var token in chatService.StreamCompletionAsync(prepared.Messages, cancellationToken))
            {
                await WriteEventAsync(response, "token", new { text = token }, cancellationToken);
            }
        }
        catch (RateLimitedException)
        {
            await WriteEventAsync(response, "error", new { detail = PromptBuilder.RateLimitMessage }, cancellationToken);
            await WriteEventAsync(response, "done", new { answered = false }, cancellationToken);
            return;
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Chyba při streamování odpovědi.");
            await WriteEventAsync(response, "error", new { detail = "Omlouvám se, něco se pokazilo. Zkuste to prosím za chvíli." }, cancellationToken);
            await WriteEventAsync(response, "done", new { answered = false }, cancellationToken);
            return;
        }

        await WriteEventAsync(response, "sources", prepared.Sources.Select(ToDto), cancellationToken);
        await WriteEventAsync(response, "done", new { answered = true }, cancellationToken);
    }

    private static async Task WriteEventAsync(HttpResponse response, string eventName, object data, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(data, JsonOptions);
        await response.WriteAsync($"event: {eventName}\ndata: {json}\n\n", cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }

    private static async Task WriteProblemAsync(HttpContext context, int status, string title, string detail, CancellationToken cancellationToken)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
        }, JsonOptions, contentType: "application/problem+json", cancellationToken);
    }

    private static SourceDto ToDto(Source source) => new(source.Title, source.Uri, source.Type);

    private static IReadOnlyList<ChatMessage> MapHistory(List<ChatMessageDto>? history)
    {
        if (history is null || history.Count == 0)
        {
            return [];
        }

        return [.. history
            .Where(m => !string.IsNullOrWhiteSpace(m.Role) && !string.IsNullOrWhiteSpace(m.Content))
            .Select(m => new ChatMessage { Role = NormalizeRole(m.Role!), Content = m.Content! })];
    }

    private static string NormalizeRole(string role) =>
        role.Equals(ChatRoles.Assistant, StringComparison.OrdinalIgnoreCase) ? ChatRoles.Assistant : ChatRoles.User;
}
