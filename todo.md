# TODO — EnerkomChatbot (chatbot) → nasazení do Azure

Cíl: nasadit chatbota vedle EDC do **stávajícího** předplatného (sdílené `rg-edc` + `edc-env` + Postgres `edc-postgres-gtsmjb`), doména `chatbot.enerkom-hp.cz`, náklady ~0 navíc.
Detailní plán: [docs/09-azure-deploy.md](docs/09-azure-deploy.md) · Artefakty: `Dockerfile`, `Dockerfile.indexer`, `deploy/`, `.github/workflows/deploy-azure.yml`.

---

## A. Předpoklady — implementace (musí být HOTOVO před deployem)

Pořadí dle [docs/08-implementation-plan.md](docs/08-implementation-plan.md):

- [ ] **T0.1** Skeleton: `EnerkomChatbot.slnx`, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`, projekty `EnerkomChatbot.Core` / `EnerkomChatbot.Indexer` / `EnerkomChatbot.Api` (+ testy). TFM `net10.0`.
- [ ] **T1.x** Databáze a Core: `schema.sql` (`vector(1536)`), modely + abstrakce, `PgVectorStore`, `Chunker`, Azure OpenAI klienti (přes `Azure.AI.OpenAI` + `Microsoft.Extensions.AI.OpenAI`).
- [ ] **T2.x** Indexer: DocumentLoader (PDF/DOCX/MD), WebCrawler, IndexingPipeline + `Program.cs`.
- [ ] **T3.x** Chat API: ChatService + PromptBuilder, endpointy (`POST /api/chat`, `GET /health`), CORS, rate limit.
- [ ] **T4.1** Widget: `web/widget/package.json` + vite (build → `web/widget/dist/widget.js`), Shadow DOM, maskot Elektron.
- [ ] **Sladit konfiguraci:** klíče env (`Database__ConnectionString`, `AzureOpenAI__Endpoint`, `AzureOpenAI__ApiKey`, `AzureOpenAI__ChatDeployment`, `AzureOpenAI__EmbeddingDeployment`, `Cors__AllowedOrigins__*`) musí odpovídat tomu, jak je čte API/Indexer (Options pattern).
- [ ] **Azure OpenAI** — viz sekce A2 (resource vytvoří `setup.ps1`).

## A2. Azure OpenAI — modely a deploye

> LLM + embeddings na Azure OpenAI. Resource + deploye vytvoří `deploy/setup.ps1` (krok 3) — žádná samostatná infrastruktura navíc.

- [ ] Ověřit dostupnost modelů v regionu: `az cognitiveservices account list-models -n chatbot-openai -g rg-edc` (chat `gpt-4o-mini`, embed `text-embedding-3-small`) + aktuální verze modelů a `ApiVersion`.
- [ ] `setup.ps1` vytvoří resource `chatbot-openai` (Sweden Central / EU) + 2 deploye s **TPM stropem** (chat 10k, embed 30k) → endpoint a klíč si načte sám.
- [ ] Embedding dimenze **1536** (`text-embedding-3-small`) musí sedět s `vector(1536)` ve `schema.sql`.
- [ ] (Doporučeno pro produkci) místo API klíče **managed identity** na Container App + role `Cognitive Services OpenAI User`.
- [ ] GDPR: data zůstávají v Azure tenantu (EU), Microsoft je netrénuje → uvést odeslání dotazů Azure OpenAI v zásadách zpracování.

## B. Repo a CI/CD příprava

- [ ] `git init` v `d:eposkromenerkom-chatbot` + remote na GitHub, push.
- [ ] Service principal → secret `AZURE_CREDENTIALS`:
      `az ad sp create-for-rbac --name chatbot-deploy --role contributor --scopes /subscriptions/594e58df-c16e-47e0-9dd9-c404efd67701/resourceGroups/rg-edc --sdk-auth`
- [ ] (Jen privátní repo) ghcr.io credentials pro Container App: `az containerapp registry set --server ghcr.io ...`.
- [ ] `deploy/secrets.env` (z `deploy/secrets.env.example`): vyplnit `POSTGRES_APP_PASSWORD` (Azure OpenAI klíč si `setup.ps1` vytvoří/načte sám).

## C. Azure infrastruktura (jednorázově — `deploy/setup.ps1`)

- [ ] `az login` do správného předplatného (`594e58df-…`, účet enerkom-hp).
- [ ] **Krok 1** Povolit pgvector: `azure.extensions` = `timescaledb,vector` (**append**, ne přepsat; bez restartu serveru).
- [ ] **Krok 2** Vytvořit DB `chatbot-db` na `edc-postgres-gtsmjb`.
- [ ] **Krok 3** Spustit `deploy/db-setup.sql` jako admin `edcadmin` proti `chatbot-db` (extension + role `chatbot_app`, heslo = `POSTGRES_APP_PASSWORD`).
- [ ] **Krok 3** (2b) Vytvořit Azure OpenAI `chatbot-openai` + deploye `gpt-4o-mini` a `text-embedding-3-small` s **TPM stropem**.
- [ ] **Krok 4** Vytvořit Container App `chatbot-api` ve `edc-env` (scale-to-zero, secrets, env).
- [ ] **Krok 5** Vytvořit indexer Job `chatbot-indexer` (cron `0 2 * * *`).

## D. Doména a deploy

- [ ] Cloudflare: CNAME `chatbot` → FQDN appky + TXT `asuid.chatbot` (validace).
- [ ] `az containerapp hostname add` + `hostname bind` (managed cert) pro `chatbot.enerkom-hp.cz`.
- [ ] Cloudflare orange cloud zapnout až po vystavení certu; SSL mód **Full**.
- [ ] Push do `main` → workflow `deploy-azure.yml` nahradí placeholder image (API + indexer).
- [ ] První indexace ručně: `az containerapp job start -n chatbot-indexer -g rg-edc`.
- [ ] Vložit `<script src="https://chatbot.enerkom-hp.cz/widget.js" data-...>` na web `www.enerkomhp.cz` (Webnode).

## E. Verifikace (akceptace)

- [ ] `curl https://chatbot.enerkom-hp.cz/health` → 200, cert platný.
- [ ] `POST /api/chat` dotaz z obsahu → odpověď + citace; dotaz mimo obsah → „nevím" + kontakt.
- [ ] `indexing_runs` má řádek `success`, tabulka `documents` naplněna.
- [ ] Widget na testovací stránce funguje proti produkční API; styly hostitele neovlivní widget.
- [ ] **Regrese EDC:** `https://edc-data.enerkom-hp.cz/health` stále 200 (pgvector + nová DB nic neshodily).

