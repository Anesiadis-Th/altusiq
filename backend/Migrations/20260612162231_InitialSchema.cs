using System;
using System.Collections.Generic;
using AltusIQ.Api.Models;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace AltusIQ.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:postgis", ",,");

            migrationBuilder.CreateTable(
                name: "Flights",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Icao24 = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    Callsign = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    OriginCountry = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    OpenedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastPosition = table.Column<Point>(type: "geometry(Point, 4326)", nullable: true),
                    LastAltitude = table.Column<double>(type: "double precision", nullable: true),
                    TrackPoints = table.Column<List<TrackPoint>>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flights", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Flights_ClosedAt",
                table: "Flights",
                column: "ClosedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Flights_Icao24",
                table: "Flights",
                column: "Icao24");

            migrationBuilder.CreateIndex(
                name: "IX_Flights_LastPosition",
                table: "Flights",
                column: "LastPosition")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "IX_Flights_OpenedAt",
                table: "Flights",
                column: "OpenedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Flights");
        }
    }
}
