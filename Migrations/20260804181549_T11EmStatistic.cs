using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IoTPlatform.Migrations
{
    /// <inheritdoc />
    public partial class T11EmStatistic : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ansheng_em_statistics",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    AppCode = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DeviceId = table.Column<long>(type: "bigint", nullable: false),
                    SlotNum = table.Column<int>(type: "int", nullable: false),
                    Granularity = table.Column<int>(type: "int", nullable: false),
                    PeriodKey = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Kwh = table.Column<double>(type: "double", nullable: false),
                    SyncedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ansheng_em_statistics", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ansheng_em_statistics_AppCode_DeviceId",
                table: "ansheng_em_statistics",
                columns: new[] { "AppCode", "DeviceId" });

            migrationBuilder.CreateIndex(
                name: "IX_ansheng_em_statistics_DeviceId",
                table: "ansheng_em_statistics",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_ansheng_em_statistics_DeviceId_SlotNum_Granularity_PeriodKey",
                table: "ansheng_em_statistics",
                columns: new[] { "DeviceId", "SlotNum", "Granularity", "PeriodKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ansheng_em_statistics");
        }
    }
}
