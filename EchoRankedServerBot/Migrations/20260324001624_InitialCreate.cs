using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace EchoRankedServerBot.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "player_identities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DiscordId = table.Column<string>(type: "text", nullable: false),
                    NakamaId = table.Column<string>(type: "text", nullable: false),
                    EvrId = table.Column<string>(type: "text", nullable: true),
                    PlayerName = table.Column<string>(type: "text", nullable: true),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_player_identities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "player_match_stats",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PlayerName = table.Column<string>(type: "text", nullable: false),
                    PlayerId = table.Column<long>(type: "bigint", nullable: false),
                    DiscordUsername = table.Column<string>(type: "text", nullable: true),
                    DiscordId = table.Column<string>(type: "text", nullable: true),
                    EvrId = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<string>(type: "text", nullable: true),
                    UserIp = table.Column<string>(type: "text", nullable: true),
                    MatchName = table.Column<string>(type: "text", nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Win = table.Column<bool>(type: "boolean", nullable: false),
                    Lose = table.Column<bool>(type: "boolean", nullable: false),
                    Mvp = table.Column<bool>(type: "boolean", nullable: false),
                    MvpScore = table.Column<double>(type: "double precision", nullable: false),
                    Points = table.Column<int>(type: "integer", nullable: false),
                    Saves = table.Column<int>(type: "integer", nullable: false),
                    Assists = table.Column<int>(type: "integer", nullable: false),
                    PossessionTime = table.Column<double>(type: "double precision", nullable: false),
                    Stuns = table.Column<int>(type: "integer", nullable: false),
                    Passes = table.Column<int>(type: "integer", nullable: false),
                    Catches = table.Column<int>(type: "integer", nullable: false),
                    Steals = table.Column<int>(type: "integer", nullable: false),
                    Blocks = table.Column<int>(type: "integer", nullable: false),
                    Interceptions = table.Column<int>(type: "integer", nullable: false),
                    Goals = table.Column<int>(type: "integer", nullable: false),
                    ShotsTaken = table.Column<int>(type: "integer", nullable: false),
                    LongBounceShots = table.Column<int>(type: "integer", nullable: false),
                    ThreePointShots = table.Column<int>(type: "integer", nullable: false),
                    TwoPointShots = table.Column<int>(type: "integer", nullable: false),
                    ShortBounceShots = table.Column<int>(type: "integer", nullable: false),
                    ThrowDistances = table.Column<string>(type: "text", nullable: true),
                    ShotSpeeds = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_player_match_stats", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "watched_players",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DiscordId = table.Column<string>(type: "text", nullable: false),
                    IpAddress = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_watched_players", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_player_identities_DiscordId",
                table: "player_identities",
                column: "DiscordId");

            migrationBuilder.CreateIndex(
                name: "IX_player_identities_NakamaId",
                table: "player_identities",
                column: "NakamaId");

            migrationBuilder.CreateIndex(
                name: "IX_player_match_stats_DiscordId",
                table: "player_match_stats",
                column: "DiscordId");

            migrationBuilder.CreateIndex(
                name: "IX_player_match_stats_MatchName",
                table: "player_match_stats",
                column: "MatchName");

            migrationBuilder.CreateIndex(
                name: "IX_player_match_stats_Timestamp",
                table: "player_match_stats",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_watched_players_DiscordId",
                table: "watched_players",
                column: "DiscordId");

            migrationBuilder.CreateIndex(
                name: "IX_watched_players_IpAddress",
                table: "watched_players",
                column: "IpAddress");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "player_identities");

            migrationBuilder.DropTable(
                name: "player_match_stats");

            migrationBuilder.DropTable(
                name: "watched_players");
        }
    }
}
