using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HouseCenter.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPatientRouteVisitTimeRemoveDestinations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Destinations",
                table: "WorkRoutes");

            migrationBuilder.AddColumn<TimeOnly>(
                name: "RouteVisitTime",
                table: "Patients",
                type: "time without time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RouteVisitTime",
                table: "Patients");

            migrationBuilder.AddColumn<string>(
                name: "Destinations",
                table: "WorkRoutes",
                type: "jsonb",
                nullable: true);
        }
    }
}
