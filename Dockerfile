# Dockerfile — EnerkomChatbot.Api (chatbot backend + widget)
#
# PŘEDPOKLADY (viz docs/01-project-structure.md, docs/09-azure-deploy.md):
#   - src/EnerkomChatbot.Api/EnerkomChatbot.Api.csproj (referencuje EnerkomChatbot.Core)
#   - src/EnerkomChatbot.Core/
#   - web/widget/ s package.json + vite (npm run build → web/widget/dist/widget.js)
#   - EnerkomChatbot.Api servíruje statiku z wwwroot (UseStaticFiles / MapStaticAssets)
#   - EnerkomChatbot.Api naslouchá na :8080 a má GET /health
# Build context = kořen repozitáře.

# ---- Stage 1: build widgetu (Vite → IIFE bundle widget.js) ----
FROM node:22-alpine AS widget
WORKDIR /widget
COPY web/widget/package*.json ./
RUN npm ci
COPY web/widget/ ./
RUN npm run build
# výstup: /widget/dist/  (widget.js + případné assety)

# ---- Stage 2: build & publish .NET API ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY EnerkomChatbot.slnx Directory.Build.props Directory.Packages.props NuGet.config ./
COPY src/EnerkomChatbot.Core/ src/EnerkomChatbot.Core/
COPY src/EnerkomChatbot.Api/ src/EnerkomChatbot.Api/
RUN dotnet restore src/EnerkomChatbot.Api/EnerkomChatbot.Api.csproj
RUN dotnet publish src/EnerkomChatbot.Api/EnerkomChatbot.Api.csproj -c Release -o /app/publish --no-restore

# ---- Stage 3: runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./
# Widget servírovaný ze stejného původu jako API (řeší CORS pro statiku)
COPY --from=widget /widget/dist/ ./wwwroot/

ENV ASPNETCORE_URLS=http://+:8080
ENV PORT=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "EnerkomChatbot.Api.dll"]
