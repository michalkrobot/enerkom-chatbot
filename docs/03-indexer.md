# 03 — Indexer (EnerkomChatbot.Indexer)

Dávkový proces. Vstup = zdroje (web + dokumenty), výstup = naplněná tabulka `documents`. Spouští se plánovaně (viz 07).

## Spuštění

Generic Host konzolová app. Argumenty/konfigurace určí, co indexovat:

```
EnerkomChatbot.Indexer            # plný běh: web + dokumenty
EnerkomChatbot.Indexer --web      # jen web
EnerkomChatbot.Indexer --docs     # jen dokumenty
EnerkomChatbot.Indexer --dry-run  # nezapisuje do DB, jen vypíše statistiky
```

## Pipeline (IndexingPipeline)

```
1. vytvoř indexing_run (UUID), zapiš řádek do indexing_runs
2. načti zdroje:
     web   → WebCrawler.GetPagesAsync()
     docs  → DocumentLoader.LoadAsync()
3. pro každý zdroj:
     a. spočítej source_hash
     b. nezměněn? → označ chunky run-em, pokračuj
     c. změněn?   → extrahuj text → chunkuj → embedduj (batch) → DELETE staré + INSERT nové
4. mark-and-sweep: smaž chunky s jiným indexing_run
5. uzavři indexing_run (status, počty)
```

Celá pipeline běží sekvenčně přes zdroje; embeddings se dávkují. Žádná paralelizace, která by tloukla na rate limit.

## WebCrawler

> **Stav webu (ověřeno 2026-06-25):** `https://www.enerkomhp.cz/sitemap.xml` existuje a je validní — **17 URL**. `robots.txt` odkazuje na sitemap, nastavuje `Crawl-delay: 10`, blokuje jen `/servers/frontend/`. → Crawler pojede přes sitemap (fallback BFS netřeba). Respektovat crawl-delay (konfig `RequestDelayMs`/`RespectRobotsCrawlDelay`). Vyřadit duplicitní `/kopie-z-proc-se-zapojit/` (konfig `ExcludeUrls`). Stránky `/l/clanek-s-obrazky/`, `/l/novy-spot/` jsou obrázkové → očekávat málo textu.

- Vstup: URL `sitemap.xml` (konfigurovatelně). Z něj seznam `<loc>` URL.
- Odfiltrovat URL z `Indexer:ExcludeUrls` (duplicitní/testovací stránky).
  - Fallback bez sitemap: BFS crawl od kořenové URL, jen stejná doména, max hloubka N (konfigurovatelné), respektovat `robots.txt`.
- Pro každou URL: stáhnout HTML (HttpClient s timeoutem + slušný User-Agent).
- **Čištění HTML (AngleSharp):**
  - odstranit `<script>`, `<style>`, `<nav>`, `<header>`, `<footer>`, `<aside>`, prvky s rolí navigace, cookie lišty.
  - vzít hlavní obsah (`<main>`, `<article>`, nebo největší textový blok).
  - titulek z `<title>` / `<h1>` / `og:title`.
  - výstup: čistý plain text + titulek + finální URL.
- Mezi requesty malá prodleva (např. 200–500 ms), ať web nezahltíme.

Výstup: `IEnumerable<RawSource>` `{ SourceType="web", Uri=url, Title, Text }`.

## DocumentLoader

Zdroj dokumentů = `Indexer:DocumentsPath`. Dev: lokální složka `data/knowledge-base`. **Prod: cloud úložiště** (Azure Blob / GCS / S3) — loader musí umět číst i odtud (abstrahovat za `IDocumentSource`, lokální FS a blob jako dvě implementace; v MVP stačí lokální FS, blob jako rozšíření). Podporované přípony:

| Typ | Knihovna | Pozn. |
|---|---|---|
| `.pdf` | PdfPig | extrakce textu po stránkách; u **skenovaného** PDF (žádný textový layer) → buď přeskočit s warningem, nebo OCR (Tesseract) — OCR je volitelné rozšíření, ne v MVP |
| `.docx` | DocumentFormat.OpenXml | projít odstavce body |
| `.md` | čtení souboru | volitelně odstranit markdown markup |
| `.txt` | čtení souboru | |

Výstup: `RawSource` `{ SourceType, Uri=nazevSouboru, Title=nazevBezPripony, Text }`.

## Chunker (EnerkomChatbot.Core/Rag/Chunker)

Parametry (konfigurovatelné):
- `MaxTokens = 500`
- `OverlapTokens = 80`

Algoritmus:
1. Normalizuj whitespace, sjednoť konce řádků.
2. Rozděl text na odstavce (prázdný řádek / nadpisy).
3. Skládej odstavce do chunku, dokud nepřekročíš `MaxTokens`.
4. Mezi sousedními chunky drž `OverlapTokens` překryv (posledních ~80 tokenů předchozího chunku přidej na začátek dalšího) — drží kontext přes hranice.
5. Příliš dlouhý jediný odstavec rozsekej po větách.

Odhad tokenů: jednoduchá heuristika (≈ znaky/4) stačí; přesný tokenizer není nutný.

Výstup: `List<Chunk>` `{ Index, Content, TokenCount }`.

## Embeddings (EnerkomChatbot.Core/Embeddings — Azure OpenAI)

- Implementuje `IEmbeddingClient` z `EnerkomChatbot.Core/Abstractions` (wrapper nad `IEmbeddingGenerator` z Microsoft.Extensions.AI).
- Model: deployment `text-embedding-3-small` (konfigurovatelně, 1536 dim). Azure OpenAI embeddingy nemají „task type" jako Gemini — pro chunky i dotaz se volá stejně.
- **Dávkování:** posílat chunky po dávkách (např. 100), respektovat TPM kvótu deploymentu.
- **Resilience:** HTTP 429 (TPM) / 5xx → retry s exponenciálním backoffem (Microsoft.Extensions.Http.Resilience). Po vyčerpání pokusů → běh selže s jasným logem (raději spadnout než uložit nekonzistentní index).

> Pozn.: pro **dotaz** (v API) se používá stejný embedding model/deployment jako pro chunky — jinak retrieval nefunguje.

## Logování a výstup

- Strukturované logy: počet zdrojů, nové/změněné/nezměněné, počet chunků, počet embed volání, doba běhu.
- Na konci shrnutí do `indexing_runs`.
- Při chybě jednoho zdroje: zalogovat, **pokračovat** dalšími (jeden rozbitý PDF nesmí shodit celý běh); ale chyba embeddings (rate limit) běh ukončí.

## Idempotence

Opakované spuštění bez změny zdrojů nesmí měnit data (jen `indexed_at`/`indexing_run`). Žádné duplicity — staré chunky se před vložením mažou per zdroj.
