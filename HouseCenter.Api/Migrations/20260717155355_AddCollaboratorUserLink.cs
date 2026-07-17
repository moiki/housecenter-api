using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HouseCenter.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCollaboratorUserLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "Collaborators",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Collaborators_UserId",
                table: "Collaborators",
                column: "UserId",
                unique: true,
                filter: "\"UserId\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_Collaborators_Users_UserId",
                table: "Collaborators",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Collaborators_Users_UserId",
                table: "Collaborators");

            migrationBuilder.DropIndex(
                name: "IX_Collaborators_UserId",
                table: "Collaborators");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "Collaborators");
        }
    }
}
