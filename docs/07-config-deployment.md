# 07 — Konfigurace a nasazení

## Konfigurace (Options pattern)

Sdílené sekce v `appsettings.json` (API) a `appsettings.json`/env (Indexer). **Secrets nikdy do repozitáře** — přes user-secrets (dev) a env proměnné / secret store cloudu (prod).

```jsonc
{
  "AzureOpenAI": {
    "Endpoint": "",                            // https://chatbot-openai.openai.azure.com/ (z env/secret)
    "ApiKey": "",                              // SECRET → env/secret store (nebo managed identity)
    "ChatDeployment": "gpt-4o-mini",           // název deploymentu v Azure OpenAI
    "EmbeddingDeployment": "text-embedding-3-small",
    "EmbeddingDimensions": 1536,
    "ApiVersion": "2024-10-21",                // ověřit aktuální
    "Temperature": 0.2,
    "MaxOutputTokens": 800
  },
  "Database": {
    "ConnectionString": ""              // SECRET → env/secret store
  },
  "Retrieval": {
    "TopK": 5,
    "MinSimilarity": 0.5
  },
  "Indexer": {
    "SitemapUrl": "https://www.enerkomhp.cz/sitemap.xml",
    "CrawlFallbackRootUrl": "https://www.enerkomhp.cz/",
    "MaxCrawlDepth": 3,
    "DocumentsPath": "data/knowledge-base",  // složka s PDF/DOCX/MD (viz konvence níže)
    "Chunk": { "MaxTokens": 500, "OverlapTokens": 80 },
    "EmbeddingBatchSize": 100,
    "RequestDelayMs": 10000,            // robots.txt enerkomhp.cz: Crawl-delay 10s (vlastní web → lze zkrátit)
    "RespectRobotsCrawlDelay": true,
    "ExcludeUrls": [ "https://www.enerkomhp.cz/kopie-z-proc-se-zapojit/" ]  // duplicitní/testovací stránka
  },
  "Cors": {
    "AllowedOrigins": [ "https://www.enerkomhp.cz", "https://enerkomhp.cz" ]
  },
  "Org": {
    "Name": "Enerkom HP",
    "Contact": "info@enerkomhp.cz, +420 …"
  }
}
```

### Secrets

| Secret | Kde |
|---|---|
| `AzureOpenAI:ApiKey` | dev: `dotnet user-secrets`; prod: Container App secret / Key Vault (nebo managed identity — bez klíče) |
| `AzureOpenAI:Endpoint` | env / Container App env (není tajné, ale drž ho v konfiguraci) |
| `Database:ConnectionString` | dtto jako klíč |

Azure OpenAI resource + deploye modelů vytvoří `deploy/setup.ps1` (viz [09-azure-deploy.md](09-azure-deploy.md)). Náklady jsou zastropované **TPM kvótou** deploymentu — viz sekce Cost guardrails v 09.

## Hosting

> **Realita prostředí:** web `www.enerkomhp.cz` běží na **Webnode** (hostovaný web builder — nelze tam nasadit aplikaci ani „nastavit crawler"). Crawler ale běží v našem indexeru na Azure a stránky čte zvenčí přes HTTP → Webnode nevyžaduje žádné nastavení. **Backend (API + indexer) hostujeme na Azure**, kde má zadavatel další aplikace. Web je tedy jen *zdroj dat* a *místo vložení widgetu*.

| Komponenta | Kde běží | Pozn. |
|---|---|---|
| `EnerkomChatbot.Api` | **Azure** — App Service (B1/free) nebo Container App (scale-to-zero) | |
| `widget.js` | statika — `wwwroot` v `EnerkomChatbot.Api` (jeden host, řeší CORS+původ), nebo Azure Blob static | |
| Postgres + pgvector | **stávající** cloud DB | 0 navíc |
| `EnerkomChatbot.Indexer` | **Azure Container Apps Job** / WebJob / Function timer (cron) | běží jen při indexaci |
| Web (zdroj dat) | **Webnode** (cizí) | jen se z něj čte; vkládá se sem `<script>` widgetu |

> Cloud Run / Container Apps se škálováním na nulu jsou pro nízký provoz nejlevnější — platí se jen za běh požadavku.

## Nasazení API

- Build & publish .NET (kontejner přes `dotnet publish /t:PublishContainer` — bez Dockerfile, nebo klasický Dockerfile).
- Env proměnné: `AzureOpenAI__Endpoint`, `AzureOpenAI__ApiKey`, `AzureOpenAI__ChatDeployment`, `AzureOpenAI__EmbeddingDeployment`, `Database__ConnectionString`, `Cors__AllowedOrigins__0`.
- `/health` napojit na health probe hostingu.

## Plán indexace (EnerkomChatbot.Indexer)

Indexer je krátkoběžný proces → spouštět **plánovačem**, ne pořád:

| Cloud | Mechanismus |
|---|---|
| **Azure (cílové prostředí)** | **Container Apps Job (cron)** / WebJob / Azure Function timer |
| Google Cloud | Cloud Run Job + Cloud Scheduler (cron) |
| AWS | ECS Scheduled Task / EventBridge cron |

- **Frekvence:** podle toho, jak často se mění obsah. Web neziskovky obvykle 1×/den (např. v noci) bohatě stačí; dokumenty třeba 1×/týden.
- Indexer je idempotentní a inkrementální (viz 03) → opakované běhy jsou levné (přepočítává jen změny → málo embed volání).
- Po doběhu zapsat výsledek do `indexing_runs` (monitoring).

## Náklady (odhad)

| Položka | Náklad |
|---|---|
| LLM + embeddings (Azure OpenAI `gpt-4o-mini`, desítky dotazů/den) | haléře–jednotky $/měs, zastropováno TPM kvótou |
| Postgres + pgvector | 0 navíc (stávající) |
| API hosting (scale-to-zero) | nízké jednotky $/měs nebo free tier |
| Widget (statika) | ~0 |

→ Neziskové **Azure** kredity (Microsoft for Nonprofits) tyto náklady kryjí s velkou rezervou. Pojistky proti přečerpání kreditů (TPM kvóta, budget alert, typ předplatného) viz [09-azure-deploy.md](09-azure-deploy.md), sekce Cost guardrails.

## GDPR / soukromí

- Indexuje se **veřejný** obsah webu + interní dokumenty (bez osobních údajů — ověřit, že dokumenty neobsahují citlivá data).
- Dotazy uživatelů: pro lazení lze logovat **anonymně a krátkodobě** (bez IP/osobních údajů), nebo nelogovat vůbec. Rozhodnout a zdokumentovat v zásadách ochrany osobních údajů webu.
- Při použití Azure OpenAI zůstávají dotazy v **tvém Azure tenantu** (EU region, např. Sweden Central) a Microsoft je **nepoužívá k tréninku** modelů. To je GDPR-příznivější než Gemini free tier; přesto odeslání dotazů Azure OpenAI uvést v zásadách zpracování.

## Monitoring (lehký)

- `/health` endpoint + health probe hostingu.
- Logy API (počet dotazů, % `answered=false`, počet 429) → odhalí mezery v obsahu a nárazy na limit.
- `indexing_runs` → kontrola, že indexace probíhá a kolik chunků je v DB.
