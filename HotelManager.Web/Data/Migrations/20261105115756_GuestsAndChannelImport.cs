using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HotelManager.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class GuestsAndChannelImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Reservations_HotelId",
                table: "Reservations");

            migrationBuilder.AddColumn<string>(
                name: "ExternalReference",
                table: "Reservations",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "GuestId",
                table: "Reservations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "Reservations",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "Direct");

            migrationBuilder.CreateTable(
                name: "Guests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HotelId = table.Column<Guid>(type: "uuid", nullable: false),
                    FirstName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    LastName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Company = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Street = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PostalCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    City = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Country = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Nationality = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DateOfBirth = table.Column<DateOnly>(type: "date", nullable: true),
                    Language = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    VatId = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsVip = table.Column<bool>(type: "boolean", nullable: false),
                    MarketingConsent = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AnonymizedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Guests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Guests_Hotels_HotelId",
                        column: x => x.HotelId,
                        principalTable: "Hotels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_GuestId",
                table: "Reservations",
                column: "GuestId");

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_HotelId_Source_ExternalReference",
                table: "Reservations",
                columns: new[] { "HotelId", "Source", "ExternalReference" },
                unique: true,
                filter: "\"ExternalReference\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Guests_HotelId_Email",
                table: "Guests",
                columns: new[] { "HotelId", "Email" });

            migrationBuilder.CreateIndex(
                name: "IX_Guests_HotelId_LastName",
                table: "Guests",
                columns: new[] { "HotelId", "LastName" });

            migrationBuilder.AddForeignKey(
                name: "FK_Reservations_Guests_GuestId",
                table: "Reservations",
                column: "GuestId",
                principalTable: "Guests",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.Sql("""
                ALTER TABLE "Guests" ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON "Guests"
                    USING ("HotelId" = NULLIF(current_setting('app.hotel_id', true), '')::uuid)
                    WITH CHECK ("HotelId" = NULLIF(current_setting('app.hotel_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Reservations_Guests_GuestId",
                table: "Reservations");

            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON \"Guests\";");

            migrationBuilder.DropTable(
                name: "Guests");

            migrationBuilder.DropIndex(
                name: "IX_Reservations_GuestId",
                table: "Reservations");

            migrationBuilder.DropIndex(
                name: "IX_Reservations_HotelId_Source_ExternalReference",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "ExternalReference",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "GuestId",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "Reservations");

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_HotelId",
                table: "Reservations",
                column: "HotelId");
        }
    }
}
