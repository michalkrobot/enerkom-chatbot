# 09 — Nasazení do Azure (plán pro agenta)

Plán pro nasazení chatbota **EnerkomChatbot** do **stávajícího** Azure předplatného Enerkom HP, vedle aplikace EDC, s minimálními náklady navíc. Plán je psaný tak, aby ho mohl odkrokovat agent. Navazuje na [07-config-deployment.md](07-config-deployment.md) a tvoří konkrétní obsah **Fáze 5** z [08-implementation-plan.md](08-implementation-plan.md).

---

## 0. Skutečný stav infrastruktury (ověřeno v Azure 2026-06-25)

> ⚠️ **Korekce předpokladu ze zadání:** žádná virtuálka (VM) neexistuje. EDC běží **serverless** jako Azure Container App. „Stejná virtuálka, ať se neplatí navíc" se proto realizuje jako **sdílené Container Apps Environment** + **sdílený Postgres server s novou databází**. Při scale-to-zero je marginální náklad chatbota ~0.

| Položka | Hodnota | Pozn. |
|---|---|---|
| Subscription | `Azure subscription 1` (`594e58df-c16e-47e0-9dd9-c404efd67701`) | účet `krobot@enerkom-hp.cz` |
| Resource group | `rg-edc` | sem patří i chatbot |
| Region zdrojů | **North Europe** | Container App MUSÍ být ve stejném regionu jako jeho env |
| Container Apps Environment | `edc-env` (North Europe) | **sdílíme** — sem přidáme chatbota |
| Běžící app EDC | `edc-api` → `edc-data.enerkom-hp.cz` | nesahat |
| **Aktivní Postgres** | **`edc-postgres-gtsmjb`** (PG 17, Standard_B1ms, Burstable) | sem přidáme `chatbot-db` |
| Postgres admin | `edcadmin` | heslo je v secretu `connection-string` u `edc-api` |
| Druhý Postgres | `edc-postgres-nbikqr` | ⚠️ zřejmě osiřelý duplikát — **NEPOUŽÍVAT**; zvážit smazání (mimo rozsah) |
| pgvector | **NENÍ povolen** | `azure.extensions` = `timescaledb` → nutno přidat `vector` |
| DNS | Cloudflare (`enerkom-hp.cz`) | EDC používá orange cloud + SSL Full |

**Rozhodnutí pro tento deploy:**
- Deploy přes **GitHub Actions + ghcr.io** (zrcadlí EDC `deploy-azure.yml`).
- Přístup k DB přes **dedikovanou roli** s právy jen na `chatbot-db` (ne sdílení `edcadmin`).

---

## 1. Cílová architektura

```
Cloudflare DNS                     Azure rg-edc / edc-env (North Europe)
chatbot.enerkom-hp.cz ─CNAME─► chatbot-api (Container App, scale-to-zero, managed cert)
                                     │  servíruje i widget.js (wwwroot)
                                     │
   www.enerkomhp.cz (Webnode) ◄──────┤ widget <script>
                                     │
                          ┌──────────┴───────────┐
                          ▼                      ▼
            edc-postgres-gtsmjb            Azure OpenAI (chatbot-openai)
              DB: chatbot-db                 gpt-4o-mini + text-embedding-3-small
              role: chatbot_app              (klíč jen na backendu, TPM strop)
              EXTENSION vector
                          ▲
                          │ UPSERT (1×/den)
              chatbot-indexer (Container Apps Job, cron, stejné env)
```

Sdílí se: subscription, resource group, Container Apps Environment, Postgres **server**. Odděleně: databáze, DB role, container app, doména, secrets, **Azure OpenAI resource** (vlastní, kvůli TPM stropu a izolaci).

---

## 2. Předpoklady (musí platit PŘED deployem)

1. **Aplikace je implementovaná** — fáze 0–4 z [08-implementation-plan.md](08-implementation-plan.md). Aktuálně v repu existují jen docs + `web/widget` + `data/knowledge-base`; `src/` a `.slnx` zatím **nejsou**. Bez běžícího `EnerkomChatbot.Api` není co nasazovat.
2. **`enerkom-chatbot` je git repozitář s remote na GitHubu** (dnes není git repo). Potřeba pro Actions + ghcr.io.
3. **`Dockerfile`** pro `EnerkomChatbot.Api`, který servíruje i `widget.js` ze `wwwroot` (vzor: EDC `Dockerfile`, multistage SDK 10 → aspnet 10; pro chatbota BEZ Playwright závislostí — ty EDC potřeboval na scraping, chatbot ne).
4. **Lokálně přihlášené `az` CLI** do správného předplatného (`az account show` → `594e58df-…`).
5. **Azure OpenAI** — resource + deploye modelů vytvoří `setup.ps1` (krok 2b). Ověřit jen dostupnost modelů v regionu (`az cognitiveservices account list-models`).

