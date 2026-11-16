using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AltusIQ.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAnomalyEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AnomalyEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Icao24 = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    Callsign = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    OriginCountry = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Squawk = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Sensitive = table.Column<bool>(type: "boolean", nullable: false),
                    FirstSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConfirmedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    FirstLongitude = table.Column<double>(type: "double precision", nullable: true),
                    FirstLatitude = table.Column<double>(type: "double precision", nullable: true),
                    FirstAltitude = table.Column<double>(type: "double precision", nullable: true),
                    LastLongitude = table.Column<double>(type: "double precision", nullable: true),
                    LastLatitude = table.Column<double>(type: "double precision", nullable: true),
                    LastAltitude = table.Column<double>(type: "double precision", nullable: true),
                    EvidenceJson = table.Column<string>(type: "jsonb", nullable: true),
                    InvestigationJson = table.Column<string>(type: "jsonb", nullable: true),
                    InvestigationStatus = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Grounded = table.Column<bool>(type: "boolean", nullable: false),
                    PromptVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Model = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: true),
                    InputTokens = table.Column<int>(type: "integer", nullable: true),
                    OutputTokens = table.Column<int>(type: "integer", nullable: true),
                    LatencyMs = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnomalyEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnomalyEvents_ConfirmedAt",
                table: "AnomalyEvents",
                column: "ConfirmedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AnomalyEvents_Icao24",
                table: "AnomalyEvents",
                column: "Icao24");

            migrationBuilder.CreateIndex(
                name: "IX_AnomalyEvents_Status",
                table: "AnomalyEvents",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnomalyEvents");
        }
    }
}
