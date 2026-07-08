using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IoTPlatform.Migrations
{
    /// <inheritdoc />
    public partial class AddAnShengIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ProtocolConfigId",
                table: "devices",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ansheng_device_configs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    DeviceId = table.Column<long>(type: "bigint", nullable: false),
                    AppCode = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Imei = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    GetDevStatusSec = table.Column<int>(type: "int", nullable: true),
                    GetDevStatusQ = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OrderUpSec = table.Column<int>(type: "int", nullable: true),
                    Rs485Sec = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ansheng_device_configs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ansheng_device_configs_devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "discovered_ansheng_devices",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    AppCode = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Imei = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Model = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NetType = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DiscoveredAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    IsClaimed = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ClaimedDeviceId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_discovered_ansheng_devices", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_devices_ProtocolConfigId",
                table: "devices",
                column: "ProtocolConfigId");

            migrationBuilder.CreateIndex(
                name: "IX_ansheng_device_configs_AppCode",
                table: "ansheng_device_configs",
                column: "AppCode");

            migrationBuilder.CreateIndex(
                name: "IX_ansheng_device_configs_DeviceId",
                table: "ansheng_device_configs",
                column: "DeviceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ansheng_device_configs_Imei",
                table: "ansheng_device_configs",
                column: "Imei");

            migrationBuilder.CreateIndex(
                name: "IX_discovered_ansheng_devices_AppCode",
                table: "discovered_ansheng_devices",
                column: "AppCode");

            migrationBuilder.CreateIndex(
                name: "IX_discovered_ansheng_devices_Imei",
                table: "discovered_ansheng_devices",
                column: "Imei");

            migrationBuilder.CreateIndex(
                name: "IX_discovered_ansheng_devices_IsClaimed",
                table: "discovered_ansheng_devices",
                column: "IsClaimed");

            migrationBuilder.AddForeignKey(
                name: "FK_devices_protocol_configs_ProtocolConfigId",
                table: "devices",
                column: "ProtocolConfigId",
                principalTable: "protocol_configs",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_devices_protocol_configs_ProtocolConfigId",
                table: "devices");

            migrationBuilder.DropTable(
                name: "ansheng_device_configs");

            migrationBuilder.DropTable(
                name: "discovered_ansheng_devices");

            migrationBuilder.DropIndex(
                name: "IX_devices_ProtocolConfigId",
                table: "devices");

            migrationBuilder.DropColumn(
                name: "ProtocolConfigId",
                table: "devices");
        }
    }
}
