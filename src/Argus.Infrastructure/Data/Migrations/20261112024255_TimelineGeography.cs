using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Argus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class TimelineGeography : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GeographicFocus",
                columns: table => new
                {
                    InvestigationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Latitude = table.Column<double>(type: "REAL", nullable: false),
                    Longitude = table.Column<double>(type: "REAL", nullable: false),
                    Zoom = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GeographicFocus", x => x.InvestigationId);
                    table.CheckConstraint("CK_Focus_Range", "Latitude BETWEEN -90 AND 90 AND Longitude BETWEEN -180 AND 180 AND Zoom BETWEEN 1 AND 18");
                    table.ForeignKey(
                        name: "FK_GeographicFocus_Investigations_InvestigationId",
                        column: x => x.InvestigationId,
                        principalTable: "Investigations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Locations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    InvestigationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EntityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Precision = table.Column<string>(type: "TEXT", nullable: false),
                    Latitude = table.Column<double>(type: "REAL", nullable: true),
                    Longitude = table.Column<double>(type: "REAL", nullable: true),
                    UncertaintyMeters = table.Column<double>(type: "REAL", nullable: true),
                    Basis = table.Column<string>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Locations", x => x.Id);
                    table.UniqueConstraint("AK_Locations_Id_InvestigationId", x => new { x.Id, x.InvestigationId });
                    table.CheckConstraint("CK_Locations_Basis", "Basis IN ('EXPLICIT_SELECTION','VERIFIED_SOURCE_METADATA')");
                    table.CheckConstraint("CK_Locations_Coordinates", "(Precision = 'UNKNOWN' AND Latitude IS NULL AND Longitude IS NULL AND UncertaintyMeters IS NULL) OR (Precision IN ('EXACT','APPROXIMATE') AND Latitude IS NOT NULL AND Longitude IS NOT NULL AND Latitude BETWEEN -90 AND 90 AND Longitude BETWEEN -180 AND 180 AND ((Precision = 'EXACT' AND (UncertaintyMeters IS NULL OR UncertaintyMeters = 0)) OR (Precision = 'APPROXIMATE' AND UncertaintyMeters IS NOT NULL AND UncertaintyMeters > 0 AND UncertaintyMeters <= 20000000)))");
                    table.CheckConstraint("CK_Locations_Status", "Status IN ('PROPOSED','APPROVED','REJECTED')");
                    table.ForeignKey(
                        name: "FK_Locations_Entities_EntityId_InvestigationId",
                        columns: x => new { x.EntityId, x.InvestigationId },
                        principalTable: "Entities",
                        principalColumns: new[] { "Id", "InvestigationId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Locations_Investigations_InvestigationId",
                        column: x => x.InvestigationId,
                        principalTable: "Investigations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    InvestigationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    StartAt = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    EndAt = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    DatePrecision = table.Column<string>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    LocationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    OriginKey = table.Column<string>(type: "TEXT", nullable: true),
                    OriginalDateText = table.Column<string>(type: "TEXT", nullable: true),
                    Interpretation = table.Column<string>(type: "TEXT", nullable: true),
                    ProcessingJobId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Events", x => x.Id);
                    table.UniqueConstraint("AK_Events_Id_InvestigationId", x => new { x.Id, x.InvestigationId });
                    table.CheckConstraint("CK_Events_Dates", "(DatePrecision = 'UNKNOWN' AND StartAt IS NULL AND EndAt IS NULL) OR (DatePrecision IN ('EXACT','MONTH','YEAR') AND StartAt IS NOT NULL AND EndAt IS NOT NULL AND EndAt >= StartAt)");
                    table.CheckConstraint("CK_Events_Status", "Status IN ('PROPOSED','APPROVED','REJECTED')");
                    table.CheckConstraint("CK_Events_Title", "length(trim(Title)) BETWEEN 1 AND 200");
                    table.CheckConstraint("CK_Events_Type", "Type IN ('OTHER','MEETING','MOVEMENT','PUBLICATION','CUSTOM')");
                    table.ForeignKey(
                        name: "FK_Events_Investigations_InvestigationId",
                        column: x => x.InvestigationId,
                        principalTable: "Investigations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Events_Locations_LocationId_InvestigationId",
                        columns: x => new { x.LocationId, x.InvestigationId },
                        principalTable: "Locations",
                        principalColumns: new[] { "Id", "InvestigationId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Events_ProcessingJobs_ProcessingJobId",
                        column: x => x.ProcessingJobId,
                        principalTable: "ProcessingJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LocationEvidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LocationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SnapshotId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Start = table.Column<int>(type: "INTEGER", nullable: false),
                    End = table.Column<int>(type: "INTEGER", nullable: false),
                    Active = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocationEvidence", x => x.Id);
                    table.CheckConstraint("CK_LocationEvidence_Span", "Start >= 0 AND End > Start AND End - Start <= 8000");
                    table.ForeignKey(
                        name: "FK_LocationEvidence_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LocationEvidence_SourceTextSnapshots_SnapshotId_SourceId",
                        columns: x => new { x.SnapshotId, x.SourceId },
                        principalTable: "SourceTextSnapshots",
                        principalColumns: new[] { "Id", "SourceId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EventEntities",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EntityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    InvestigationId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventEntities", x => new { x.EventId, x.EntityId });
                    table.ForeignKey(
                        name: "FK_EventEntities_Entities_EntityId_InvestigationId",
                        columns: x => new { x.EntityId, x.InvestigationId },
                        principalTable: "Entities",
                        principalColumns: new[] { "Id", "InvestigationId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EventEntities_Events_EventId_InvestigationId",
                        columns: x => new { x.EventId, x.InvestigationId },
                        principalTable: "Events",
                        principalColumns: new[] { "Id", "InvestigationId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EventEvidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SnapshotId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Start = table.Column<int>(type: "INTEGER", nullable: false),
                    End = table.Column<int>(type: "INTEGER", nullable: false),
                    Active = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventEvidence", x => x.Id);
                    table.CheckConstraint("CK_EventEvidence_Span", "Start >= 0 AND End > Start AND End - Start <= 8000");
                    table.ForeignKey(
                        name: "FK_EventEvidence_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EventEvidence_SourceTextSnapshots_SnapshotId_SourceId",
                        columns: x => new { x.SnapshotId, x.SourceId },
                        principalTable: "SourceTextSnapshots",
                        principalColumns: new[] { "Id", "SourceId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EventEntities_EntityId_InvestigationId",
                table: "EventEntities",
                columns: new[] { "EntityId", "InvestigationId" });

            migrationBuilder.CreateIndex(
                name: "IX_EventEntities_EventId_InvestigationId",
                table: "EventEntities",
                columns: new[] { "EventId", "InvestigationId" });

            migrationBuilder.CreateIndex(
                name: "IX_EventEvidence_EventId_SnapshotId_Start_End",
                table: "EventEvidence",
                columns: new[] { "EventId", "SnapshotId", "Start", "End" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EventEvidence_SnapshotId_SourceId",
                table: "EventEvidence",
                columns: new[] { "SnapshotId", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_Events_InvestigationId_Status_StartAt",
                table: "Events",
                columns: new[] { "InvestigationId", "Status", "StartAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Events_LocationId_InvestigationId",
                table: "Events",
                columns: new[] { "LocationId", "InvestigationId" });

            migrationBuilder.CreateIndex(
                name: "IX_Events_OriginKey",
                table: "Events",
                column: "OriginKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Events_ProcessingJobId",
                table: "Events",
                column: "ProcessingJobId");

            migrationBuilder.CreateIndex(
                name: "IX_LocationEvidence_LocationId_SnapshotId_Start_End",
                table: "LocationEvidence",
                columns: new[] { "LocationId", "SnapshotId", "Start", "End" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LocationEvidence_SnapshotId_SourceId",
                table: "LocationEvidence",
                columns: new[] { "SnapshotId", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_Locations_EntityId",
                table: "Locations",
                column: "EntityId",
                unique: true,
                filter: "DeletedAt IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Locations_EntityId_InvestigationId",
                table: "Locations",
                columns: new[] { "EntityId", "InvestigationId" });

            migrationBuilder.CreateIndex(
                name: "IX_Locations_InvestigationId",
                table: "Locations",
                column: "InvestigationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EventEntities");

            migrationBuilder.DropTable(
                name: "EventEvidence");

            migrationBuilder.DropTable(
                name: "GeographicFocus");

            migrationBuilder.DropTable(
                name: "LocationEvidence");

            migrationBuilder.DropTable(
                name: "Events");

            migrationBuilder.DropTable(
                name: "Locations");
        }
    }
}
