using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IoTPlatform.Migrations
{
    /// <inheritdoc />
    public partial class T10TimeTask : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ansheng_time_tasks",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    AppCode = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DeviceId = table.Column<long>(type: "bigint", nullable: false),
                    SlotNum = table.Column<int>(type: "int", nullable: false),
                    TaskKind = table.Column<int>(type: "int", nullable: false),
                    TaskIndex = table.Column<int>(type: "int", nullable: false),
                    TaskId = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Enable = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    WeekDays = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Hour = table.Column<int>(type: "int", nullable: false),
                    Minute = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UploadEnable = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SHour = table.Column<int>(type: "int", nullable: false),
                    SMinute = table.Column<int>(type: "int", nullable: false),
                    EHour = table.Column<int>(type: "int", nullable: false),
                    EMinute = table.Column<int>(type: "int", nullable: false),
                    OnMins = table.Column<int>(type: "int", nullable: false),
                    OffMins = table.Column<int>(type: "int", nullable: false),
                    SyncedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    RowVersion = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ansheng_time_tasks", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ansheng_time_tasks_AppCode_DeviceId",
                table: "ansheng_time_tasks",
                columns: new[] { "AppCode", "DeviceId" });

            migrationBuilder.CreateIndex(
                name: "IX_ansheng_time_tasks_DeviceId",
                table: "ansheng_time_tasks",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_ansheng_time_tasks_DeviceId_SlotNum_TaskKind_TaskIndex",
                table: "ansheng_time_tasks",
                columns: new[] { "DeviceId", "SlotNum", "TaskKind", "TaskIndex" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ansheng_time_tasks");
        }
    }
}
