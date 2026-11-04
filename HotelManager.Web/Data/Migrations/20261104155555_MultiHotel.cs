using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HotelManager.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class MultiHotel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "City",
                table: "Hotels",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Country",
                table: "Hotels",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "Hotels",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Hotels",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "LegalName",
                table: "Hotels",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Phone",
                table: "Hotels",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PostalCode",
                table: "Hotels",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Street",
                table: "Hotels",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VatId",
                table: "Hotels",
                type: "character varying(24)",
                maxLength: 24,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "HotelMemberships",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    HotelId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HotelMemberships", x => new { x.UserId, x.HotelId });
                    table.ForeignKey(
                        name: "FK_HotelMemberships_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_HotelMemberships_Hotels_HotelId",
                        column: x => x.HotelId,
                        principalTable: "Hotels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HotelMemberships_HotelId",
                table: "HotelMemberships",
                column: "HotelId");

            // Hotel roles were global Identity roles; they become per-hotel memberships.
            // A locked account keeps its (deactivated) membership.
            migrationBuilder.Sql("""
                INSERT INTO "HotelMemberships" ("UserId", "HotelId", "Role", "IsActive", "CreatedAtUtc")
                SELECT DISTINCT ON (u."Id") u."Id", u."HotelId", r."Name",
                       (u."LockoutEnd" IS NULL OR u."LockoutEnd" < now()), u."CreatedAtUtc"
                FROM "AspNetUsers" u
                JOIN "AspNetUserRoles" ur ON ur."UserId" = u."Id"
                JOIN "AspNetRoles" r ON r."Id" = ur."RoleId"
                JOIN "Hotels" h ON h."Id" = u."HotelId"
                WHERE r."Name" IN ('Admin', 'Manager', 'FrontDesk', 'Housekeeping')
                ORDER BY u."Id", array_position(ARRAY['Admin', 'Manager', 'FrontDesk', 'Housekeeping']::text[], r."Name"::text);

                DELETE FROM "AspNetUserRoles" ur
                USING "AspNetRoles" r
                WHERE r."Id" = ur."RoleId" AND r."Name" IN ('Admin', 'Manager', 'FrontDesk', 'Housekeeping');

                DELETE FROM "AspNetRoles" WHERE "Name" IN ('Admin', 'Manager', 'FrontDesk', 'Housekeeping');
                """);

            // Row-level security: the application switches to this restricted role on every connection
            // (TenantSessionInterceptor) and binds app.hotel_id. Hosted owner roles often have BYPASSRLS,
            // which is why a separate NOLOGIN role is used. The role is cluster-wide, hence IF NOT EXISTS.
            migrationBuilder.Sql($"""
                DO $$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{AppRole}') THEN
                        CREATE ROLE {AppRole} NOLOGIN NOBYPASSRLS;
                    END IF;
                END
                $$;
                GRANT {AppRole} TO CURRENT_USER;
                GRANT USAGE ON SCHEMA public TO {AppRole};
                GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO {AppRole};
                GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO {AppRole};
                ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO {AppRole};
                ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO {AppRole};
                """);

            foreach (var table in TenantTables)
            {
                migrationBuilder.Sql($"""
                    ALTER TABLE "{table}" ENABLE ROW LEVEL SECURITY;
                    CREATE POLICY tenant_isolation ON "{table}"
                        USING ("HotelId" = NULLIF(current_setting('app.hotel_id', true), '')::uuid)
                        WITH CHECK ("HotelId" = NULLIF(current_setting('app.hotel_id', true), '')::uuid);
                    """);
            }
        }

        private const string AppRole = "hotelmanager_app";

        /// <summary>Every table with a HotelId column holding hotel-owned data.</summary>
        internal static readonly string[] TenantTables =
        [
            "Rooms", "RoomCategories", "Reservations", "RoomRateRules", "RoomAvailabilityRules",
            "FolioEntries", "PaymentTransactions", "InvoiceDocuments", "InvoiceLines", "InvoiceSequences"
        ];

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in TenantTables)
            {
                migrationBuilder.Sql($"""
                    DROP POLICY IF EXISTS tenant_isolation ON "{table}";
                    ALTER TABLE "{table}" DISABLE ROW LEVEL SECURITY;
                    """);
            }

            // The role is cluster-wide and may be used by other databases, so it is kept.
            migrationBuilder.Sql($"""
                ALTER DEFAULT PRIVILEGES IN SCHEMA public REVOKE ALL ON TABLES FROM {AppRole};
                ALTER DEFAULT PRIVILEGES IN SCHEMA public REVOKE ALL ON SEQUENCES FROM {AppRole};
                REVOKE ALL ON ALL TABLES IN SCHEMA public FROM {AppRole};
                REVOKE ALL ON ALL SEQUENCES IN SCHEMA public FROM {AppRole};
                REVOKE USAGE ON SCHEMA public FROM {AppRole};
                """);

            migrationBuilder.Sql("""
                INSERT INTO "AspNetRoles" ("Id", "Name", "NormalizedName", "ConcurrencyStamp")
                SELECT gen_random_uuid()::text, role_name, upper(role_name), gen_random_uuid()::text
                FROM unnest(ARRAY['Admin', 'Manager', 'FrontDesk', 'Housekeeping']) AS role_name
                WHERE NOT EXISTS (SELECT 1 FROM "AspNetRoles" WHERE "Name" = role_name);

                INSERT INTO "AspNetUserRoles" ("UserId", "RoleId")
                SELECT m."UserId", r."Id"
                FROM "HotelMemberships" m
                JOIN "AspNetUsers" u ON u."Id" = m."UserId" AND u."HotelId" = m."HotelId"
                JOIN "AspNetRoles" r ON r."Name" = m."Role"
                ON CONFLICT DO NOTHING;
                """);

            migrationBuilder.DropTable(
                name: "HotelMemberships");

            migrationBuilder.DropColumn(
                name: "City",
                table: "Hotels");

            migrationBuilder.DropColumn(
                name: "Country",
                table: "Hotels");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "Hotels");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Hotels");

            migrationBuilder.DropColumn(
                name: "LegalName",
                table: "Hotels");

            migrationBuilder.DropColumn(
                name: "Phone",
                table: "Hotels");

            migrationBuilder.DropColumn(
                name: "PostalCode",
                table: "Hotels");

            migrationBuilder.DropColumn(
                name: "Street",
                table: "Hotels");

            migrationBuilder.DropColumn(
                name: "VatId",
                table: "Hotels");
        }
    }
}