> Jeden krátký Dockerfile pro indexer (`EnerkomChatbot.Indexer`) je také potřeba (krok 7) — může to být stejný image se dvěma entrypointy nebo dva samostatné images.

---

## 3. Krok za krokem

Proměnné použité níže (PowerShell):

```powershell
$Rg            = "rg-edc"
$Location      = "northeurope"
$Env           = "edc-env"
$PgServer      = "edc-postgres-gtsmjb"
$PgAdmin       = "edcadmin"
$PgDb          = "chatbot-db"
$PgAppRole     = "chatbot_app"
$AppName       = "chatbot-api"
$JobName       = "chatbot-indexer"
$Domain        = "chatbot.enerkom-hp.cz"
$SubId         = "594e58df-c16e-47e0-9dd9-c404efd67701"
# Azure OpenAI
$OpenAIName       = "chatbot-openai"
$OpenAILocation   = "swedencentral"      # EU; ověř dostupnost modelů v regionu
$ChatDeployment   = "gpt-4o-mini"
$EmbedDeployment  = "text-embedding-3-small"
$EmbedDimensions  = 1536                 # text-embedding-3-small → vector(1536) ve schema.sql
$ChatTpm          = 10                    # TPM ×1000 = tvrdý strop útraty (viz Cost guardrails)
$EmbedTpm         = 30
# Repo image — DOPLNIT podle GitHub owner/repo:
$Image         = "ghcr.io/<OWNER>/<REPO>:latest"
```

### Krok 1 — Povolit pgvector na sdíleném Postgres serveru

> ⚠️ **Kritické:** `vector` se **přidá** k existující hodnotě, `timescaledb` NESMÍ zmizet (EDC ho používá). pgvector nevyžaduje `shared_preload_libraries` → **žádný restart**, EDC poběží dál bez výpadku.

```powershell
az postgres flexible-server parameter set `
  --resource-group $Rg --server-name $PgServer `
  --name azure.extensions --value "timescaledb,vector"
```

Ověření: `az postgres flexible-server parameter show -g $Rg --server-name $PgServer --name azure.extensions --query value -o tsv` → musí vrátit `timescaledb,vector`.

### Krok 2 — Databáze `chatbot-db` + dedikovaná role + extension

```powershell
az postgres flexible-server db create `
  --resource-group $Rg --server-name $PgServer --database-name $PgDb
```

Pak se připojit **jako admin `edcadmin`** k databázi `chatbot-db` (psql / azure portal query) a spustit:

```sql
-- Extension musí vytvořit admin (na Azure Flexible Serveru smí CREATE EXTENSION
-- jen člen azure_pg_admin / server admin). Indexer ji pak jen použije.
CREATE EXTENSION IF NOT EXISTS vector;

-- Dedikovaná aplikační role, jen pro chatbot-db
CREATE ROLE chatbot_app WITH LOGIN PASSWORD '<SILNE_HESLO>';
GRANT CONNECT ON DATABASE "chatbot-db" TO chatbot_app;
GRANT USAGE, CREATE ON SCHEMA public TO chatbot_app;   -- indexer si vytvoří tabulky přes schema.sql
ALTER DEFAULT PRIVILEGES IN SCHEMA public
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO chatbot_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA public
  GRANT USAGE, SELECT ON SEQUENCES TO chatbot_app;
