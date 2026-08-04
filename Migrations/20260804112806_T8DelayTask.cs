using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IoTPlatform.Migrations
{
    /// <inheritdoc />
    public partial class T8DelayTask : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SlotsSnapshot",
                table: "ansheng_device_profiles",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "SlotsSnapshotAt",
                table: "ansheng_device_profiles",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ansheng_delay_tasks",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    AppCode = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DeviceId = table.Column<long>(type: "bigint", nullable: false),
                    SlotNum = table.Column<int>(type: "int", nullable: false),
                    Enable = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SAction = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EAction = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Secs = table.Column<int>(type: "int", nullable: false),
                    Cnt = table.Column<int>(type: "int", nullable: false),
                    SyncedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ansheng_delay_tasks", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ansheng_delay_tasks_AppCode_DeviceId",
                table: "ansheng_delay_tasks",
                columns: new[] { "AppCode", "DeviceId" });

            migrationBuilder.CreateIndex(
                name: "IX_ansheng_delay_tasks_DeviceId",
                table: "ansheng_delay_tasks",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_ansheng_delay_tasks_DeviceId_SlotNum",
                table: "ansheng_delay_tasks",
                columns: new[] { "DeviceId", "SlotNum" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ansheng_delay_tasks");

            migrationBuilder.DropColumn(
                name: "SlotsSnapshot",
                table: "ansheng_device_profiles");

            migrationBuilder.DropColumn(
                name: "SlotsSnapshotAt",
                table: "ansheng_device_profiles");
        }
    }
}
