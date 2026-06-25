# 02 — Databáze (PostgreSQL + pgvector)

Využíváme **stávající** cloud Postgres. Přidáváme jen rozšíření `pgvector` a jednu hlavní tabulku.

## Předpoklad: pgvector

Ověřit, že instance podporuje `pgvector` (Azure DB for PostgreSQL Flexible Server / Google Cloud SQL / AWS RDS ho mají; u Azure je nutné rozšíření povolit v `azure.extensions` / allowlistu).

```sql
CREATE EXTENSION IF NOT EXISTS vector;
```

Pokud `pgvector` nelze povolit → fallback Qdrant v kontejneru (viz README; znamená to vyměnit implementaci `IVectorStore`, zbytek beze změny).

## Rozměr vektoru

**1536** — odpovídá Azure OpenAI `text-embedding-3-small`. Pokud se zvolí jiný embedding model, MUSÍ se rozměr sloupce `embedding vector(N)` upravit a re-indexovat. Rozměr je tedy svázaný s modelem — nikdy nemíchat vektory z různých modelů v jedné tabulce. (Pozn.: `text-embedding-3-small` umí přes parametr `dimensions` i nižší rozměr, ale držíme nativních 1536.)

## Schéma

```sql
-- Hlavní tabulka chunků
CREATE TABLE IF NOT EXISTS documents (
    id             BIGINT      GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    source_type    TEXT        NOT NULL,          -- 'web' | 'pdf' | 'docx' | 'md'
    source_uri     TEXT        NOT NULL,          -- URL stránky nebo cesta/název souboru
    title          TEXT,                          -- titulek stránky / název dokumentu
    chunk_index    INT         NOT NULL,          -- pořadí chunku v rámci zdroje
    content        TEXT        NOT NULL,          -- text chunku (pro vložení do promptu)
    content_hash   TEXT        NOT NULL,          -- hash obsahu chunku (inkrementální index)
    source_hash    TEXT        NOT NULL,          -- hash celého zdroje (detekce změny)
    token_count    INT,                           -- přibližný počet tokenů
    embedding      vector(1536) NOT NULL,         -- embedding chunku (text-embedding-3-small)
    indexed_at     TIMESTAMPTZ NOT NULL DEFAULT now(),
    indexing_run   UUID        NOT NULL            -- ID běhu indexace (pro mark-and-sweep)
);

-- Rychlé dohledání chunků daného zdroje (upsert / mazání)
-- (pozn.: PostgreSQL 12+ pro GENERATED ALWAYS AS IDENTITY)
CREATE INDEX IF NOT EXISTS ix_documents_source
    ON documents (source_type, source_uri);

-- Vektorový index (HNSW) pro cosine distance
CREATE INDEX IF NOT EXISTS ix_documents_embedding
    ON documents USING hnsw (embedding vector_cosine_ops);

-- Volitelně: evidence běhů indexace
CREATE TABLE IF NOT EXISTS indexing_runs (
    id          UUID        PRIMARY KEY,
    started_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
    finished_at TIMESTAMPTZ,
    status      TEXT        NOT NULL DEFAULT 'running',  -- running|success|failed
    sources     INT         DEFAULT 0,
    chunks      INT         DEFAULT 0,
    note        TEXT
);
```

## Vektorové hledání (retrieval)

Cosine distance přes operátor `<=>`. Menší hodnota = podobnější.

```sql
SELECT id, source_type, source_uri, title, content,
       1 - (embedding <=> @query_embedding) AS similarity
FROM documents
ORDER BY embedding <=> @query_embedding
LIMIT @k;                     -- k = 5 (konfigurovatelné)
```

Volitelný práh: zahodit výsledky se `similarity < @min_similarity` (např. 0.5) — když nic neprojde, API vrátí „odpověď jsem nenašel".

## Inkrementální indexace (strategie)

Na začátku běhu se vygeneruje `indexing_run = <nové UUID>`.

1. Pro každý zdroj spočítej `source_hash`.
2. Existují-li v DB chunky daného `source_uri` se **stejným** `source_hash` → zdroj se nezměnil:
   - jen `UPDATE documents SET indexing_run = @run, indexed_at = now() WHERE source_uri = @uri` (označit jako „viděno v tomto běhu"), embeddings se nepřepočítávají.
3. Jinak (nový/změněný zdroj): `DELETE` staré chunky zdroje a vlož nové (s novým `source_hash`, embeddingy).
4. **Mark-and-sweep na konci:** `DELETE FROM documents WHERE indexing_run <> @run` — smaže chunky zdrojů, které v aktuálním běhu nebyly viděny (zmizely z webu/úložiště).

Tím se šetří volání embeddings API (největší položka rate limitu) — přepočítávají se jen změny.

## Npgsql + pgvector v .NET

- Balíček `Pgvector` registruje mapování `vector` ↔ `Pgvector.Vector`.
- Při tvorbě `NpgsqlDataSource` zavolat `builder.UseVector()`.
- Embedding posílat jako `new Pgvector.Vector(float[])`.

## Migrace

Stačí jednoduchý SQL skript spouštěný při startu indexeru (idempotentní `CREATE ... IF NOT EXISTS`). EF Core není nutné — přístup k DB je přes čistý Npgsql (rychlé, bez ORM overheadu). Skript ulož do `src/EnerkomChatbot.Core/Storage/schema.sql` a spouštěj ho na začátku indexačního běhu.
