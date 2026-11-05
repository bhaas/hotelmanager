using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HotelManager.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class Beds24Integration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Beds24Connections",
                columns: table => new
                {
                    HotelId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProtectedRefreshToken = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    PropertyId = table.Column<int>(type: "integer", nullable: true),
                    PropertyName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    WebhookKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ConnectedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSyncAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastAttemptAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    LastCreated = table.Column<int>(type: "integer", nullable: false),
                    LastUpdated = table.Column<int>(type: "integer", nullable: false),
                    LastCancelled = table.Column<int>(type: "integer", nullable: false),
                    LastProblems = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Beds24Connections", x => x.HotelId);
                    table.ForeignKey(
                        name: "FK_Beds24Connections_Hotels_HotelId",
                        column: x => x.HotelId,
                        principalTable: "Hotels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Beds24RoomMappings",
                columns: table => new
                {
                    HotelId = table.Column<Guid>(type: "uuid", nullable: false),
                    Beds24RoomId = table.Column<int>(type: "integer", nullable: false),
                    Beds24RoomName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CategoryName = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Beds24RoomMappings", x => new { x.HotelId, x.Beds24RoomId });
                    table.ForeignKey(
                        name: "FK_Beds24RoomMappings_Hotels_HotelId",
                        column: x => x.HotelId,
                        principalTable: "Hotels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Beds24Connections_WebhookKey",
                table: "Beds24Connections",
                column: "WebhookKey",
                unique: true);

            // Beds24Connections stays without RLS (like Hotels) so the background sync and the webhook can find connections.
            migrationBuilder.Sql("""
                ALTER TABLE "Beds24RoomMappings" ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON "Beds24RoomMappings"
                    USING ("HotelId" = NULLIF(current_setting('app.hotel_id', true), '')::uuid)
                    WITH CHECK ("HotelId" = NULLIF(current_setting('app.hotel_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON \"Beds24RoomMappings\";");

            migrationBuilder.DropTable(
                name: "Beds24Connections");

            migrationBuilder.DropTable(
                name: "Beds24RoomMappings");
        }
    }
}
