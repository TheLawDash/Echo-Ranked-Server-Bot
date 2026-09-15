FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY EchoRankedServerBot/EchoRankedServerBot.csproj EchoRankedServerBot/
RUN dotnet restore EchoRankedServerBot/EchoRankedServerBot.csproj

COPY EchoRankedServerBot/ EchoRankedServerBot/
RUN dotnet publish EchoRankedServerBot/EchoRankedServerBot.csproj \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN apt-get update \
 && apt-get install -y --no-install-recommends \
        ca-certificates \
        fontconfig \
        fonts-dejavu-core \
        fonts-liberation \
        libfontconfig1 \
        libfreetype6 \
 && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish ./

ENV DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_USE_POLLING_FILE_WATCHER=true \
    DOTNET_EnableDiagnostics=0

USER 1654:1654
ENTRYPOINT ["dotnet", "EchoRankedServerBot.dll"]
