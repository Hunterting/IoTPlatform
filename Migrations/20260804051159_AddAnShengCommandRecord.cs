using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IoTPlatform.Migrations
{
    /// <inheritdoc />
    public partial class AddAnShengCommandRecord : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AnShengCommandRecords",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    AppCode = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CommandId = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DeviceId = table.Column<long>(type: "bigint", nullable: true),
                    Imei = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Method = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FrameId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RejectReason = table.Column<int>(type: "int", nullable: true),
                    RequestJson = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ResponseJson = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ErrorCode = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ErrorMessage = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IssuedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    SentAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    TimeoutAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    DurationMs = table.Column<int>(type: "int", nullable: true),
                    OperatorUserId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnShengCommandRecords", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_AnShengCommandRecords_AppCode_IssuedAt",
                table: "AnShengCommandRecords",
                columns: new[] { "AppCode", "IssuedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AnShengCommandRecords_CommandId",
                table: "AnShengCommandRecords",
                column: "CommandId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AnShengCommandRecords_DeviceId_IssuedAt",
                table: "AnShengCommandRecords",
                columns: new[] { "DeviceId", "IssuedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AnShengCommandRecords_Imei_FrameId",
                table: "AnShengCommandRecords",
                columns: new[] { "Imei", "FrameId" });

            migrationBuilder.CreateIndex(
                name: "IX_AnShengCommandRecords_Status_TimeoutAt",
                table: "AnShengCommandRecords",
                columns: new[] { "Status", "TimeoutAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnShengCommandRecords");
        }
    }
}
