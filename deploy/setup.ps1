Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# =============================================================================
# Couch (chatbot) — jednorázový setup Azure infrastruktury
# Nasazuje se VEDLE EDC do stávajícího předplatného (sdílené env + Postgres server).
# LLM/embeddings: Azure OpenAI. Viz docs/09-azure-deploy.md.
#
# Spusť: pwsh deploy/setup.ps1   (po vyplnění deploy/secrets.env)
# Prerekvizity: az CLI přihlášený do správného předplatného.
# =============================================================================

# ── Sdílené (existující) zdroje — NEMĚNIT bez ověření ────────────────────────
$SubId        = "594e58df-c16e-47e0-9dd9-c404efd67701"
$Rg           = "rg-edc"
$Location     = "northeurope"                 # musí sedět s env edc-env
$Env          = "edc-env"                     # sdílené Container Apps Environment (s EDC)
$PgServer     = "edc-postgres-gtsmjb"         # aktivní Postgres server (sdílený s EDC)
$PgAdmin      = "edcadmin"

# ── Nové zdroje pro chatbota ─────────────────────────────────────────────────
$PgDb         = "chatbot-db"
$PgAppRole    = "chatbot_app"
$AppName      = "chatbot-api"
$JobName      = "chatbot-indexer"
$Domain       = "chatbot.enerkom-hp.cz"

# ── Azure OpenAI ─────────────────────────────────────────────────────────────
$OpenAIName       = "chatbot-openai"
$OpenAILocation   = "swedencentral"           # EU; ověř dostupnost modelů: az cognitiveservices account list-models
$ChatDeployment   = "gpt-4o-mini"
$ChatModelVersion = "2024-07-18"              # ověř aktuální verzi
$EmbedDeployment  = "text-embedding-3-small"
$EmbedModelVersion= "1"
$EmbedDimensions  = 1536
# TPM kvóta = TVRDÝ strop útraty (×1000 tokenů/min). Nízká hodnota = nelze přečerpat kredity.
$ChatTpm          = 10                         # 10k TPM
$EmbedTpm         = 30                         # 30k TPM (vyšší kvůli dávkové indexaci)

# Placeholder image pro první deploy — CI/CD ji nahradí skutečnou po push do main
$PlaceholderImage = "mcr.microsoft.com/azuredocs/containerapps-helloworld:latest"
# ─────────────────────────────────────────────────────────────────────────────

# Načtení secrets
$SecretsFile = Join-Path $PSScriptRoot "secrets.env"
if (-not (Test-Path $SecretsFile)) { Write-Error "Chybí deploy/secrets.env (zkopíruj secrets.env.example)"; exit 1 }
$secrets = @{}
Get-Content $SecretsFile | Where-Object { $_ -match '^[^#].+=' } | ForEach-Object {
    $parts = $_ -split '=', 2
    $secrets[$parts[0].Trim()] = ($parts[1] -split '#')[0].Trim()
}
$AppPassword = $secrets['POSTGRES_APP_PASSWORD']
if ([string]::IsNullOrWhiteSpace($AppPassword)) {
    Write-Error "V deploy/secrets.env vyplň POSTGRES_APP_PASSWORD"; exit 1
}
# Volitelně už existující Azure OpenAI
$ExistingEndpoint = if ($secrets.ContainsKey('AZURE_OPENAI_ENDPOINT')) { $secrets['AZURE_OPENAI_ENDPOINT'] } else { "" }
$ExistingKey      = if ($secrets.ContainsKey('AZURE_OPENAI_KEY'))      { $secrets['AZURE_OPENAI_KEY'] }      else { "" }

Write-Host "=== Ověření předplatného ===" -ForegroundColor Cyan
az account set --subscription $SubId
az account show --query "{sub:name, id:id, user:user.name}" -o table

Write-Host "`n=== 1/6 Povolení pgvector (append k timescaledb, BEZ restartu) ===" -ForegroundColor Cyan
$current = az postgres flexible-server parameter show -g $Rg --server-name $PgServer --name azure.extensions --query value -o tsv
Write-Host "azure.extensions teď: $current"
if ($current -notmatch '(^|,)\s*vector\s*($|,)') {
    $new = ($current.Trim() -eq "") ? "vector" : "$current,vector"
    az postgres flexible-server parameter set -g $Rg --server-name $PgServer --name azure.extensions --value $new
    Write-Host "Nastaveno na: $new" -ForegroundColor Green
} else {
    Write-Host "vector už je povolen — přeskakuji." -ForegroundColor Green
}

Write-Host "`n=== 2/6 Databáze $PgDb ===" -ForegroundColor Cyan
az postgres flexible-server db create -g $Rg --server-name $PgServer --database-name $PgDb

Write-Host "`n--- RUČNÍ KROK: spusť deploy/db-setup.sql jako admin ($PgAdmin) proti $PgDb ---" -ForegroundColor Yellow
Write-Host "psql `"host=$PgServer.postgres.database.azure.com port=5432 dbname=$PgDb user=$PgAdmin sslmode=require`" -v app_password=`"'$AppPassword'`" -f deploy/db-setup.sql"
Read-Host "Až db-setup.sql proběhne (vytvoří extension vector + roli $PgAppRole), stiskni Enter pro pokračování"

