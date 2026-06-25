using System.Threading.RateLimiting;
using EnerkomChatbot.Api.Endpoints;
using EnerkomChatbot.Api.HealthChecks;
using EnerkomChatbot.Api.Options;
using EnerkomChatbot.Core.DependencyInjection;
using EnerkomChatbot.Core.Rag;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEnerkomChatbotCore(builder.Configuration);
builder.Services.Configure<CorsOptions>(builder.Configuration.GetSection(CorsOptions.SectionName));

const string WidgetCorsPolicy = "widget";
var allowedOrigins = builder.Configuration.GetSection($"{CorsOptions.SectionName}:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
    options.AddPolicy(WidgetCorsPolicy, policy =>
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("chat", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));

    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.ContentType = "application/problem+json";
        await context.HttpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = "Příliš mnoho požadavků",
            Detail = PromptBuilder.RateLimitMessage,
        }, cancellationToken);
    };
});

builder.Services.AddHealthChecks().AddCheck<DbHealthCheck>("database");

var app = builder.Build();

app.UseCors(WidgetCorsPolicy);
app.UseRateLimiter();
app.UseStaticFiles(); // servíruje widget.js z wwwroot (stejný původ jako API → řeší CORS pro statiku)

app.MapChatEndpoints();
app.MapHealthChecks("/health");

app.Run();

/// <summary>Pro WebApplicationFactory v integračních testech.</summary>
public partial class Program;