```

> Pozn.: `schema.sql` indexeru obsahuje `CREATE EXTENSION IF NOT EXISTS vector` — to bude no-op (už existuje) a nesmí shodit běh, pokud role nemá práva na tvorbu extension. Ověřit, že je `IF NOT EXISTS` a že selhání tohoto příkazu je ošetřené (extension předvytváří admin v tomto kroku).

**Connection string pro aplikaci** (přes dedikovanou roli):
```
Host=edc-postgres-gtsmjb.postgres.database.azure.com;Port=5432;Database=chatbot-db;Username=chatbot_app;Password=<SILNE_HESLO>;Ssl Mode=Require;
```

Síť: server má povolený přístup z Azure služeb (stejně jako pro `edc-api`), takže `chatbot-api` ve stejném env se připojí bez další konfigurace firewallu.

### Krok 2b — Azure OpenAI (resource + deploye s TPM stropem)

Vlastní Azure OpenAI resource s deploji modelů. **TPM kvóta (`--sku-capacity`, ×1000 tokenů/min) je tvrdý strop útraty** — viz [Cost guardrails](#8-cost-guardrails-pojistky-proti-přečerpání-kreditů).

```powershell
az cognitiveservices account create `
  --name $OpenAIName --resource-group $Rg --location $OpenAILocation `
  --kind OpenAI --sku S0 --custom-domain $OpenAIName --yes

az cognitiveservices account deployment create `
  --name $OpenAIName --resource-group $Rg `
  --deployment-name $ChatDeployment --model-name gpt-4o-mini `
  --model-version "2024-07-18" --model-format OpenAI `
  --sku-name Standard --sku-capacity $ChatTpm        # tvrdý strop chatu

az cognitiveservices account deployment create `
  --name $OpenAIName --resource-group $Rg `
  --deployment-name $EmbedDeployment --model-name text-embedding-3-small `
  --model-version "1" --model-format OpenAI `
  --sku-name Standard --sku-capacity $EmbedTpm       # tvrdý strop embeddingů

$OpenAIEndpoint = az cognitiveservices account show --name $OpenAIName -g $Rg --query "properties.endpoint" -o tsv
$OpenAIKey      = az cognitiveservices account keys list --name $OpenAIName -g $Rg --query "key1" -o tsv
```

> ⚠️ `text-embedding-3-small` má **1536** dimenzí → `schema.sql` musí mít `vector(1536)` (ne 768 jako u Gemini). Viz [02-database.md](02-database.md).

### Krok 3 — Container App `chatbot-api` ve sdíleném env

První deploy s placeholder image (CI/CD ho nahradí), secrets a env dle [07-config-deployment.md](07-config-deployment.md). Mapování env používá dvojité podtržítko pro sekce Options patternu (`AzureOpenAI__ApiKey`, `Database__ConnectionString`).

```powershell
$ConnStr = "Host=$PgServer.postgres.database.azure.com;Port=5432;Database=$PgDb;Username=$PgAppRole;Password=<SILNE_HESLO>;Ssl Mode=Require;"

az containerapp create `
  --name $AppName --resource-group $Rg --environment $Env `
  --image "mcr.microsoft.com/azuredocs/containerapps-helloworld:latest" `
  --target-port 8080 --ingress external `
  --min-replicas 0 --max-replicas 2 `
  --cpu 0.25 --memory 0.5Gi `
  --secrets "db-conn=$ConnStr" "openai-key=$OpenAIKey" `
  --env-vars `
    "Database__ConnectionString=secretref:db-conn" `
    "AzureOpenAI__Endpoint=$OpenAIEndpoint" `
    "AzureOpenAI__ApiKey=secretref:openai-key" `
    "AzureOpenAI__ChatDeployment=$ChatDeployment" `
    "AzureOpenAI__EmbeddingDeployment=$EmbedDeployment" `
    "AzureOpenAI__EmbeddingDimensions=$EmbedDimensions" `
    "Cors__AllowedOrigins__0=https://www.enerkomhp.cz" `
    "Cors__AllowedOrigins__1=https://enerkomhp.cz" `
    "ASPNETCORE_URLS=http://+:8080"
```

- `--min-replicas 0` = scale-to-zero (klíč k ~0 nákladům; EDC má 1, chatbot nemusí).
- Health probe napojit na `/health` (viz [07](07-config-deployment.md)).

### Krok 4 — GitHub Actions deploy (ghcr.io)

1. Vytvořit **service principal** pro CI a uložit jako secret `AZURE_CREDENTIALS`:
   ```powershell
   az ad sp create-for-rbac --name chatbot-deploy `
     --role contributor `
     --scopes /subscriptions/$SubId/resourceGroups/$Rg `
     --sdk-auth
   ```
2. GitHub repo → Settings → Secrets → Actions:
   - `AZURE_CREDENTIALS` = JSON výstup výše.
   - (ghcr.io push jde přes vestavěný `GITHUB_TOKEN`, stejně jako u EDC.)
