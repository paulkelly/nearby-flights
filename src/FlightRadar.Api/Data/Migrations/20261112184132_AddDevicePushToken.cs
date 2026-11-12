using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlightRadar.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDevicePushToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "push_token",
                table: "devices",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "push_token",
                table: "devices");
        }
    }
}
