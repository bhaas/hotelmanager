using HotelManager.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace HotelManager.Web.Features.Billing;

public static class BillingSchemaInitializer
{
    public static async Task EnsureSchemaAsync(HotelDbContext dbContext, CancellationToken cancellationToken = default)
    {
        var sql = """
                  CREATE TABLE IF NOT EXISTS "InvoiceSequences" (
                      "Year" integer NOT NULL,
                      "LastNumber" integer NOT NULL,
                      CONSTRAINT "PK_InvoiceSequences" PRIMARY KEY ("Year"),
                      CONSTRAINT "CK_InvoiceSequences_YearRange" CHECK ("Year" >= 2000),
                      CONSTRAINT "CK_InvoiceSequences_LastNumberNonNegative" CHECK ("LastNumber" >= 0)
                  );

                  CREATE TABLE IF NOT EXISTS "FolioEntries" (
                      "Id" uuid NOT NULL,
                      "ReservationId" uuid NOT NULL,
                      "EntryType" character varying(16) NOT NULL,
                      "Description" character varying(250) NOT NULL,
                      "Quantity" numeric(12,3) NOT NULL,
                      "UnitNetAmount" numeric(12,2) NOT NULL,
                      "VatRatePercent" numeric(5,2) NOT NULL,
                      "ServiceDate" date NOT NULL,
                      "Currency" character varying(3) NOT NULL,
                      "TaxExemptionReason" character varying(200),
                      "CreatedAtUtc" timestamp with time zone NOT NULL,
                      CONSTRAINT "PK_FolioEntries" PRIMARY KEY ("Id"),
                      CONSTRAINT "FK_FolioEntries_Reservations_ReservationId" FOREIGN KEY ("ReservationId") REFERENCES "Reservations" ("Id") ON DELETE CASCADE,
                      CONSTRAINT "CK_FolioEntries_QuantityPositive" CHECK ("Quantity" > 0),
                      CONSTRAINT "CK_FolioEntries_UnitNetAmountNonNegative" CHECK ("UnitNetAmount" >= 0),
                      CONSTRAINT "CK_FolioEntries_VatRateAllowed" CHECK ("VatRatePercent" IN (0, 10, 13, 20)),
                      CONSTRAINT "CK_FolioEntries_CurrencyEur" CHECK ("Currency" = 'EUR')
                  );

                  CREATE TABLE IF NOT EXISTS "PaymentTransactions" (
                      "Id" uuid NOT NULL,
                      "ReservationId" uuid NOT NULL,
                      "Amount" numeric(12,2) NOT NULL,
                      "Currency" character varying(3) NOT NULL,
                      "Method" character varying(24) NOT NULL,
                      "PaidAtUtc" timestamp with time zone NOT NULL,
                      "Reference" character varying(80),
                      "Notes" character varying(250),
                      CONSTRAINT "PK_PaymentTransactions" PRIMARY KEY ("Id"),
                      CONSTRAINT "FK_PaymentTransactions_Reservations_ReservationId" FOREIGN KEY ("ReservationId") REFERENCES "Reservations" ("Id") ON DELETE CASCADE,
                      CONSTRAINT "CK_PaymentTransactions_AmountPositive" CHECK ("Amount" > 0),
                      CONSTRAINT "CK_PaymentTransactions_CurrencyEur" CHECK ("Currency" = 'EUR')
                  );

                  CREATE TABLE IF NOT EXISTS "InvoiceDocuments" (
                      "Id" uuid NOT NULL,
                      "ReservationId" uuid NOT NULL,
                      "InvoiceNumber" character varying(24) NOT NULL,
                      "ServiceFrom" date NOT NULL,
                      "ServiceTo" date NOT NULL,
                      "DueDate" date NOT NULL,
                      "IssuedAtUtc" timestamp with time zone NOT NULL,
                      "Currency" character varying(3) NOT NULL,
                      "SellerName" character varying(200) NOT NULL,
                      "SellerAddress" character varying(300) NOT NULL,
                      "SellerVatId" character varying(24) NOT NULL,
                      "BuyerName" character varying(200) NOT NULL,
                      "BuyerAddress" character varying(300) NOT NULL,
                      "BuyerVatId" character varying(24),
                      "NetAmount" numeric(12,2) NOT NULL,
                      "VatAmount" numeric(12,2) NOT NULL,
                      "GrossAmount" numeric(12,2) NOT NULL,
                      CONSTRAINT "PK_InvoiceDocuments" PRIMARY KEY ("Id"),
                      CONSTRAINT "FK_InvoiceDocuments_Reservations_ReservationId" FOREIGN KEY ("ReservationId") REFERENCES "Reservations" ("Id") ON DELETE CASCADE,
                      CONSTRAINT "CK_InvoiceDocuments_CurrencyEur" CHECK ("Currency" = 'EUR'),
                      CONSTRAINT "CK_InvoiceDocuments_GrossNonNegative" CHECK ("GrossAmount" >= 0)
                  );

                  CREATE TABLE IF NOT EXISTS "InvoiceLines" (
                      "Id" uuid NOT NULL,
                      "InvoiceDocumentId" uuid NOT NULL,
                      "Description" character varying(250) NOT NULL,
                      "Quantity" numeric(12,3) NOT NULL,
                      "UnitNetAmount" numeric(12,2) NOT NULL,
                      "VatRatePercent" numeric(5,2) NOT NULL,
                      "NetAmount" numeric(12,2) NOT NULL,
                      "VatAmount" numeric(12,2) NOT NULL,
                      "GrossAmount" numeric(12,2) NOT NULL,
                      "TaxExemptionReason" character varying(200),
                      CONSTRAINT "PK_InvoiceLines" PRIMARY KEY ("Id"),
                      CONSTRAINT "FK_InvoiceLines_InvoiceDocuments_InvoiceDocumentId" FOREIGN KEY ("InvoiceDocumentId") REFERENCES "InvoiceDocuments" ("Id") ON DELETE CASCADE,
                      CONSTRAINT "CK_InvoiceLines_QuantityPositive" CHECK ("Quantity" > 0),
                      CONSTRAINT "CK_InvoiceLines_VatRateAllowed" CHECK ("VatRatePercent" IN (0, 10, 13, 20))
                  );

                  CREATE INDEX IF NOT EXISTS "IX_FolioEntries_ReservationId_ServiceDate" ON "FolioEntries" ("ReservationId", "ServiceDate");
                  CREATE INDEX IF NOT EXISTS "IX_PaymentTransactions_ReservationId_PaidAtUtc" ON "PaymentTransactions" ("ReservationId", "PaidAtUtc");
                  CREATE UNIQUE INDEX IF NOT EXISTS "IX_InvoiceDocuments_InvoiceNumber" ON "InvoiceDocuments" ("InvoiceNumber");
                  CREATE UNIQUE INDEX IF NOT EXISTS "IX_InvoiceDocuments_ReservationId" ON "InvoiceDocuments" ("ReservationId");
                  CREATE INDEX IF NOT EXISTS "IX_InvoiceLines_InvoiceDocumentId" ON "InvoiceLines" ("InvoiceDocumentId");
                  """;

        await dbContext.Database.ExecuteSqlRawAsync(sql, cancellationToken);
    }
}
