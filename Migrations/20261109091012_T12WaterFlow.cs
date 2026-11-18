using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IoTPlatform.Migrations
{
    /// <inheritdoc />
    public partial class T12WaterFlow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "WaterPulses",
                table: "device_data_records",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ansheng_wf_config",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    AppCode = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DeviceId = table.Column<long>(type: "bigint", nullable: false),
                    K = table.Column<double>(type: "double", nullable: false),
                    Offset = table.Column<double>(type: "double", nullable: false),
                    MinFlow = table.Column<double>(type: "double", nullable: false),
                    MaxFlow = table.Column<double>(type: "double", nullable: false),
                    SyncedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ansheng_wf_config", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ansheng_wf_config_DeviceId",
                table: "ansheng_wf_config",
                columns: new[] { "DeviceId", "AppCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ansheng_wf_config_DeviceId1",
                table: "ansheng_wf_config",
                column: "DeviceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ansheng_wf_config");

            migrationBuilder.DropColumn(
                name: "WaterPulses",
                table: "device_data_records");
        }
    }
}
