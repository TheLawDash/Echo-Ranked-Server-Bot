# EchoRankedServerBot

A Discord bot for managing ranked Echo VR matches, integrating Nakama, NeatQueue, and Discord to automate match lifecycle, stats tracking, and server allocation.

## Features

- **Match lifecycle management** -- monitor, create, and track ranked matches end-to-end
- **Server allocation** -- automatic server decision-making based on player latency and geolocation
- **Player stats tracking** -- per-match stat recording with PostgreSQL persistence
- **Scoreboard generation** -- rendered match scoreboards via SkiaSharp
- **Alt detection** -- identify alternate accounts across players
- **Player watch system** -- flag and monitor specific players
- **NeatQueue integration** -- queue management and ranked matchmaking
- **Live match updates** -- real-time match status posted to Discord channels

## Tech Stack

- .NET 10 (C# console application)
- Discord.Net 3.17
- SkiaSharp for image rendering
- PostgreSQL with Entity Framework Core (Npgsql)
- Nakama game backend
- Microsoft.Extensions.Hosting for DI and hosted services

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- PostgreSQL instance
- Discord bot token (with appropriate guild permissions)
- Nakama server credentials
- NeatQueue API key

## Configuration

Copy the example config and fill in your values:

```bash
cp EchoRankedServerBot/appsettings.example.json EchoRankedServerBot/appsettings.json
```

All sensitive values can be set via environment variables:

| Environment Variable | Description |
|---|---|
| `EchoRankedDiscordToken` | Discord bot token |
| `EchoRankedPostgresConnection` | PostgreSQL connection string |
| `EchoRankedNakamaUsername` | Nakama auth username |
| `EchoRankedNakamaPassword` | Nakama auth password |
| `EchoRankedNakamaHttpKey` | Nakama HTTP key |
| `EchoRankedNeatQueueApiKey` | NeatQueue API key |

The `Bot` section in `appsettings.json` requires Discord guild/channel/role IDs. The `Nakama` and `Api` sections configure external service endpoints. See `appsettings.example.json` for the full structure.

## Build & Run

```bash
dotnet build
dotnet run --project EchoRankedServerBot
```

Database migrations are applied automatically on startup, so no manual migration step is needed.

## Project Structure

```
EchoRankedServerBot/
  Program.cs                 # Entry point
  BackgroundServices/        # Hosted services (DiscordBotService, MatchMonitorService)
  Commands/                  # Discord slash commands (MatchCommandModule)
  Configuration/             # Options classes (BotOptions, NakamaOptions, ApiOptions)
  Data/                      # DbContext, entities, migrations
  Extensions/                # Service registration and config helpers
  Handlers/                  # Discord event handlers (ready, messages, channels)
  Models/                    # Domain models (Match, Stats, Nakama, Streaming, etc.)
  Services/                  # Core logic (NakamaApi, MatchLifecycle, AltDetection,
                             # Scoreboard, NeatQueue, ServerDecision, and more)
  Assets/                    # Image assets for scoreboard rendering
```