## E2. Cost guardrails (pojistky proti přečerpání ročních kreditů)

> Detail v [docs/09-azure-deploy.md](docs/09-azure-deploy.md) sekce 8. Kredity sdílí celé předplatné s EDC.

- [ ] **TPM kvóta na deployech = tvrdý strop** (nastaví `setup.ps1`: chat 10k, embed 30k). Ověřit/doladit hodnoty. ⭐ jediný skutečný hard-stop.
- [ ] Ověřit **typ předplatného** (`az account show --query "subscriptionPolicies"`): Sponsorship se po vyčerpání kreditu zastaví, nefakturuje.
- [x] **Budget alerty nastaveny** (subscription scope, EUR, alerty 50/80/100 % actual + 100 % forecast, e-maily krobot@enerkom-hp.cz + krobot.michal@gmail.com):
      - `ai-openai-monthly-20` = **20 €/měs**, filtr jen na AI (ResourceType `microsoft.cognitiveservices/accounts`).
      - `subscription-monthly-100` = **100 €/měs** celkově.
      - ⚠️ Pozn.: budget jen upozorní e-mailem; tvrdý strop AI je TPM kvóta deploymentu.
- [ ] Ověřit app-level limity: rate limiter na `/api/chat`, `AzureOpenAI:MaxOutputTokens=800`, omezený TopK.

## F. Úklid / nice-to-have

- [ ] Zvážit smazání osiřelého Postgres serveru `edc-postgres-nbikqr` (duplikát, platí se zbytečně) — po ověření, že ho nic nepoužívá.
- [ ] Lehký monitoring: logy API (počet dotazů, % `answered=false`, počet 429).
