# Builds application using dotnet's sdk
FROM mcr.microsoft.com/dotnet/sdk:5.0 AS build

WORKDIR /
COPY ./DiscordBot/ ./app/
WORKDIR /app/

RUN dotnet restore
RUN dotnet build --configuration Release --no-restore


# Build finale image
FROM mcr.microsoft.com/dotnet/runtime:5.0

WORKDIR /app/

COPY --from=build /app/bin/Release/netcoreapp5.0/ ./

RUN echo "deb http://httpredir.debian.org/debian buster main contrib" > /etc/apt/sources.list
RUN echo "deb http://security.debian.org/ buster/updates main contrib" >> /etc/apt/sources.list
RUN echo "ttf-mscorefonts-installer msttcorefonts/accepted-mscorefonts-eula select true" | debconf-set-selections
RUN apt update
RUN apt install -y ttf-mscorefonts-installer
RUN apt clean
RUN apt autoremove -y
RUN rm -rf /var/lib/apt/lists/

ENTRYPOINT ["./DiscordBot"]
