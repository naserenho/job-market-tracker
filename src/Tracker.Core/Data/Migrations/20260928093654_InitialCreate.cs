using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "adzuna_aggregates",
                columns: table => new
                {
                    run_id = table.Column<string>(type: "text", nullable: false),
                    term = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_adzuna_aggregates", x => new { x.run_id, x.term, x.kind });
                });

            migrationBuilder.CreateTable(
                name: "jobbank_monthly",
                columns: table => new
                {
                    month = table.Column<DateOnly>(type: "date", nullable: false),
                    noc_code = table.Column<string>(type: "text", nullable: false),
                    noc_name = table.Column<string>(type: "text", nullable: false),
                    postings = table.Column<int>(type: "integer", nullable: false),
                    vacancies = table.Column<int>(type: "integer", nullable: false),
                    wage_min_median = table.Column<decimal>(type: "numeric", nullable: true),
                    wage_max_median = table.Column<decimal>(type: "numeric", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_jobbank_monthly", x => new { x.month, x.noc_code });
                });

            migrationBuilder.CreateTable(
                name: "postings",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false),
                    source_id = table.Column<string>(type: "text", nullable: false),
                    company = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    url = table.Column<string>(type: "text", nullable: false),
                    location_text = table.Column<string>(type: "text", nullable: false),
                    city = table.Column<string>(type: "text", nullable: true),
                    is_gta = table.Column<bool>(type: "boolean", nullable: false),
                    is_remote_canada = table.Column<bool>(type: "boolean", nullable: false),
                    in_scope = table.Column<bool>(type: "boolean", nullable: false),
                    role_family = table.Column<string>(type: "text", nullable: false),
                    seniority = table.Column<string>(type: "text", nullable: false),
                    salary_min = table.Column<decimal>(type: "numeric", nullable: true),
                    salary_max = table.Column<decimal>(type: "numeric", nullable: true),
                    salary_period = table.Column<string>(type: "text", nullable: true),
                    salary_origin = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    is_full_text = table.Column<bool>(type: "boolean", nullable: false),
                    citizenship_or_clearance = table.Column<bool>(type: "boolean", nullable: false),
                    first_seen_run = table.Column<string>(type: "text", nullable: false),
                    last_seen_run = table.Column<string>(type: "text", nullable: false),
                    duplicate_of = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_postings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "runs",
                columns: table => new
                {
                    run_id = table.Column<string>(type: "text", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_runs", x => x.run_id);
                });

            migrationBuilder.CreateTable(
                name: "skills",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    category = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_skills", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "posting_sightings",
                columns: table => new
                {
                    posting_id = table.Column<string>(type: "text", nullable: false),
                    run_id = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_posting_sightings", x => new { x.posting_id, x.run_id });
                    table.ForeignKey(
                        name: "fk_posting_sightings_postings_posting_id",
                        column: x => x.posting_id,
                        principalTable: "postings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_posting_sightings_runs_run_id",
                        column: x => x.run_id,
                        principalTable: "runs",
                        principalColumn: "run_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "posting_skills",
                columns: table => new
                {
                    posting_id = table.Column<string>(type: "text", nullable: false),
                    skill_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_posting_skills", x => new { x.posting_id, x.skill_id });
                    table.ForeignKey(
                        name: "fk_posting_skills_postings_posting_id",
                        column: x => x.posting_id,
                        principalTable: "postings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_posting_skills_skills_skill_id",
                        column: x => x.skill_id,
                        principalTable: "skills",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_posting_sightings_run_id",
                table: "posting_sightings",
                column: "run_id");

            migrationBuilder.CreateIndex(
                name: "ix_posting_skills_skill_id",
                table: "posting_skills",
                column: "skill_id");

            migrationBuilder.CreateIndex(
                name: "ix_postings_in_scope_duplicate_of",
                table: "postings",
                columns: new[] { "in_scope", "duplicate_of" });

            migrationBuilder.CreateIndex(
                name: "ix_skills_name",
                table: "skills",
                column: "name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "adzuna_aggregates");

            migrationBuilder.DropTable(
                name: "jobbank_monthly");

            migrationBuilder.DropTable(
                name: "posting_sightings");

            migrationBuilder.DropTable(
                name: "posting_skills");

            migrationBuilder.DropTable(
                name: "runs");

            migrationBuilder.DropTable(
                name: "postings");

            migrationBuilder.DropTable(
                name: "skills");
        }
    }
}