Write-Host "`n=== 3/6 Azure OpenAI ($OpenAIName) + deploye s TPM stropem ===" -ForegroundColor Cyan
if (-not [string]::IsNullOrWhiteSpace($ExistingEndpoint) -and -not [string]::IsNullOrWhiteSpace($ExistingKey)) {
    Write-Host "Používám existující Azure OpenAI z secrets.env (přeskakuji tvorbu)." -ForegroundColor Green
    $OpenAIEndpoint = $ExistingEndpoint
    $OpenAIKey      = $ExistingKey
} else {
    az cognitiveservices account create `
        --name $OpenAIName --resource-group $Rg --location $OpenAILocation `
        --kind OpenAI --sku S0 --custom-domain $OpenAIName --yes
    # Chat deployment s TPM stropem (tvrdá pojistka proti přečerpání kreditů)
    az cognitiveservices account deployment create `
        --name $OpenAIName --resource-group $Rg `
        --deployment-name $ChatDeployment --model-name gpt-4o-mini `
        --model-version $ChatModelVersion --model-format OpenAI `
        --sku-name Standard --sku-capacity $ChatTpm
    # Embedding deployment s TPM stropem
    az cognitiveservices account deployment create `
        --name $OpenAIName --resource-group $Rg `
        --deployment-name $EmbedDeployment --model-name text-embedding-3-small `
        --model-version $EmbedModelVersion --model-format OpenAI `
        --sku-name Standard --sku-capacity $EmbedTpm
    $OpenAIEndpoint = az cognitiveservices account show --name $OpenAIName -g $Rg --query "properties.endpoint" -o tsv
    $OpenAIKey      = az cognitiveservices account keys list --name $OpenAIName -g $Rg --query "key1" -o tsv
}
Write-Host "Endpoint: $OpenAIEndpoint" -ForegroundColor Green

$ConnStr = "Host=$PgServer.postgres.database.azure.com;Port=5432;Database=$PgDb;Username=$PgAppRole;Password=$AppPassword;Ssl Mode=Require;"

Write-Host "`n=== 4/6 Container App $AppName (sdílené env, scale-to-zero) ===" -ForegroundColor Cyan
az containerapp create `
    --name $AppName --resource-group $Rg --environment $Env `
    --image $PlaceholderImage `
    --target-port 8080 --ingress external `
    --min-replicas 0 --max-replicas 2 `
    --cpu 0.25 --memory 0.5Gi `
    --secrets ("db-conn=$ConnStr") ("openai-key=$OpenAIKey") `
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

Write-Host "`n=== 5/6 Indexer job $JobName (cron 02:00 denně) ===" -ForegroundColor Cyan
az containerapp job create `
    --name $JobName --resource-group $Rg --environment $Env `
    --trigger-type Schedule --cron-expression "0 2 * * *" `
    --replica-timeout 1800 --replica-retry-limit 1 `
    --image $PlaceholderImage `
    --cpu 0.5 --memory 1.0Gi `
    --secrets ("db-conn=$ConnStr") ("openai-key=$OpenAIKey") `
    --env-vars `
        "Database__ConnectionString=secretref:db-conn" `
        "AzureOpenAI__Endpoint=$OpenAIEndpoint" `
        "AzureOpenAI__ApiKey=secretref:openai-key" `
        "AzureOpenAI__EmbeddingDeployment=$EmbedDeployment" `
        "AzureOpenAI__EmbeddingDimensions=$EmbedDimensions" `
        "Indexer__SitemapUrl=https://www.enerkomhp.cz/sitemap.xml" `
        "Indexer__DocumentsPath=/app/data/knowledge-base"

Write-Host "`n=== 6/6 Výsledek ===" -ForegroundColor Cyan
$Fqdn = az containerapp show -n $AppName -g $Rg --query "properties.configuration.ingress.fqdn" -o tsv
Write-Host "Container App URL: https://$Fqdn" -ForegroundColor Green

Write-Host "`nDalší kroky (ručně):" -ForegroundColor Yellow
Write-Host "  1. Cloudflare: CNAME 'chatbot' -> $Fqdn  + TXT 'asuid.chatbot' (ID z 'az containerapp hostname add')"
Write-Host "  2. Doména + managed cert:"
Write-Host "       az containerapp hostname add  --hostname $Domain --name $AppName --resource-group $Rg"
Write-Host "       az containerapp hostname bind --hostname $Domain --name $AppName --resource-group $Rg --environment $Env --validation-method CNAME"
Write-Host "     (orange cloud v Cloudflare zapni až po vystavení certu; SSL mód Full)"
Write-Host "  3. Service principal pro GitHub Actions (secret AZURE_CREDENTIALS):"
Write-Host "       az ad sp create-for-rbac --name chatbot-deploy --role contributor --scopes /subscriptions/$SubId/resourceGroups/$Rg --sdk-auth"
Write-Host "  4. Push do main -> workflow .github/workflows/deploy-azure.yml nahradí placeholder image."
Write-Host "  5. První indexace ručně: az containerapp job start -n $JobName -g $Rg"
Write-Host ""
Write-Host "POJISTKY NÁKLADŮ (viz docs/09 sekce Cost guardrails):" -ForegroundColor Yellow
Write-Host "  - TVRDÝ strop je TPM kvóta deploymentů (chat=$ChatTpm k, embed=$EmbedTpm k) — nelze přečerpat víc."
Write-Host "  - Ověř typ předplatného: az account show --query 'subscriptionPolicies'  (Sponsorship se po vyčerpání kreditu zastaví, nefakturuje)."
Write-Host "  - Nastav Budget alert v Cost Management (Portal -> Cost Management -> Budgets) na rg-edc, např. 20 EUR/měs."
