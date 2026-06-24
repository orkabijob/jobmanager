using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orkabi.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddClassSyllabusId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SyllabusId",
                table: "Classes",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Classes_SyllabusId",
                table: "Classes",
                column: "SyllabusId");

            migrationBuilder.AddForeignKey(
                name: "FK_Classes_Syllabi_SyllabusId",
                table: "Classes",
                column: "SyllabusId",
                principalTable: "Syllabi",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Classes_Syllabi_SyllabusId",
                table: "Classes");

            migrationBuilder.DropIndex(
                name: "IX_Classes_SyllabusId",
                table: "Classes");

            migrationBuilder.DropColumn(
                name: "SyllabusId",
                table: "Classes");
        }
    }
}
