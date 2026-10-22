using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tracker.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddJobBankRegions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "jobbank_regions",
                columns: table => new
                {
                    month = table.Column<DateOnly>(type: "date", nullable: false),
                    economic_region = table.Column<string>(type: "text", nullable: false),
                    postings = table.Column<int>(type: "integer", nullable: false),
                    vacancies = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_jobbank_regions", x => new { x.month, x.economic_region });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "jobbank_regions");
        }
    }
}
