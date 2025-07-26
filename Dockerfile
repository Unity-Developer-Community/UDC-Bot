# Builds application using dotnet's sdk
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

WORKDIR /
COPY ./DiscordBot/ ./app/
WORKDIR /app/

RUN dotnet restore
RUN dotnet publish --configuration Release --no-restore --output /app/publish


# Build finale image
FROM mcr.microsoft.com/dotnet/runtime:8.0

WORKDIR /app/

COPY --from=build /app/publish/ ./

RUN echo "deb http://deb.debian.org/debian bullseye main contrib" > /etc/apt/sources.list
RUN echo "deb http://security.debian.org/ bullseye-security main contrib" >> /etc/apt/sources.list
RUN echo "ttf-mscorefonts-installer msttcorefonts/accepted-mscorefonts-eula select true" | debconf-set-selections
RUN apt update
RUN apt install -y ttf-mscorefonts-installer
RUN apt clean
RUN apt autoremove -y
RUN rm -rf /var/lib/apt/lists/

ENTRYPOINT ["./DiscordBot"]
