# 00 — Architektura

## Komponenty

Systém má **tři běhové části** + sdílenou knihovnu:

1. **`Couch.Core`** (knihovna) — sdílené modely, rozhraní, RAG služby (chunking, embeddings klient, retrieval). Sdílí Indexer i API.
2. **`Couch.Indexer`** (konzolová / worker app) — dávkový proces: stáhne zdroje, zpracuje, naplní/aktualizuje vektorovou DB. Spouští se plánovaně.
3. **`Couch.Api`** (ASP.NET Core) — online proces: přijímá dotazy z widgetu, dělá retrieval + volá LLM, vrací odpověď s citacemi.
4. **Widget** (`web/widget`, TypeScript) — frontend vložený na web neziskovky.

Společná databáze: **PostgreSQL + pgvector** (stávající cloud instance).

## Tok dat — indexace (offline)

```
┌──────────────┐
│ Zdroje       │  sitemap.xml → seznam URL
│              │  složka/úložiště dokumentů → PDF/DOCX/MD
└──────┬───────┘
       ▼
┌──────────────┐  HTML: odstranit nav/footer/script, vytáhnout hlavní text + titulek
│ Extrakce     │  PDF: extrakce textu (u skenů OCR)
│ textu        │  DOCX: extrakce textu
│              │  MD: rovnou (strip markdown markup volitelně)
└──────┬───────┘
       ▼
┌──────────────┐  rozdělení na chunky ~500 tokenů, overlap ~80 tokenů
│ Chunking     │  hranice po odstavcích/nadpisech
└──────┬───────┘
       ▼
┌──────────────┐  každý chunk → vektor (Azure OpenAI text-embedding-3-small, 1536 dim)
│ Embeddings   │  dávkově (batch), respektovat rate limit
└──────┬───────┘
       ▼
┌──────────────┐  UPSERT do tabulky documents (viz 02-database.md)
│ Uložení      │  metadata: zdroj, URL/cesta, titulek, hash, čas
└──────────────┘
```

**Inkrementálnost:** u každého zdroje se počítá hash obsahu. Když se hash nezměnil, chunk se přeskočí. Zdroje, které z webu/úložiště zmizely, se z DB odstraní (mark-and-sweep podle `indexed_at` běhu).

## Tok dat — dotaz (online)

```
widget                Couch.Api                         Postgres    Azure OpenAI
  │   POST /api/chat      │                                 │              │
  │ ─────────────────────►│                                 │              │
  │  {question, history}  │  1) embedding(question) ───────────────────────►│
  │                       │ ◄───────────────────────────────────── vektor   │
  │                       │  2) SELECT top-k ORDER BY <=>   │              │
  │                       │ ───────────────────────────────►│              │
  │                       │ ◄─────────────── chunky+metadata │              │
  │                       │  3) sestav prompt (kontext+CZ pravidla)         │
  │                       │  4) chat completion ───────────────────────────►│
  │                       │ ◄──────────────────────────── odpověď (stream)  │
  │ ◄─────────────────────│  5) odpověď + citace (SSE stream)               │
  │  text + sources[]     │                                 │              │
```

## Hranice a zodpovědnosti

- **Indexer nezná HTTP** — je to čistě dávkový proces nad `Couch.Core`.
- **API needělá crawl** — jen čte z DB a volá LLM. Indexaci nikdy nespouští synchronně v request handleru.
- **Embeddings klient je v `Couch.Core`** — sdílí ho oba (indexer i API musí používat stejný model a rozměr vektoru, jinak retrieval nefunguje).
- **Widget nezná API klíče** — veškerá komunikace s Azure OpenAI jde přes `Couch.Api`. Klíč nikdy neopouští backend.

## Nefunkční požadavky

- **Náklady ~0:** free tier LLM + stávající DB. Žádná další placená služba.
- **Čeština:** systémový prompt i UI česky; embedding model multijazyčný.
- **Anti-halucinace:** odpovídat jen z kontextu, jinak „nevím" + odkaz na kontakt. Vracet citace.
- **Odolnost vůči limitům:** při HTTP 429 z Azure OpenAI (překročení TPM) → retry s backoff; po vyčerpání → uživateli hláška „zkuste to za chvíli".
- **GDPR:** pracujeme s veřejným obsahem; dotazy uživatelů nelogovat s osobními údaji (viz 07).

## Diagram nasazení

```
[ Web neziskovky ] ── <script> ──► [ widget.js (CDN/static) ]
                                          │ fetch /api/chat
                                          ▼
                              [ Couch.Api (cloud, malá instance) ]
                                    │              │
                              SQL   │              │ HTTPS (API key)
                                    ▼              ▼
                        [ PostgreSQL+pgvector ]  [ Azure OpenAI ]
                                    ▲
                              UPSERT │
                        [ Couch.Indexer ] ◄── plánovač (1×/den)
```
