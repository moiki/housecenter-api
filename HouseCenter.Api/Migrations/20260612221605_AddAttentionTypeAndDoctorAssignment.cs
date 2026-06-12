using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HouseCenter.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAttentionTypeAndDoctorAssignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "Treatments",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Medical");

            migrationBuilder.AddColumn<string>(
                name: "PrimaryAttentionType",
                table: "Patients",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Medical");

            migrationBuilder.CreateTable(
                name: "patient_doctors",
                columns: table => new
                {
                    DoctorId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_patient_doctors", x => new { x.DoctorId, x.PatientId });
                    table.ForeignKey(
                        name: "FK_patient_doctors_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_patient_doctors_Users_DoctorId",
                        column: x => x.DoctorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_patient_doctors_PatientId",
                table: "patient_doctors",
                column: "PatientId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "patient_doctors");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "Treatments");

            migrationBuilder.DropColumn(
                name: "PrimaryAttentionType",
                table: "Patients");
        }
    }
}
