-- EnerkomChatbot — schéma databáze (PostgreSQL + pgvector)
-- Idempotentní: spouští se na začátku každého indexačního běhu.
-- Viz docs/02-database.md. Rozměr vektoru 1536 = Azure OpenAI text-embedding-3-small.

CREATE EXTENSION IF NOT EXISTS vector;

-- Hlavní tabulka chunků
CREATE TABLE IF NOT EXISTS documents (
    id             BIGINT       GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    source_type    TEXT         NOT NULL,          -- 'web' | 'pdf' | 'docx' | 'md' | 'txt'
    source_uri     TEXT         NOT NULL,          -- URL stránky nebo cesta/název souboru
    title          TEXT,                           -- titulek stránky / název dokumentu
    chunk_index    INT          NOT NULL,          -- pořadí chunku v rámci zdroje
    content        TEXT         NOT NULL,          -- text chunku (pro vložení do promptu)
    content_hash   TEXT         NOT NULL,          -- hash obsahu chunku
    source_hash    TEXT         NOT NULL,          -- hash celého zdroje (detekce změny)
    token_count    INT,                            -- přibližný počet tokenů
    embedding      vector(1536) NOT NULL,          -- embedding chunku (text-embedding-3-small)
    indexed_at     TIMESTAMPTZ  NOT NULL DEFAULT now(),
    indexing_run   UUID         NOT NULL            -- ID běhu indexace (mark-and-sweep)
);

-- Rychlé dohledání chunků daného zdroje (upsert / mazání)
CREATE INDEX IF NOT EXISTS ix_documents_source
    ON documents (source_type, source_uri);

-- Mark-and-sweep podle běhu
CREATE INDEX IF NOT EXISTS ix_documents_run
    ON documents (indexing_run);

-- Vektorový index (HNSW) pro cosine distance
CREATE INDEX IF NOT EXISTS ix_documents_embedding
    ON documents USING hnsw (embedding vector_cosine_ops);

-- Evidence běhů indexace
CREATE TABLE IF NOT EXISTS indexing_runs (
    id          UUID        PRIMARY KEY,
    started_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
    finished_at TIMESTAMPTZ,
    status      TEXT        NOT NULL DEFAULT 'running',  -- running | success | failed
    sources     INT         DEFAULT 0,
    chunks      INT         DEFAULT 0,
    note        TEXT
);
