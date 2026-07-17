using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HouseCenter.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCollaboratorEmailUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Collaborators_Email",
                table: "Collaborators",
                column: "Email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Collaborators_Email",
                table: "Collaborators");
        }
    }
}
