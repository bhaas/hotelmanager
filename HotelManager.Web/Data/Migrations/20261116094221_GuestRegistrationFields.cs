using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HotelManager.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class GuestRegistrationFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ArrivalMode",
                table: "Guests",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Interests",
                table: "Guests",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LicensePlate",
                table: "Guests",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferralSource",
                table: "Guests",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TravelDocument",
                table: "Guests",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TravelPurpose",
                table: "Guests",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ArrivalMode",
                table: "Guests");

            migrationBuilder.DropColumn(
                name: "Interests",
                table: "Guests");

            migrationBuilder.DropColumn(
                name: "LicensePlate",
                table: "Guests");

            migrationBuilder.DropColumn(
                name: "ReferralSource",
                table: "Guests");

            migrationBuilder.DropColumn(
                name: "TravelDocument",
                table: "Guests");

            migrationBuilder.DropColumn(
                name: "TravelPurpose",
                table: "Guests");
        }
    }
}
