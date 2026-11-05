using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HotelManager.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class HotelOperatingSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FooterText",
                table: "InvoiceDocuments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CashPaymentLimit",
                table: "Hotels",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 10000m);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "CheckInTime",
                table: "Hotels",
                type: "time without time zone",
                nullable: false,
                defaultValue: new TimeOnly(15, 0, 0));

            migrationBuilder.AddColumn<TimeOnly>(
                name: "CheckOutTime",
                table: "Hotels",
                type: "time without time zone",
                nullable: false,
                defaultValue: new TimeOnly(11, 0, 0));

            migrationBuilder.AddColumn<string>(
                name: "InvoiceFooter",
                table: "Hotels",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceNumberPrefix",
                table: "Hotels",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "AT");

            migrationBuilder.AddColumn<int>(
                name: "PaymentTermDays",
                table: "Hotels",
                type: "integer",
                nullable: false,
                defaultValue: 14);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FooterText",
                table: "InvoiceDocuments");

            migrationBuilder.DropColumn(
                name: "CashPaymentLimit",
                table: "Hotels");

            migrationBuilder.DropColumn(
                name: "CheckInTime",
                table: "Hotels");

            migrationBuilder.DropColumn(
                name: "CheckOutTime",
                table: "Hotels");

            migrationBuilder.DropColumn(
                name: "InvoiceFooter",
                table: "Hotels");

            migrationBuilder.DropColumn(
                name: "InvoiceNumberPrefix",
                table: "Hotels");

            migrationBuilder.DropColumn(
                name: "PaymentTermDays",
                table: "Hotels");
        }
    }
}
