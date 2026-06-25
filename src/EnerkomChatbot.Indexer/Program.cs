using EnerkomChatbot.Core.DependencyInjection;
using EnerkomChatbot.Core.Options;
using EnerkomChatbot.Indexer;
using EnerkomChatbot.Indexer.Sources;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddEnerkomChatbotCore(builder.Configuration);
builder.Services.Configure<IndexerOptions>(builder.Configuration.GetSection(IndexerOptions.SectionName));

builder.Services.AddHttpClient<WebCrawler>((sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<IndexerOptions>>().Value;
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
});

builder.Services.AddSingleton<DocumentLoader>();
builder.Services.AddSingleton<ISourceLoader>(sp => sp.GetRequiredService<WebCrawler>());
builder.Services.AddSingleton<ISourceLoader>(sp => sp.GetRequiredService<DocumentLoader>());
builder.Services.AddSingleton<IndexingPipeline>();

using var host = builder.Build();

var runOptions = IndexingRunOptions.Parse(args);
var pipeline = host.Services.GetRequiredService<IndexingPipeline>();
var logger = host.Services.GetRequiredService<ILogger<Program>>();

try
{
    await pipeline.RunAsync(runOptions, CancellationToken.None);
    return 0;
}
catch (Exception ex)
{
    logger.LogCritical(ex, "Indexační běh selhal.");
    return 1;
}
