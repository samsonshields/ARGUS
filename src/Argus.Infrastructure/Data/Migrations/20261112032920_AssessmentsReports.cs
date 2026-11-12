using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Argus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AssessmentsReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Claims",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    InvestigationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Statement = table.Column<string>(type: "TEXT", nullable: false),
                    ConfidenceLevel = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Reasoning = table.Column<string>(type: "TEXT", nullable: false),
                    ContradictionsResolved = table.Column<bool>(type: "INTEGER", nullable: false),
                    ContradictionResolution = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Claims", x => x.Id);
                    table.CheckConstraint("CK_Claims_Confidence", "ConfidenceLevel IN ('LOW','MODERATE','HIGH','INSUFFICIENT_INFORMATION')");
                    table.CheckConstraint("CK_Claims_Resolution", "(ContradictionsResolved = 0 AND ContradictionResolution = '') OR (ContradictionsResolved = 1 AND length(trim(ContradictionResolution)) BETWEEN 1 AND 5000)");
                    table.CheckConstraint("CK_Claims_Statement", "length(trim(Statement)) BETWEEN 1 AND 2000");
                    table.CheckConstraint("CK_Claims_Status", "Status IN ('PROPOSED','APPROVED','REJECTED')");
                    table.ForeignKey(
                        name: "FK_Claims_Investigations_InvestigationId",
                        column: x => x.InvestigationId,
                        principalTable: "Investigations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GeneratedReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    InvestigationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GeneratedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Markdown = table.Column<string>(type: "TEXT", nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", nullable: false),
                    ValidationJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GeneratedReports", x => x.Id);
                    table.CheckConstraint("CK_Reports_Hash", "length(ContentHash) = 64");
                    table.ForeignKey(
                        name: "FK_GeneratedReports_Investigations_InvestigationId",
                        column: x => x.InvestigationId,
                        principalTable: "Investigations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClaimEvidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClaimId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SnapshotId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Start = table.Column<int>(type: "INTEGER", nullable: false),
                    End = table.Column<int>(type: "INTEGER", nullable: false),
                    Relationship = table.Column<string>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: false),
                    Active = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClaimEvidence", x => x.Id);
                    table.CheckConstraint("CK_ClaimEvidence_Relationship", "Relationship IN ('SUPPORTS','CONTRADICTS','CONTEXT')");
                    table.CheckConstraint("CK_ClaimEvidence_Span", "Start >= 0 AND End > Start AND End - Start <= 8000");
                    table.ForeignKey(
                        name: "FK_ClaimEvidence_Claims_ClaimId",
                        column: x => x.ClaimId,
                        principalTable: "Claims",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClaimEvidence_SourceTextSnapshots_SnapshotId_SourceId",
                        columns: x => new { x.SnapshotId, x.SourceId },
                        principalTable: "SourceTextSnapshots",
                        principalColumns: new[] { "Id", "SourceId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClaimEvidence_ClaimId_SnapshotId_Start_End",
                table: "ClaimEvidence",
                columns: new[] { "ClaimId", "SnapshotId", "Start", "End" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClaimEvidence_SnapshotId_SourceId",
                table: "ClaimEvidence",
                columns: new[] { "SnapshotId", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_Claims_InvestigationId_Status_UpdatedAt",
                table: "Claims",
                columns: new[] { "InvestigationId", "Status", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_GeneratedReports_InvestigationId_GeneratedAt",
                table: "GeneratedReports",
                columns: new[] { "InvestigationId", "GeneratedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClaimEvidence");

            migrationBuilder.DropTable(
                name: "GeneratedReports");

            migrationBuilder.DropTable(
                name: "Claims");
        }
    }
}
