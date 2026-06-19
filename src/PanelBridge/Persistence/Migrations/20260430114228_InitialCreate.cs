using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace PanelBridge.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CaseHandlerPanels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CaseHandlerId = table.Column<int>(type: "int", nullable: false),
                    PanelId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CaseHandlerPanels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CaseHandlers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FirstName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Telephone = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CaseHandlers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CaseLookups",
                columns: table => new
                {
                    UniversalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PanelRef = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PanelId = table.Column<int>(type: "int", nullable: false),
                    InternalRef = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RegionId = table.Column<int>(type: "int", nullable: true),
                    CaseTypeId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    AssignedCaseHandlerId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CaseLookups", x => x.UniversalId);
                });

            migrationBuilder.CreateTable(
                name: "CaseTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CaseTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Lenders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SortReferId = table.Column<int>(type: "int", nullable: true),
                    EconId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Lenders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Milestones",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PanelId = table.Column<int>(type: "int", nullable: false),
                    CaseTypeId = table.Column<int>(type: "int", nullable: false),
                    RegionId = table.Column<int>(type: "int", nullable: true),
                    PanelMilestoneCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Milestones", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Panels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Panels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Regions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Regions", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "Panels",
                columns: new[] { "Id", "Name" },
                values: new object[,]
                {
                    { 1, "sortrefer" },
                    { 2, "econ" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_CaseHandlerPanels_CaseHandlerId_PanelId",
                table: "CaseHandlerPanels",
                columns: new[] { "CaseHandlerId", "PanelId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CaseHandlers_Email",
                table: "CaseHandlers",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CaseLookups_InternalRef",
                table: "CaseLookups",
                column: "InternalRef");

            migrationBuilder.CreateIndex(
                name: "IX_CaseLookups_PanelId_PanelRef",
                table: "CaseLookups",
                columns: new[] { "PanelId", "PanelRef" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CaseTypes_Name",
                table: "CaseTypes",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Lenders_EconId",
                table: "Lenders",
                column: "EconId",
                unique: true,
                filter: "[EconId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Lenders_SortReferId",
                table: "Lenders",
                column: "SortReferId",
                unique: true,
                filter: "[SortReferId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Milestones_PanelId_CaseTypeId_RegionId_PanelMilestoneCode",
                table: "Milestones",
                columns: new[] { "PanelId", "CaseTypeId", "RegionId", "PanelMilestoneCode" },
                unique: true,
                filter: "[RegionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Panels_Name",
                table: "Panels",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Regions_Name",
                table: "Regions",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CaseHandlerPanels");

            migrationBuilder.DropTable(
                name: "CaseHandlers");

            migrationBuilder.DropTable(
                name: "CaseLookups");

            migrationBuilder.DropTable(
                name: "CaseTypes");

            migrationBuilder.DropTable(
                name: "Lenders");

            migrationBuilder.DropTable(
                name: "Milestones");

            migrationBuilder.DropTable(
                name: "Panels");

            migrationBuilder.DropTable(
                name: "Regions");
        }
    }
}
