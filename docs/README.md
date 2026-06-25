# Chatbot pro web neziskovky — implementační dokumentace

RAG chatbot, který odpovídá na dotazy návštěvníků webu **výhradně z obsahu webu a interních dokumentů** (PDF/Word/Markdown). Postaveno tak, aby provoz byl **co nejlevnější** (free tier LLM, využití stávající cloud Postgres databáze).

## Kontext a omezení

- **Zadavatel:** nezisková organizace, bez nároku na zisk → priorita = minimální náklady.
- **Provoz:** nízký (desítky dotazů/den) → vejdeme se do free tieru LLM.
- **Infrastruktura:** cloud + **stávající PostgreSQL** v cloudu (využijeme pro vektory přes `pgvector`).
- **Backend:** .NET (ASP.NET Core Minimal API).
- **Frontend:** vložitelný JS/TS widget (`<script>` tag na web).

## Architektura (přehled)

```
INDEXACE (dávkově, plánovaně)
  web (sitemap.xml) + dokumenty (PDF/Word/MD)
    → extrakce textu → chunking → embeddings → Postgres (pgvector)

DOTAZ (online)
  widget → Chat API → embedding dotazu → vektorové hledání (top-k)
    → sestavení promptu (kontext + pravidla) → Azure OpenAI → odpověď + citace → widget
```

## Klíčová technologická rozhodnutí (závazná)

| Vrstva | Volba | Důvod |
|---|---|---|
| LLM | **Azure OpenAI `gpt-4o-mini`** | jednotně na Azure, GDPR čisté (data se netrénují), náklady kryje neziskový kredit |
| Embeddings | **Azure OpenAI `text-embedding-3-small`** (1536 dim) | stejný resource, EU region |
| AI abstrakce | **Microsoft.Extensions.AI** + oficiální Azure provider (`Azure.AI.OpenAI`) | oficiální .NET, snadná výměna providera |
| Vektorová DB | **PostgreSQL + pgvector** | už ji máme, 0 Kč navíc |
| Backend | **ASP.NET Core (.NET 10) Minimal API** | |
| Frontend | **TypeScript widget** (vanilla, bez frameworku) | malý bundle, snadné vložení |

> ⚠️ Konkrétní model ID/verze se mění — ověřit dostupnost v regionu (`az cognitiveservices account list-models`). Deployment name je konfigurovatelný (viz `07-config-deployment.md`). Náklady jsou zastropované TPM kvótou (viz `09-azure-deploy.md`, sekce Cost guardrails).

## Mapa dokumentů

| # | Dokument | Obsah |
|---|---|---|
| 00 | [Architektura](00-architecture.md) | detailní tok dat, komponenty, hranice |
| 01 | [Struktura projektu](01-project-structure.md) | solution, projekty, závislosti |
| 02 | [Databáze](02-database.md) | pgvector, schéma, migrace, indexy |
| 03 | [Indexer](03-indexer.md) | crawl webu, parsing dokumentů, chunking, embeddings |
| 04 | [Chat API](04-chat-api.md) | endpointy, kontrakty, RAG pipeline |
| 05 | [Prompty](05-prompts.md) | systémový prompt (CZ), anti-halucinace, citace |
| 06 | [Frontend widget](06-frontend-widget.md) | chování, integrace, API volání |
| 07 | [Konfigurace a nasazení](07-config-deployment.md) | settings, secrets, hosting, plán indexace |
| 08 | [Implementační plán](08-implementation-plan.md) | rozpad na úkoly pro agenty + pořadí |

## Jak na implementaci

Dokument [`08-implementation-plan.md`](08-implementation-plan.md) obsahuje rozpad na samostatné, na sebe navazující úkoly. Každý úkol odkazuje na příslušný spec dokument výše. Implementace probíhá zdola nahoru: DB → Core → Indexer → Chat API → Widget → nasazení.
