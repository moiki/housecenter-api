using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HouseCenter.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAttentionSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AttentionSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    CollaboratorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: true),
                    WorkRouteId = table.Column<Guid>(type: "uuid", nullable: true),
                    AttentionType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    SessionDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DurationMinutes = table.Column<int>(type: "integer", nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttentionSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttentionSessions_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AttentionSessions_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AttentionSessions_Users_CollaboratorId",
                        column: x => x.CollaboratorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttentionSessions_WorkRoutes_WorkRouteId",
                        column: x => x.WorkRouteId,
                        principalTable: "WorkRoutes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttentionSessions_ClinicId",
                table: "AttentionSessions",
                column: "ClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_AttentionSessions_CollaboratorId",
                table: "AttentionSessions",
                column: "CollaboratorId");

            migrationBuilder.CreateIndex(
                name: "IX_AttentionSessions_PatientId",
                table: "AttentionSessions",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_AttentionSessions_WorkRouteId",
                table: "AttentionSessions",
                column: "WorkRouteId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AttentionSessions");
        }
    }
}
