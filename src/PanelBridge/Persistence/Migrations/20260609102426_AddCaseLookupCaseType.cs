using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelBridge.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCaseLookupCaseType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CaseType",
                table: "CaseLookups",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Backfill existing rows from the panel ref suffix.
            //   1 = Sale (S, FS)
            //   2 = Purchase (P, FP)
            //   3 = Remortgage (R, FR)
            //   0 = Unknown (anything else)
            migrationBuilder.Sql(@"
                UPDATE [CaseLookups]
                SET [CaseType] = CASE
                    WHEN RIGHT([PanelRef], 3) IN ('-FS') THEN 1
                    WHEN RIGHT([PanelRef], 3) IN ('-FP') THEN 2
                    WHEN RIGHT([PanelRef], 3) IN ('-FR') THEN 3
                    WHEN RIGHT([PanelRef], 2) = '-S' THEN 1
                    WHEN RIGHT([PanelRef], 2) = '-P' THEN 2
                    WHEN RIGHT([PanelRef], 2) = '-R' THEN 3
                    ELSE 0
                END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CaseType",
                table: "CaseLookups");
        }
    }
}
