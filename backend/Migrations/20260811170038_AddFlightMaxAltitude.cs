using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AltusIQ.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddFlightMaxAltitude : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "MaxAltitude",
                table: "Flights",
                type: "double precision",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaxAltitude",
                table: "Flights");
        }
    }
}
