using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Argus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialInvestigations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Investigations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 5000, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    EndDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Region = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Investigations", x => x.Id);
                    table.CheckConstraint("CK_Investigations_Dates", "EndDate IS NULL OR StartDate IS NULL OR EndDate >= StartDate");
                    table.CheckConstraint("CK_Investigations_Status", "Status IN ('Active', 'Archived')");
                    table.CheckConstraint("CK_Investigations_Title", "length(trim(Title)) BETWEEN 1 AND 200");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Investigations_Status",
                table: "Investigations",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Investigations_UpdatedAt",
                table: "Investigations",
                column: "UpdatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Investigations");
        }
    }
}
