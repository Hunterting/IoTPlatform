using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IoTPlatform.Migrations
{
    /// <inheritdoc />
    public partial class AddEnergyMeteringFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "ElectricKWh",
                table: "device_data_records",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ElectricPower",
                table: "device_data_records",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "GasFlow",
                table: "device_data_records",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "GasTotal",
                table: "device_data_records",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "WaterFlow",
                table: "device_data_records",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "WaterTotal",
                table: "device_data_records",
                type: "double",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ElectricKWh",
                table: "device_data_records");

            migrationBuilder.DropColumn(
                name: "ElectricPower",
                table: "device_data_records");

            migrationBuilder.DropColumn(
                name: "GasFlow",
                table: "device_data_records");

            migrationBuilder.DropColumn(
                name: "GasTotal",
                table: "device_data_records");

            migrationBuilder.DropColumn(
                name: "WaterFlow",
                table: "device_data_records");

            migrationBuilder.DropColumn(
                name: "WaterTotal",
                table: "device_data_records");
        }
    }
}
