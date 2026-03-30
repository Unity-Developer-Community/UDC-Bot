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
COPY ./SERVER/fonts/ ./assets/fonts/
COPY ./SERVER/images/ ./assets/images/
COPY ./SERVER/skins/ ./assets/skins/

# Add contrib repo for MS fonts, matching the base image's Debian codename
RUN . /etc/os-release && \
    echo "deb https://deb.debian.org/debian ${VERSION_CODENAME} contrib" > /etc/apt/sources.list.d/contrib.list && \
    echo "deb https://security.debian.org/debian-security ${VERSION_CODENAME}-security contrib" >> /etc/apt/sources.list.d/contrib.list && \
    echo "ttf-mscorefonts-installer msttcorefonts/accepted-mscorefonts-eula select true" | debconf-set-selections && \
    apt-get update && \
    apt-get install -y --no-install-recommends ttf-mscorefonts-installer && \
    apt-get autoremove -y && \
    apt-get clean && \
    rm -rf /var/lib/apt/lists/*

ENTRYPOINT ["./DiscordBot"]
