# 08 — Implementační plán (úkoly pro agenty)

Rozpad na samostatné úkoly. Pořadí je zdola nahoru (DB → Core → Indexer → API → Widget → nasazení). Každý úkol = jeden agent, s definovaným vstupem (spec dokument), výstupem a akceptačními kritérii. Úkoly v rámci jedné fáze, které na sobě nezávisí, lze pustit paralelně.

---

## Fáze 0 — Skeleton

**T0.1 — Založit solution a projekty**
- Spec: [01-project-structure.md](01-project-structure.md)
- Výstup: `Couch.slnx`, projekty `Couch.Core`, `Couch.Indexer`, `Couch.Api`, test projekty; `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`. TFM `net10.0`, nullable on.
- Akceptace: `dotnet build` projde; referencemi platí Core ← (Indexer, Api).

---

## Fáze 1 — Databáze a Core abstrakce

**T1.1 — DB schéma + migrační skript**
- Spec: [02-database.md](02-database.md)
- Výstup: `src/Couch.Core/Storage/schema.sql` (validní SQL — pozor na `BIGINT GENERATED ALWAYS AS IDENTITY`), idempotentní; `CREATE EXTENSION vector`; tabulky `documents`, `indexing_runs`; HNSW index.
- Akceptace: skript projde na čisté Postgres instanci s pgvector; index existuje.

**T1.2 — Core modely a abstrakce**
- Spec: [00-architecture.md](00-architecture.md), [01-project-structure.md](01-project-structure.md)
- Výstup: modely (`RawSource`, `Chunk`, `DocumentChunk`, `SearchResult`, `Source`, `ChatMessage`) a rozhraní (`IEmbeddingClient`, `IChatClient`, `IVectorStore`, `IChunker`).
- Akceptace: kompiluje; rozhraní pokrývají potřeby Indexeru i API.

**T1.3 — PgVectorStore (Npgsql + pgvector)**
- Spec: [02-database.md](02-database.md)
- Výstup: `IVectorStore` impl — `UpsertSourceChunksAsync`, `MarkSourceSeenAsync`, `SweepAsync(run)`, `SearchAsync(embedding,k,minSim)`, `GetSourceHashAsync`. `NpgsqlDataSource.UseVector()`.
- Akceptace: integrační test proti reálné/Testcontainers Postgres+pgvector: insert + search vrací nejbližší chunk; sweep maže neviděné.

**T1.4 — Chunker**
- Spec: [03-indexer.md](03-indexer.md) (sekce Chunker)
- Výstup: `IChunker` impl s `MaxTokens`/`OverlapTokens`, dělení po odstavcích, overlap, fallback po větách.
- Akceptace: unit testy — délky chunků ≤ limit, overlap drží, prázdný/krátký vstup ošetřen.

**T1.5 — Azure OpenAI klienti (embeddings + chat)**
- Spec: [01](01-project-structure.md) (pozn. k provideru), [03](03-indexer.md), [05](05-prompts.md)
- Výstup: wrappery implementující `IEmbeddingClient` (batch, deployment `text-embedding-3-small`, 1536 dim) a `IChatClient` (streaming completion, temperature/maxTokens, deployment `gpt-4o-mini`) nad `AzureOpenAIClient` (`Azure.AI.OpenAI`) přes `Microsoft.Extensions.AI.OpenAI`. Resilience handler (retry/backoff na 429/TPM/5xx). Autentizace klíčem nebo `DefaultAzureCredential`.
- Akceptace: jednotkové testy s mockem `IChatClient`/`IEmbeddingGenerator` — správné mapování, parsování odpovědi, retry na 429. Manuální smoke test proti reálnému Azure OpenAI s testovacím deployem.

---

## Fáze 2 — Indexer

**T2.1 — DocumentLoader (PDF/DOCX/MD)**
- Spec: [03-indexer.md](03-indexer.md)
- Výstup: načítání ze složky dle přípony (PdfPig, OpenXml, MD/TXT), `RawSource` výstup, skenované PDF → warning + skip.
- Akceptace: testy s ukázkovými soubory každého typu; chybný soubor neshodí běh.

**T2.2 — WebCrawler (sitemap + fallback BFS, AngleSharp čištění)**
- Spec: [03-indexer.md](03-indexer.md)
- Výstup: parsování sitemap, stahování HTML, čištění (odstranění nav/footer/script), extrakce titulku + hlavního textu, delay mezi requesty, fallback BFS s respektem k robots.txt.
- Akceptace: test na ukázkovém HTML — odstraní navigaci, vrátí čistý text a titulek.

