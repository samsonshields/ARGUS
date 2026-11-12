using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Argus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class WorkspaceQuality : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_LocationEvidence_LocationId_Active",
                table: "LocationEvidence",
                columns: new[] { "LocationId", "Active" });

            migrationBuilder.CreateIndex(
                name: "IX_EventEvidence_EventId_Active",
                table: "EventEvidence",
                columns: new[] { "EventId", "Active" });

            migrationBuilder.CreateIndex(
                name: "IX_EntityMentions_EntityId_Status",
                table: "EntityMentions",
                columns: new[] { "EntityId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ClaimEvidence_ClaimId_Active_Relationship",
                table: "ClaimEvidence",
                columns: new[] { "ClaimId", "Active", "Relationship" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LocationEvidence_LocationId_Active",
                table: "LocationEvidence");

            migrationBuilder.DropIndex(
                name: "IX_EventEvidence_EventId_Active",
                table: "EventEvidence");

            migrationBuilder.DropIndex(
                name: "IX_EntityMentions_EntityId_Status",
                table: "EntityMentions");

            migrationBuilder.DropIndex(
                name: "IX_ClaimEvidence_ClaimId_Active_Relationship",
                table: "ClaimEvidence");
        }
    }
}
