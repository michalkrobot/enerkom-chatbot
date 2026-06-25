-- db-setup.sql — jednorázové nastavení databáze chatbot-db
--
-- Spustit JAKO ADMIN (edcadmin) připojený k databázi "chatbot-db" na serveru
-- edc-postgres-gtsmjb.postgres.database.azure.com.
--
-- Příklad (psql):
--   psql "host=edc-postgres-gtsmjb.postgres.database.azure.com port=5432 dbname=chatbot-db \
--         user=edcadmin sslmode=require" \
--        -v app_password="'ZMEN_NA_HESLO_Z_secrets.env'" -f deploy/db-setup.sql
--
-- POZN.: databázi "chatbot-db" vytvoří deploy/setup.ps1 (az ...) PŘED spuštěním tohoto skriptu.
--        pgvector musí být povolen v azure.extensions (dělá setup.ps1, krok 1).

-- Extension musí vytvořit admin (na Azure Flexible Serveru smí CREATE EXTENSION
-- jen člen azure_pg_admin / server admin). Indexer ji pak už jen použije.
CREATE EXTENSION IF NOT EXISTS vector;

-- Dedikovaná aplikační role s přístupem JEN do chatbot-db (princip nejmenších oprávnění).
-- Pokud role existuje, jen aktualizuj heslo.
DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'chatbot_app') THEN
    EXECUTE format('CREATE ROLE chatbot_app WITH LOGIN PASSWORD %L', :'app_password');
  ELSE
    EXECUTE format('ALTER ROLE chatbot_app WITH LOGIN PASSWORD %L', :'app_password');
  END IF;
END $$;

GRANT CONNECT ON DATABASE "chatbot-db" TO chatbot_app;
GRANT USAGE, CREATE ON SCHEMA public TO chatbot_app;   -- indexer si vytvoří tabulky přes schema.sql

-- Práva na budoucí tabulky/sekvence vytvořené rolí samotnou platí automaticky;
-- toto pokrývá i objekty vytvořené adminem v schema.sql.
ALTER DEFAULT PRIVILEGES IN SCHEMA public
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO chatbot_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA public
  GRANT USAGE, SELECT ON SEQUENCES TO chatbot_app;