**T2.3 — IndexingPipeline + Program.cs (DI, args, schema bootstrap)**
- Spec: [03-indexer.md](03-indexer.md), [07-config-deployment.md](07-config-deployment.md)
- Výstup: orchestrace load→hash→chunk→embed→upsert + mark-and-sweep; spuštění schema.sql na startu; argumenty `--web/--docs/--dry-run`; zápis do `indexing_runs`; strukturované logy.
- Akceptace: end-to-end běh proti testovací DB naplní `documents`; druhý běh beze změny zdrojů nepřepočítá embeddings (ověřit počet embed volání = 0); odebraný zdroj se smaže.

---

## Fáze 3 — Chat API

**T3.1 — ChatService (RAG pipeline) + PromptBuilder**
- Spec: [04-chat-api.md](04-chat-api.md), [05-prompts.md](05-prompts.md)
- Výstup: validace → embed query → retrieval → fallback při prázdnu → build promptu s očíslovaným kontextem → streaming completion → sestavení `sources`.
- Akceptace: unit testy s mock `IVectorStore`/`IChatClient` — fallback při 0 hitech (negeneruje přes LLM), sources odpovídají hitům, history se ořezává.

**T3.2 — Endpointy + Program.cs (SSE, CORS, rate limit, health)**
- Spec: [04-chat-api.md](04-chat-api.md), [07-config-deployment.md](07-config-deployment.md)
- Výstup: `POST /api/chat` (SSE default + `?stream=false` JSON), `GET /health`; CORS dle `Cors:AllowedOrigins`; rate limiter; mapování 429→ProblemDetails s přívětivou hláškou; DI wiring.
- Akceptace: integrační testy (WebApplicationFactory) — JSON i SSE varianta, validace 400, CORS hlavičky, rate limit vrací 429.

---

## Fáze 4 — Widget

**T4.1 — TypeScript widget**
- Spec: [06-frontend-widget.md](06-frontend-widget.md)
- Výstup: `web/widget` (Vite lib build → `widget.js` IIFE), Shadow DOM, plovoucí tlačítko, streaming přes SSE + JSON fallback, citace jako odkazy, historie, konfigurace přes `data-*`, escapování výstupu.
- **Vizuál:** světlá hlavička, zelenotyrkysová paleta, přátelské vykání; maskot **Elektron** jako avatar (`web/widget/assets/elektron-avatar.png`) s CSS animací (houpání) a stavy idle/přemýšlí/mluví/nenašel.
- Akceptace: manuální test vložením `<script>` na ukázkovou HTML stránku proti běžícímu API; styly hostitele neovlivní widget; chybové stavy mají přívětivou hlášku; avatar Elektrona se zobrazuje a jemně animuje.

---

## Fáze 5 — Nasazení a provoz

**T5.1 — Kontejnerizace + nasazení API**
- Spec: [07-config-deployment.md](07-config-deployment.md)
- Výstup: publish/container API, env proměnné (secrets), health probe, hosting se scale-to-zero, statické servírování `widget.js`.
- Akceptace: nasazená instance odpovídá na `/health`; widget na testovacím webu funguje proti produkční API.

**T5.2 — Plánovaná indexace**
- Spec: [07-config-deployment.md](07-config-deployment.md)
- Výstup: scheduled job (cron) spouštějící Indexer dle cloudu; výchozí web 1×/den, dokumenty dle potřeby.
- Akceptace: job proběhne dle plánu, `indexing_runs` ukazuje úspěch.

---

## Doporučené pořadí a paralelizace

```
T0.1
  └─ T1.1, T1.2            (paralelně po T0.1)
       └─ T1.3, T1.4, T1.5 (paralelně po T1.2; T1.3 po T1.1)
            └─ T2.1, T2.2  (paralelně)
                 └─ T2.3
            └─ T3.1
                 └─ T3.2
                      └─ T4.1
                           └─ T5.1, T5.2
```

## Definice hotového (celý systém)

1. Indexace naplní DB z webu i dokumentů, inkrementálně.
2. Widget na testovacím webu odpoví na dotaz z obsahu, se streamingem a citacemi.
3. Dotaz mimo obsah → „nevím" + kontakt (žádná halucinace).
4. Náklady na provoz ~0 (free tier + stávající DB).
5. Limity ošetřeny (rate limit, 429 hláška), CORS uzamčen na doménu webu.