3. `.github/workflows/deploy-azure.yml` — zrcadlo EDC workflow, jen `CONTAINERAPP_NAME: chatbot-api`. Build → push `ghcr.io/<owner>/<repo>:<sha>` → `az containerapp update`. Po prvním běhu nahradí placeholder image.

> Pokud je repo privátní, Container App potřebuje registry credentials pro ghcr.io (`az containerapp registry set --server ghcr.io --username <user> --password <PAT s read:packages>`). Veřejný ghcr image to nepotřebuje.

### Krok 5 — Doména `chatbot.enerkom-hp.cz` + managed cert

Stejný postup jako EDC (`edc-data` má managed cert). FQDN container appky zjistit:
```powershell
$Fqdn = az containerapp show -n $AppName -g $Rg --query "properties.configuration.ingress.fqdn" -o tsv
```
1. **Cloudflare**: CNAME `chatbot` → `$Fqdn`. Pro validaci přidat i TXT `asuid.chatbot` s ověřovacím ID (`az containerapp hostname add` vypíše, co je třeba). SSL mód **Full**.
2. Přidat hostname + managed cert:
   ```powershell
   az containerapp hostname add --hostname $Domain --name $AppName --resource-group $Rg
   az containerapp hostname bind --hostname $Domain --name $AppName --resource-group $Rg `
     --environment $Env --validation-method CNAME
   ```
3. Cloudflare orange cloud (proxy) zapnout až po vystavení certu, ať validace projde (případně dočasně grey cloud — stejná zkušenost jako u EDC).

### Krok 6 — Indexer jako Container Apps Job (cron)

Krátkoběžný proces, spouštět plánovaně (default web 1×/den v noci). Stejné env, stejná `db-conn` + `openai-key`.

```powershell
az containerapp job create `
  --name $JobName --resource-group $Rg --environment $Env `
  --trigger-type Schedule --cron-expression "0 2 * * *" `
  --replica-timeout 1800 --replica-retry-limit 1 `
  --image $Image `
  --cpu 0.5 --memory 1.0Gi `
  --secrets "db-conn=$ConnStr" "openai-key=$OpenAIKey" `
  --env-vars `
    "Database__ConnectionString=secretref:db-conn" `
    "AzureOpenAI__Endpoint=$OpenAIEndpoint" `
    "AzureOpenAI__ApiKey=secretref:openai-key" `
    "AzureOpenAI__EmbeddingDeployment=$EmbedDeployment" `
    "AzureOpenAI__EmbeddingDimensions=$EmbedDimensions" `
    "Indexer__SitemapUrl=https://www.enerkomhp.cz/sitemap.xml" `
    "Indexer__DocumentsPath=/app/data/knowledge-base"
```

- Image indexeru = entrypoint `EnerkomChatbot.Indexer` (samostatný Dockerfile, nebo stejný image s jiným ENTRYPOINT/argumentem).
- Dokumenty z `data/knowledge-base` musí být v image (COPY) nebo na připojeném úložišti.
- Po doběhu zápis do `indexing_runs` (monitoring). Ruční spuštění prvního běhu: `az containerapp job start -n $JobName -g $Rg`.

### Krok 7 — Widget + vložení na web

- `widget.js` se servíruje z `wwwroot` `chatbot-api` (jeden původ → řeší CORS), URL `https://chatbot.enerkom-hp.cz/widget.js`.
- Na Webnode web `www.enerkomhp.cz` vložit `<script src="https://chatbot.enerkom-hp.cz/widget.js" data-...>` (Webnode umožní vložit HTML blok / kód do hlavičky).
- CORS na API uzamčen na `https://www.enerkomhp.cz` a `https://enerkomhp.cz` (krok 3).

---

## 4. Verifikace (akceptace deploye)

1. `curl https://chatbot.enerkom-hp.cz/health` → 200.
2. Cert platný (HTTPS bez varování), doména řeší na container app.
3. `POST /api/chat` s testovacím dotazem z obsahu webu → odpověď + citace (po prvním běhu indexeru).
4. Dotaz mimo obsah → „nevím" + kontakt (žádná halucinace).
5. Indexer job: ruční start proběhne úspěšně, `indexing_runs` má řádek `success`, `documents` se naplní.
6. Widget vložený na testovací stránku funguje proti produkční API; styly hostitele neovlivní widget.
7. **Regrese EDC:** `edc-data.enerkom-hp.cz/health` stále 200 (ověřit, že přidání `vector` do allowlistu a nová DB nic neshodily).

---

## 5. Náklady

