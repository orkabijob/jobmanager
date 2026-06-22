using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orkabi.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddProbeStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "Probes",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Status",
                table: "Probes");
        }
    }
}
