using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AltusIQ.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddInvestigationTrace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InvestigationTraceJson",
                table: "AnomalyEvents",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ModelCalls",
                table: "AnomalyEvents",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InvestigationTraceJson",
                table: "AnomalyEvents");

            migrationBuilder.DropColumn(
                name: "ModelCalls",
                table: "AnomalyEvents");
        }
    }
}
