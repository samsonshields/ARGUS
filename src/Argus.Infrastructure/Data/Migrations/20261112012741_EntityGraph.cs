using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Argus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class EntityGraph : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "ProcessingJobs",
                type: "TEXT",
                nullable: false,
                defaultValue: "ENTITIES");

            migrationBuilder.CreateTable(
                name: "Relationships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    InvestigationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceEntityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TargetEntityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    CustomLabel = table.Column<string>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", nullable: false),
                    Origin = table.Column<string>(type: "TEXT", nullable: false),
                    RetiredReason = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Relationships", x => x.Id);
                    table.CheckConstraint("CK_Relationships_Endpoints", "SourceEntityId <> TargetEntityId OR RetiredReason IS NOT NULL");
                    table.CheckConstraint("CK_Relationships_Status", "Status IN ('PROPOSED','APPROVED','REJECTED')");
                    table.CheckConstraint("CK_Relationships_Type", "Type IN ('MEMBER_OF','ASSOCIATED_WITH','LOCATED_IN','PARTICIPATED_IN','MENTIONED_WITH','CUSTOM')");
                    table.ForeignKey(
                        name: "FK_Relationships_Entities_SourceEntityId_InvestigationId",
                        columns: x => new { x.SourceEntityId, x.InvestigationId },
                        principalTable: "Entities",
                        principalColumns: new[] { "Id", "InvestigationId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Relationships_Entities_TargetEntityId_InvestigationId",
                        columns: x => new { x.TargetEntityId, x.InvestigationId },
                        principalTable: "Entities",
                        principalColumns: new[] { "Id", "InvestigationId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Relationships_Investigations_InvestigationId",
                        column: x => x.InvestigationId,
                        principalTable: "Investigations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RelationshipEvidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RelationshipId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SnapshotId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceMentionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TargetMentionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Start = table.Column<int>(type: "INTEGER", nullable: false),
                    End = table.Column<int>(type: "INTEGER", nullable: false),
                    OriginKey = table.Column<string>(type: "TEXT", nullable: false),
                    Method = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RelationshipEvidence", x => x.Id);
                    table.CheckConstraint("CK_RelationshipEvidence_Span", "Start >= 0 AND End > Start AND End - Start <= 8000");
                    table.CheckConstraint("CK_RelationshipEvidence_Status", "Status IN ('PROPOSED','APPROVED','REJECTED')");
                    table.ForeignKey(
                        name: "FK_RelationshipEvidence_EntityMentions_SourceMentionId",
                        column: x => x.SourceMentionId,
                        principalTable: "EntityMentions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RelationshipEvidence_EntityMentions_TargetMentionId",
                        column: x => x.TargetMentionId,
                        principalTable: "EntityMentions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RelationshipEvidence_Relationships_RelationshipId",
                        column: x => x.RelationshipId,
                        principalTable: "Relationships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RelationshipEvidence_SourceTextSnapshots_SnapshotId_SourceId",
                        columns: x => new { x.SnapshotId, x.SourceId },
                        principalTable: "SourceTextSnapshots",
                        principalColumns: new[] { "Id", "SourceId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RelationshipEvidence_Sources_SourceId",
                        column: x => x.SourceId,
                        principalTable: "Sources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RelationshipEvidence_OriginKey",
                table: "RelationshipEvidence",
                column: "OriginKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RelationshipEvidence_RelationshipId",
                table: "RelationshipEvidence",
                column: "RelationshipId");

            migrationBuilder.CreateIndex(
                name: "IX_RelationshipEvidence_SnapshotId_SourceId",
                table: "RelationshipEvidence",
                columns: new[] { "SnapshotId", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_RelationshipEvidence_SourceId",
                table: "RelationshipEvidence",
                column: "SourceId");

            migrationBuilder.CreateIndex(
                name: "IX_RelationshipEvidence_SourceMentionId",
                table: "RelationshipEvidence",
                column: "SourceMentionId");

            migrationBuilder.CreateIndex(
                name: "IX_RelationshipEvidence_TargetMentionId",
                table: "RelationshipEvidence",
                column: "TargetMentionId");

            migrationBuilder.CreateIndex(
                name: "IX_Relationships_InvestigationId_Status",
                table: "Relationships",
                columns: new[] { "InvestigationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Relationships_SourceEntityId",
                table: "Relationships",
                column: "SourceEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_Relationships_SourceEntityId_InvestigationId",
                table: "Relationships",
                columns: new[] { "SourceEntityId", "InvestigationId" });

            migrationBuilder.CreateIndex(
                name: "IX_Relationships_TargetEntityId",
                table: "Relationships",
                column: "TargetEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_Relationships_TargetEntityId_InvestigationId",
                table: "Relationships",
                columns: new[] { "TargetEntityId", "InvestigationId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RelationshipEvidence");

            migrationBuilder.DropTable(
                name: "Relationships");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "ProcessingJobs");
        }
    }
}
