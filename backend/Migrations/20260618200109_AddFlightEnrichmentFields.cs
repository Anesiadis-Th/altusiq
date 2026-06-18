using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AltusIQ.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddFlightEnrichmentFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ArrivalAirport",
                table: "Flights",
                type: "character varying(4)",
                maxLength: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DepartureAirport",
                table: "Flights",
                type: "character varying(4)",
                maxLength: 4,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EnrichedAt",
                table: "Flights",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EnrichmentAttempts",
                table: "Flights",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Flights_EnrichedAt",
                table: "Flights",
                column: "EnrichedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Flights_EnrichedAt",
                table: "Flights");

            migrationBuilder.DropColumn(
                name: "ArrivalAirport",
                table: "Flights");

            migrationBuilder.DropColumn(
                name: "DepartureAirport",
                table: "Flights");

            migrationBuilder.DropColumn(
                name: "EnrichedAt",
                table: "Flights");

            migrationBuilder.DropColumn(
                name: "EnrichmentAttempts",
                table: "Flights");
        }
    }
}
