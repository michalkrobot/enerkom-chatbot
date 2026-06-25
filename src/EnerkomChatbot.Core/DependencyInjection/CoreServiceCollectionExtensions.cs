using EnerkomChatbot.Core.Abstractions;
using EnerkomChatbot.Core.Embeddings;
using EnerkomChatbot.Core.Options;
using EnerkomChatbot.Core.Rag;
using EnerkomChatbot.Core.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using Pgvector.Npgsql;

namespace EnerkomChatbot.Core.DependencyInjection;

/// <summary>Registrace sdílených služeb Core (DB, RAG, AOAI klienti). Používá API i Indexer.</summary>
public static class CoreServiceCollectionExtensions
{
    public static IServiceCollection AddEnerkomChatbotCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AzureOpenAIOptions>(configuration.GetSection(AzureOpenAIOptions.SectionName));
        services.Configure<RetrievalOptions>(configuration.GetSection(RetrievalOptions.SectionName));
        services.Configure<OrgOptions>(configuration.GetSection(OrgOptions.SectionName));
        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.SectionName));
        services.Configure<ChunkOptions>(configuration.GetSection(ChunkOptions.SectionName));

        // Sdílený NpgsqlDataSource s mapováním pgvector.
        services.AddSingleton(sp =>
        {
            var connectionString = sp.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString;
            var builder = new NpgsqlDataSourceBuilder(connectionString);
            builder.UseVector();
            return builder.Build();
        });

        services.AddSingleton<IVectorStore, PgVectorStore>();
        services.AddSingleton<IIndexingRunStore, IndexingRunStore>();
        services.AddSingleton<IDatabaseInitializer, DatabaseInitializer>();
        services.AddSingleton<IChunker>(sp => new Chunker(sp.GetRequiredService<IOptions<ChunkOptions>>()));
        services.AddSingleton<PromptBuilder>();
        services.AddSingleton<ChatService>();

        services.AddAzureOpenAIClients();

        return services;
    }
}
