using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IoTPlatform.Migrations
{
    /// <inheritdoc />
    public partial class AddAnShengProfileAndDiscoveredColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Iccid",
                table: "discovered_ansheng_devices",
                type: "varchar(50)",
                maxLength: 50,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "Kind",
                table: "discovered_ansheng_devices",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastProbedAt",
                table: "discovered_ansheng_devices",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProbeError",
                table: "discovered_ansheng_devices",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "ProbeStatus",
                table: "discovered_ansheng_devices",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SlotAmount",
                table: "discovered_ansheng_devices",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Version",
                table: "discovered_ansheng_devices",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ansheng_device_profiles",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    AppCode = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Imei = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DeviceId = table.Column<long>(type: "bigint", nullable: true),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    KindSource = table.Column<int>(type: "int", nullable: false),
                    NetType = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SlotAmount = table.Column<int>(type: "int", nullable: true),
                    PhaseAmount = table.Column<int>(type: "int", nullable: true),
                    Version = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Model = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Iccid = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Signal = table.Column<int>(type: "int", nullable: true),
                    ProbeStatus = table.Column<int>(type: "int", nullable: false),
                    ProbeError = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LastProbedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ansheng_device_profiles", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_discovered_ansheng_devices_ProbeStatus",
                table: "discovered_ansheng_devices",
                column: "ProbeStatus");

            migrationBuilder.CreateIndex(
                name: "IX_ansheng_device_profiles_AppCode_Imei",
                table: "ansheng_device_profiles",
                columns: new[] { "AppCode", "Imei" });

            migrationBuilder.CreateIndex(
                name: "IX_ansheng_device_profiles_DeviceId",
                table: "ansheng_device_profiles",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_ansheng_device_profiles_Imei",
                table: "ansheng_device_profiles",
                column: "Imei",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ansheng_device_profiles");

            migrationBuilder.DropIndex(
                name: "IX_discovered_ansheng_devices_ProbeStatus",
                table: "discovered_ansheng_devices");

            migrationBuilder.DropColumn(
                name: "Iccid",
                table: "discovered_ansheng_devices");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "discovered_ansheng_devices");

            migrationBuilder.DropColumn(
                name: "LastProbedAt",
                table: "discovered_ansheng_devices");

            migrationBuilder.DropColumn(
                name: "ProbeError",
                table: "discovered_ansheng_devices");

            migrationBuilder.DropColumn(
                name: "ProbeStatus",
                table: "discovered_ansheng_devices");

            migrationBuilder.DropColumn(
                name: "SlotAmount",
                table: "discovered_ansheng_devices");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "discovered_ansheng_devices");
        }
    }
}