| Položka | Náklad navíc |
|---|---|
| Container App `chatbot-api` (scale-to-zero) | ~0 (platí se jen za běh requestů) |
| Container Apps env | 0 (sdílené `edc-env`) |
| Postgres `chatbot-db` na sdíleném serveru | 0 (žádný nový server) |
| Indexer job (1×/den, pár minut) | zanedbatelné |
| Azure OpenAI (`gpt-4o-mini` + embeddingy, desítky dotazů/den) | haléře–jednotky $/měs (kryje neziskový kredit); zastropováno TPM kvótou |
| **Úspora navíc:** smazat osiřelý `edc-postgres-nbikqr` | −1× Standard_B1ms/měs |

Podrobné pojistky proti přečerpání kreditů → sekce 8.

---

## 6. Bezpečnost a rollback

- **Least privilege:** chatbot má vlastní DB roli omezenou na `chatbot-db`; nemá přístup k EDC datům.
- **Secrets** jen jako Container App secrets / GitHub secrets, nikdy v repu.
- **Azure OpenAI klíč** neopouští backend (widget ho nikdy nevidí). Pro produkci zvážit místo klíče **managed identity** (Entra ID) na Container App + roli `Cognitive Services OpenAI User` — bez klíče v secretech.
- **Rollback appky:** `az containerapp revision list/activate` — revize jsou immutable, návrat na předchozí je okamžitý.
- **Rollback DB:** nová DB je izolovaná; v nejhorším `DROP DATABASE "chatbot-db"` + revoke role, EDC nedotčeno.
- **Žádný restart Postgres** během celého deploye → EDC bez výpadku.

---

## 7. Hodnoty k doplnění agentem

- `<OWNER>/<REPO>` — GitHub repo chatbota (po jeho založení).
- `<SILNE_HESLO>` — heslo role `chatbot_app` (vygenerovat, uložit do secret store).
- **Azure OpenAI** — endpoint i klíč vytvoří/načte `setup.ps1` (krok 2b). Ověřit dostupnost modelů `gpt-4o-mini` / `text-embedding-3-small` v regionu (`az cognitiveservices account list-models -n $OpenAIName -g $Rg`) a aktuální verze modelů.
- Ověřit přesný tvar `appsettings` klíčů vůči implementaci (`AzureOpenAI:Endpoint`, `AzureOpenAI:ApiKey`, `Database:ConnectionString`) a sladit s env mapováním v krocích 3 a 6.

---

## 8. Cost guardrails (pojistky proti přečerpání kreditů)

> Kontext: neziskové Azure kredity jsou **na rok** a sdílí je celé předplatné (i EDC). Cílem je, aby chatbot kredity **nemohl vyčerpat**.

Od nejtvrdší pojistky po nejměkčí:

1. **TPM kvóta na deploji = tvrdý technický strop ⭐** — `--sku-capacity` u `az cognitiveservices account deployment create` (×1000 tokenů/min: chat `$ChatTpm`=10k, embed `$EmbedTpm`=30k). Model fyzicky nezpracuje víc → maximální měsíční útrata je matematicky omezená, i při zneužití. Toto je jediný skutečný *hard stop* (ne jen alert). Tune dle potřeby.
2. **Typ předplatného** — pokud jsou kredity přes **Azure Sponsorship**, předplatné se po vyčerpání kreditu **deaktivuje** (nefakturuje, nestrhává z karty). Ověřit: `az account show --query "subscriptionPolicies"` / Portal → Subscriptions → typ. Nejhorší scénář pak = služby přestanou fungovat, ne přečerpání.
3. **Budget alert** (Cost Management) — Portal → Cost Management → Budgets → nový budget na `rg-edc` (např. 20 €/měs) s alertem na 50/80/100 %. ⚠️ Budget **jen upozorní e-mailem**, neutne útratu. Tvrdý stop přes budget jde dodělat: alert → action group → Logic App/Automation runbook, který deployment vypne (volitelné, díky bodu 1 většinou netřeba).
4. **Limity v aplikaci** — rate limiter na `/api/chat`, `AzureOpenAI:MaxOutputTokens` (800), omezený retrieval kontext (TopK) → zastropují cenu jednoho dotazu a chrání před zneužitím (viz [04](04-chat-api.md)/[05](05-prompts.md)).

Pořadí nasazení: body 1 a 4 nastaví `setup.ps1` / kód, body 2 a 3 jsou ruční ověření/nastavení v Portálu.
