using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orkabi.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddVacationReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Reason",
                table: "VacationRequests",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Reason",
                table: "VacationRequests");
        }
    }
}
