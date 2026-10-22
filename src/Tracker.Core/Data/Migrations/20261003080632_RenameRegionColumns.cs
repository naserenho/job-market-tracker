using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tracker.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenameRegionColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A rename keeps the values: --report-only right after the upgrade still reads every posting's scope.
            migrationBuilder.RenameColumn(
                name: "is_remote_canada",
                table: "postings",
                newName: "is_remote_in_country");

            migrationBuilder.RenameColumn(
                name: "is_gta",
                table: "postings",
                newName: "is_local");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "is_remote_in_country",
                table: "postings",
                newName: "is_remote_canada");

            migrationBuilder.RenameColumn(
                name: "is_local",
                table: "postings",
                newName: "is_gta");
        }
    }
}
