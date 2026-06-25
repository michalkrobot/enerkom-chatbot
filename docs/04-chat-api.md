# 04 — Chat API (EnerkomChatbot.Api)

ASP.NET Core Minimal API. Online proces: přijme dotaz, udělá retrieval, zavolá LLM, vrátí odpověď s citacemi.

## Endpointy

### `POST /api/chat`

Hlavní endpoint. Podporuje **streamování** odpovědi (Server-Sent Events) i klasickou JSON odpověď (podle `Accept` hlavičky nebo query `?stream=false`).

**Request:**
```jsonc
{
  "question": "Jak se mohu stát dobrovolníkem?",
  "history": [                       // volitelné, pro navazující dotazy
    { "role": "user", "content": "..." },
    { "role": "assistant", "content": "..." }
  ]
}
```

**Response (JSON, ?stream=false):**
```jsonc
{
  "answer": "Dobrovolníkem se můžete stát vyplněním formuláře…",
  "sources": [
    { "title": "Pro dobrovolníky", "uri": "https://…/dobrovolnici", "type": "web" },
    { "title": "Manuál pro dobrovolníky", "uri": "manual-dobrovolnici.pdf", "type": "pdf" }
  ],
  "answered": true                   // false = informace nebyla v kontextu
}
```

**Response (SSE, default):** proud událostí:
```
event: token        data: {"text":"Dobro"}
event: token        data: {"text":"volníkem"}
...
event: sources      data: [{"title":"…","uri":"…","type":"web"}]
event: done         data: {"answered":true}
```

**Chyby:**
- `400` — prázdný/příliš dlouhý dotaz (limit délky, viz validace).
- `429` — vyčerpán rate limit (náš nebo upstream Azure OpenAI / TPM). Body: ProblemDetails s `detail` = „Služba je dočasně vytížená, zkuste to prosím za chvíli." Widget tuto hlášku zobrazí.
- `503` — DB/LLM nedostupné.

### `GET /health`

Liveness + readiness (ověří připojení k DB). Pro monitoring/hosting.

## RAG pipeline (ChatService v EnerkomChatbot.Core/Rag)

```
1. Validace: question 1..2000 znaků; ořež history na posledních ~6 zpráv.
2. embedding = EmbeddingClient.EmbedQueryAsync(question)   // task type RETRIEVAL_QUERY
3. hits = VectorStore.SearchAsync(embedding, k=5, minSimilarity=0.5)
4. if hits prázdné:
      → vrať answered=false, fallback hláška (viz 05), sources=[]
5. context = PromptBuilder.BuildContext(hits)   // očíslované úryvky + jejich zdroje
6. messages = [ system prompt (05), ...history, user: question + context ]
7. odpověď = ChatClient.CompleteStreamingAsync(messages)
8. sources = distinct zdroje z hits, které model reálně použil
              (MVP: vrátit všechny retrieved zdroje; pokročile: parsovat citace z odpovědi)
9. stream tokenů → klient; na konci pošli sources + done
```

## Retrieval detail

- `k = 5` (konfigurovatelné). Při nízkém provozu klidně i vyšší.
- `minSimilarity` práh — pokud nejlepší hit pod prahem, považuj za „nenalezeno" (`answered=false`).
- Kontext sestavit s **explicitními ID zdrojů**, aby model mohl citovat (viz 05).

## Resilience a limity

- **Rate limiting (vlastní):** ASP.NET Core rate limiter na `/api/chat` (např. fixed window, par requestů/min/IP) — ochrana před zneužitím a před vyčerpáním kreditů / nárazem na TPM kvótu.
- **Upstream 429 z Azure OpenAI (TPM):** retry s backoff (Microsoft.Extensions.Http.Resilience); po vyčerpání → vrátit klientovi `429` s přívětivou hláškou.
- **Timeout:** rozumný timeout na LLM volání; při překročení → `503` + hláška.

## CORS

Povolit jen doménu(y) webu neziskovky (konfigurovatelně, `Cors:AllowedOrigins`). Widget běží na cizí stránce → CORS je nutný. Žádné `*`.

## Bezpečnost

- API klíč Azure OpenAI **jen na serveru** (konfigurace/secrets, viz 07). Nikdy se neposílá klientovi.
- Žádná autentizace koncových uživatelů (veřejný chatbot), ale **rate limit + CORS + délkové limity** jsou povinné.
- Vstup uživatele jde do promptu jako data — systémový prompt musí být odolný vůči pokusům o „prompt injection" (viz 05); nikdy nevykonávat instrukce z retrieved obsahu jako příkazy.

## DI wiring (Program.cs — jen náčrt zodpovědností)

- `NpgsqlDataSource` (singleton, `UseVector()`).
- `IEmbeddingClient` → wrapper nad Azure OpenAI `IEmbeddingGenerator` (deployment `text-embedding-3-small`).
- `IChatClient` → wrapper nad Azure OpenAI `IChatClient` (deployment `gpt-4o-mini`).
- `IVectorStore` → `PgVectorStore`.
- `ChatService`.
- Options: `AzureOpenAIOptions`, `RetrievalOptions`, `CorsOptions`.
- Rate limiter, CORS, health checks, mapování endpointů.
