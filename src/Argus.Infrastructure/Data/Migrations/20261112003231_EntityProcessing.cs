using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Argus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class EntityProcessing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Sources_Status",
                table: "Sources");

            migrationBuilder.CreateTable(
                name: "Entities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    InvestigationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", nullable: false),
                    AliasesJson = table.Column<string>(type: "TEXT", nullable: false),
                    MergedIntoId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Entities", x => x.Id);
                    table.UniqueConstraint("AK_Entities_Id_InvestigationId", x => new { x.Id, x.InvestigationId });
                    table.ForeignKey(
                        name: "FK_Entities_Entities_MergedIntoId_InvestigationId",
                        columns: x => new { x.MergedIntoId, x.InvestigationId },
                        principalTable: "Entities",
                        principalColumns: new[] { "Id", "InvestigationId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Entities_Investigations_InvestigationId",
                        column: x => x.InvestigationId,
                        principalTable: "Investigations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SourceTextSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Text = table.Column<string>(type: "TEXT", nullable: false),
                    TextHash = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceTextSnapshots", x => x.Id);
                    table.UniqueConstraint("AK_SourceTextSnapshots_Id_SourceId", x => new { x.Id, x.SourceId });
                    table.ForeignKey(
                        name: "FK_SourceTextSnapshots_Sources_SourceId",
                        column: x => x.SourceId,
                        principalTable: "Sources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProcessingJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    InvestigationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    State = table.Column<string>(type: "TEXT", nullable: false),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    ErrorCode = table.Column<string>(type: "TEXT", nullable: true),
                    Error = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SnapshotId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CandidateCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessingJobs", x => x.Id);
                    table.CheckConstraint("CK_Jobs_State", "State IN ('PENDING','RUNNING','COMPLETED','FAILED','CANCELLED')");
                    table.ForeignKey(
                        name: "FK_ProcessingJobs_Investigations_InvestigationId",
                        column: x => x.InvestigationId,
                        principalTable: "Investigations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProcessingJobs_SourceTextSnapshots_SnapshotId",
                        column: x => x.SnapshotId,
                        principalTable: "SourceTextSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProcessingJobs_Sources_SourceId",
                        column: x => x.SourceId,
                        principalTable: "Sources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EntityMentions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    InvestigationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SnapshotId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProcessingJobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Text = table.Column<string>(type: "TEXT", nullable: false),
                    OriginalType = table.Column<string>(type: "TEXT", nullable: false),
                    Start = table.Column<int>(type: "INTEGER", nullable: false),
                    End = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    EntityId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ModelName = table.Column<string>(type: "TEXT", nullable: false),
                    ModelVersion = table.Column<string>(type: "TEXT", nullable: false),
                    ReviewedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EntityMentions", x => x.Id);
                    table.CheckConstraint("CK_Mentions_Review", "(Status = 'APPROVED' AND EntityId IS NOT NULL) OR (Status IN ('PROPOSED','REJECTED') AND EntityId IS NULL)");
                    table.CheckConstraint("CK_Mentions_Span", "Start >= 0 AND End > Start");
                    table.ForeignKey(
                        name: "FK_EntityMentions_Entities_EntityId_InvestigationId",
                        columns: x => new { x.EntityId, x.InvestigationId },
                        principalTable: "Entities",
                        principalColumns: new[] { "Id", "InvestigationId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EntityMentions_ProcessingJobs_ProcessingJobId",
                        column: x => x.ProcessingJobId,
                        principalTable: "ProcessingJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EntityMentions_SourceTextSnapshots_SnapshotId_SourceId",
                        columns: x => new { x.SnapshotId, x.SourceId },
                        principalTable: "SourceTextSnapshots",
                        principalColumns: new[] { "Id", "SourceId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EntityMentions_Sources_SourceId",
                        column: x => x.SourceId,
                        principalTable: "Sources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Sources_Status",
                table: "Sources",
                sql: "ProcessingStatus IN ('Ready','Failed','Pending','Processing')");

            migrationBuilder.CreateIndex(
                name: "IX_Entities_InvestigationId_NormalizedName",
                table: "Entities",
                columns: new[] { "InvestigationId", "NormalizedName" });

            migrationBuilder.CreateIndex(
                name: "IX_Entities_MergedIntoId_InvestigationId",
                table: "Entities",
                columns: new[] { "MergedIntoId", "InvestigationId" });

            migrationBuilder.CreateIndex(
                name: "IX_EntityMentions_EntityId_InvestigationId",
                table: "EntityMentions",
                columns: new[] { "EntityId", "InvestigationId" });

            migrationBuilder.CreateIndex(
                name: "IX_EntityMentions_InvestigationId_Status",
                table: "EntityMentions",
                columns: new[] { "InvestigationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_EntityMentions_ProcessingJobId",
                table: "EntityMentions",
                column: "ProcessingJobId");

            migrationBuilder.CreateIndex(
                name: "IX_EntityMentions_SnapshotId_SourceId",
                table: "EntityMentions",
                columns: new[] { "SnapshotId", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_EntityMentions_SnapshotId_Start_End_OriginalType",
                table: "EntityMentions",
                columns: new[] { "SnapshotId", "Start", "End", "OriginalType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EntityMentions_SourceId",
                table: "EntityMentions",
                column: "SourceId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessingJobs_InvestigationId",
                table: "ProcessingJobs",
                column: "InvestigationId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessingJobs_SnapshotId",
                table: "ProcessingJobs",
                column: "SnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessingJobs_SourceId",
                table: "ProcessingJobs",
                column: "SourceId",
                unique: true,
                filter: "State IN ('PENDING','RUNNING')");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessingJobs_State_NextAttemptAt",
                table: "ProcessingJobs",
                columns: new[] { "State", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SourceTextSnapshots_SourceId_TextHash",
                table: "SourceTextSnapshots",
                columns: new[] { "SourceId", "TextHash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EntityMentions");

            migrationBuilder.DropTable(
                name: "Entities");

            migrationBuilder.DropTable(
                name: "ProcessingJobs");

            migrationBuilder.DropTable(
                name: "SourceTextSnapshots");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Sources_Status",
                table: "Sources");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Sources_Status",
                table: "Sources",
                sql: "ProcessingStatus IN ('Ready','Failed')");
        }
    }
}
