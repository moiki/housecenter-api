using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HouseCenter.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkRouteRecurrence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsRecurrenceIndefinite",
                table: "WorkRoutes",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "RecurrenceDays",
                table: "WorkRoutes",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<DateOnly>(
                name: "RecurrenceEndDate",
                table: "WorkRoutes",
                type: "date",
                nullable: true);

            // 3-step reversible add of a NOT NULL column with a semantically-honest backfill
            // (locked decision: legacy rows' RecurrenceStartDate = their CreatedDate, not a
            // single constant): 1) add nullable, 2) raw-SQL backfill from CreatedDate,
            // 3) tighten to NOT NULL once every row has a value.
            migrationBuilder.AddColumn<DateOnly>(
                name: "RecurrenceStartDate",
                table: "WorkRoutes",
                type: "date",
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE \"WorkRoutes\" SET \"RecurrenceStartDate\" = CAST(\"CreatedDate\" AS date) " +
                "WHERE \"RecurrenceStartDate\" IS NULL;");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "RecurrenceStartDate",
                table: "WorkRoutes",
                type: "date",
                nullable: false,
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsRecurrenceIndefinite",
                table: "WorkRoutes");

            migrationBuilder.DropColumn(
                name: "RecurrenceDays",
                table: "WorkRoutes");

            migrationBuilder.DropColumn(
                name: "RecurrenceEndDate",
                table: "WorkRoutes");

            migrationBuilder.DropColumn(
                name: "RecurrenceStartDate",
                table: "WorkRoutes");
        }
    }
}
