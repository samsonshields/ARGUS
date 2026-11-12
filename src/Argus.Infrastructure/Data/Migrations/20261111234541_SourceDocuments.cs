using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Argus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class SourceDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    InvestigationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RecordType = table.Column<string>(type: "TEXT", nullable: false),
                    RecordId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Action = table.Column<string>(type: "TEXT", nullable: false),
                    BeforeJson = table.Column<string>(type: "TEXT", nullable: true),
                    AfterJson = table.Column<string>(type: "TEXT", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Actor = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditEvents_Investigations_InvestigationId",
                        column: x => x.InvestigationId,
                        principalTable: "Investigations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Sources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    InvestigationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    SourceType = table.Column<string>(type: "TEXT", nullable: false),
                    OriginalUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    Publisher = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    PublishedAt = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    RetrievedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Reliability = table.Column<string>(type: "TEXT", nullable: false),
                    FilePath = table.Column<string>(type: "TEXT", nullable: false),
                    OriginalFileName = table.Column<string>(type: "TEXT", nullable: false),
                    FileSize = table.Column<long>(type: "INTEGER", nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ExtractedText = table.Column<string>(type: "TEXT", nullable: false),
                    ProcessingStatus = table.Column<string>(type: "TEXT", nullable: false),
                    ProcessingError = table.Column<string>(type: "TEXT", nullable: true),
                    ExtractedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ExtractionVersion = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sources", x => x.Id);
                    table.CheckConstraint("CK_Sources_Hash", "length(ContentHash) = 64");
                    table.CheckConstraint("CK_Sources_Reliability", "Reliability IN ('A','B','C','D','E')");
                    table.CheckConstraint("CK_Sources_Size", "FileSize > 0");
                    table.CheckConstraint("CK_Sources_Status", "ProcessingStatus IN ('Ready','Failed')");
                    table.CheckConstraint("CK_Sources_Title", "length(trim(Title)) BETWEEN 1 AND 200");
                    table.CheckConstraint("CK_Sources_Type", "SourceType IN ('Text','Pdf','PastedText')");
                    table.ForeignKey(
                        name: "FK_Sources_Investigations_InvestigationId",
                        column: x => x.InvestigationId,
                        principalTable: "Investigations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_InvestigationId_OccurredAt",
                table: "AuditEvents",
                columns: new[] { "InvestigationId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Sources_ContentHash",
                table: "Sources",
                column: "ContentHash");

            migrationBuilder.CreateIndex(
                name: "IX_Sources_InvestigationId_ContentHash",
                table: "Sources",
                columns: new[] { "InvestigationId", "ContentHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sources_ProcessingStatus",
                table: "Sources",
                column: "ProcessingStatus");

            migrationBuilder.CreateIndex(
                name: "IX_Sources_PublishedAt",
                table: "Sources",
                column: "PublishedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditEvents");

            migrationBuilder.DropTable(
                name: "Sources");
        }
    }
}
