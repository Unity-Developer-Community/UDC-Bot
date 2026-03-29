# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

WORKDIR /app/
COPY ./NuGet.config ./
COPY ./DiscordBot/DiscordBot.csproj ./
RUN dotnet restore
COPY ./DiscordBot/ ./
RUN dotnet publish --configuration Release --no-restore --output /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/runtime:8.0

WORKDIR /app/

COPY --from=build /app/publish/ ./

# Bake immutable static assets (fonts, images, skins) into the image
COPY ./SERVER/fonts/ ./SERVER/fonts/
COPY ./SERVER/images/ ./SERVER/images/
COPY ./SERVER/skins/ ./SERVER/skins/

# Add contrib repo for MS fonts (bookworm, matching the base image)
RUN echo "deb http://deb.debian.org/debian bookworm contrib" > /etc/apt/sources.list.d/contrib.list && \
    echo "ttf-mscorefonts-installer msttcorefonts/accepted-mscorefonts-eula select true" | debconf-set-selections && \
    apt-get update && \
    apt-get install -y --no-install-recommends ttf-mscorefonts-installer && \
    apt-get clean && \
    apt-get autoremove -y && \
    rm -rf /var/lib/apt/lists/*

ENTRYPOINT ["./DiscordBot"]
