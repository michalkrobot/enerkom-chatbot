# 01 — Struktura projektu

## Solution layout

```
enerkom-chatbot/
├─ EnerkomChatbot.slnx                      # solution (nový .slnx formát)
├─ Directory.Build.props           # společné build vlastnosti (TFM, nullable, implicit usings)
├─ Directory.Packages.props        # centrální správa verzí NuGet balíčků
├─ .editorconfig
├─ docs/                           # tato dokumentace
├─ src/
│  ├─ EnerkomChatbot.Core/                  # sdílená knihovna (žádné HTTP)
│  │  ├─ Models/                   #   DocumentChunk, SearchResult, Source, ChatMessage…
│  │  ├─ Abstractions/             #   IEmbeddingClient, IChatClient, IVectorStore, IChunker…
│  │  ├─ Rag/                      #   RetrievalService, PromptBuilder, Chunker
│  │  ├─ Embeddings/               #   AzureOpenAI embed/chat klient (přes Microsoft.Extensions.AI)
│  │  └─ Storage/                  #   PgVectorStore (Npgsql + pgvector)
│  ├─ EnerkomChatbot.Indexer/               # dávkový worker (konzolová app)
│  │  ├─ Sources/                  #   WebCrawler (sitemap), DocumentLoader (PDF/DOCX/MD)
│  │  ├─ IndexingPipeline.cs       #   orchestrace: load → chunk → embed → upsert
│  │  └─ Program.cs
│  └─ EnerkomChatbot.Api/                   # ASP.NET Core Minimal API
│     ├─ Endpoints/                #   ChatEndpoints (POST /api/chat), HealthEndpoints
│     ├─ Program.cs
│     └─ appsettings.json
├─ web/
│  └─ widget/                      # TypeScript widget
│     ├─ src/
│     ├─ package.json
│     ├─ tsconfig.json
│     └─ vite.config.ts            # build do jednoho IIFE bundlu
└─ tests/
   ├─ EnerkomChatbot.Core.Tests/
   └─ EnerkomChatbot.Api.Tests/             # WebApplicationFactory integrační testy
```

## Projekty a jejich závislosti

```
EnerkomChatbot.Core   ──(referencuje)──►  nic z našich (jen NuGet)
EnerkomChatbot.Indexer ──►  EnerkomChatbot.Core
EnerkomChatbot.Api     ──►  EnerkomChatbot.Core
```

Pravidlo: **`EnerkomChatbot.Core` nesmí referencovat `Api` ani `Indexer`.** Vše sdílené patří do Core.

## Cílový framework a jazyk

- **TFM:** `net10.0`
- **C#:** `latest` (primary constructors, collection expressions, `required` členy)
- **Nullable:** `enable`
- **ImplicitUsings:** `enable`

## NuGet balíčky (centrálně v `Directory.Packages.props`)

| Balíček | Projekt | Účel |
|---|---|---|
| `Microsoft.Extensions.AI` | Core | abstrakce `IChatClient`, `IEmbeddingGenerator` |
| `Microsoft.Extensions.AI.Abstractions` | Core | rozhraní |
| `Azure.AI.OpenAI` | Core | klient pro Azure OpenAI (chat + embeddings) |
| `Microsoft.Extensions.AI.OpenAI` | Core | adaptér Azure OpenAI → `IChatClient`/`IEmbeddingGenerator` |
| `Azure.Identity` | Core | (volitelně) managed identity místo API klíče |
| `Npgsql` | Core | přístup k Postgres |
| `Pgvector` | Core | mapování `vector` typu pro Npgsql |
| `Microsoft.Extensions.Http.Resilience` | Core | retry/backoff na 429 (TPM) z Azure OpenAI |
| `AngleSharp` | Indexer | parsing HTML (čištění nav/footer) |
| `PdfPig` (UglyToad.PdfPig) | Indexer | extrakce textu z PDF |
| `DocumentFormat.OpenXml` | Indexer | extrakce textu z DOCX |
| `Microsoft.Extensions.Hosting` | Indexer | generic host, DI, konfigurace |
| `xunit.v3`, `Microsoft.AspNetCore.Mvc.Testing` | tests | testy |

> **Pozn. k Azure OpenAI provideru:** Azure OpenAI má **oficiální** podporu v .NET. Použij `AzureOpenAIClient` (balíček `Azure.AI.OpenAI`) a získej `IChatClient` / `IEmbeddingGenerator` přes `Microsoft.Extensions.AI.OpenAI` (`.AsChatClient(deployment)`, `.AsEmbeddingGenerator(deployment)`). **Žádný vlastní HTTP klient není potřeba** — náš `IEmbeddingClient` / `IChatClient` z `EnerkomChatbot.Core/Abstractions` jsou tenké wrappery nad těmito M.E.AI rozhraními (zachovají možnost pozdější výměny providera).
>
> Autentizace: API klíč (jednodušší) nebo `DefaultAzureCredential` (managed identity, bez klíče v secretech — doporučeno pro produkci). Resilience (retry/backoff na 429/TPM) přes `Microsoft.Extensions.Http.Resilience`.

## Konvence

- Jeden veřejný typ na soubor, název souboru = název typu.
- Async metody mají sufix `Async` a přijímají `CancellationToken`.
- Žádná business logika v `Program.cs` — jen wiring (DI, endpointy).
- Konfigurace přes Options pattern (`IOptions<AzureOpenAIOptions>`, `IOptions<IndexerOptions>`).
