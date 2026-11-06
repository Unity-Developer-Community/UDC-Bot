# Build context: the repository root (docker build -f CasinoWeb/Dockerfile .)

# Client stage. The client imports the slot pictures from the bot's asset folder by a relative path, so that folder
# is copied in at the same place relative to ClientApp.
FROM node:24-slim AS client
WORKDIR /src/CasinoWeb/ClientApp
COPY CasinoWeb/ClientApp/package.json CasinoWeb/ClientApp/package-lock.json ./
RUN npm ci
COPY CasinoWeb/ClientApp/ ./
COPY DiscordBot/Assets/slots/ /src/DiscordBot/Assets/slots/
# vite.config.ts writes the build to ../wwwroot
RUN npm run build

# Server stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY NuGet.config ./
COPY DiscordBot/DiscordBot.csproj DiscordBot/
COPY CasinoWeb/CasinoWeb.csproj CasinoWeb/
RUN dotnet restore CasinoWeb/CasinoWeb.csproj
COPY DiscordBot/ DiscordBot/
COPY CasinoWeb/ CasinoWeb/
COPY --from=client /src/CasinoWeb/wwwroot/ CasinoWeb/wwwroot/
RUN dotnet publish CasinoWeb/CasinoWeb.csproj --configuration Release --no-restore --output /app/publish

# Runtime stage. The .NET 8 images define the non-root user `app`; $APP_UID is its numeric id, which Kubernetes'
# runAsNonRoot needs. Settings/ (the bot's rendered Settings.json) and /keys (Data Protection keys) are mounted at runtime.
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish/ ./
# The art and font of the slots picture posted with a big win (Casino__AssetsPath defaults to ./Assets).
COPY DiscordBot/Assets/slots/ Assets/slots/
COPY DiscordBot/Assets/fonts/ConsolasBold.ttf Assets/fonts/ConsolasBold.ttf
USER $APP_UID
ENV Casino__KeysPath=/keys
# Listens on 8080 (ASPNETCORE_HTTP_PORTS default of the .NET 8 images)
EXPOSE 8080
ENTRYPOINT ["dotnet", "CasinoWeb.dll"]
