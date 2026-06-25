using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orkabi.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddActionItemResolvedFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ResolvedAt",
                table: "ActionItems",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ResolvedByUserId",
                table: "ActionItems",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ActionItems_ResolvedByUserId",
                table: "ActionItems",
                column: "ResolvedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_ActionItems_AspNetUsers_ResolvedByUserId",
                table: "ActionItems",
                column: "ResolvedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ActionItems_AspNetUsers_ResolvedByUserId",
                table: "ActionItems");

            migrationBuilder.DropIndex(
                name: "IX_ActionItems_ResolvedByUserId",
                table: "ActionItems");

            migrationBuilder.DropColumn(
                name: "ResolvedAt",
                table: "ActionItems");

            migrationBuilder.DropColumn(
                name: "ResolvedByUserId",
                table: "ActionItems");
        }
    }
}
