using HotelManager.Web.Domain;
using HotelManager.Web.Domain.Billing;
using Microsoft.EntityFrameworkCore;

namespace HotelManager.Web.Data;

public sealed class HotelDbContext(DbContextOptions<HotelDbContext> options) : DbContext(options)
{
    public DbSet<Room> Rooms => Set<Room>();

    public DbSet<RoomCategory> RoomCategories => Set<RoomCategory>();

    public DbSet<Reservation> Reservations => Set<Reservation>();

    public DbSet<RoomRateRule> RoomRateRules => Set<RoomRateRule>();

    public DbSet<RoomAvailabilityRule> RoomAvailabilityRules => Set<RoomAvailabilityRule>();

    public DbSet<FolioEntry> FolioEntries => Set<FolioEntry>();

    public DbSet<PaymentTransaction> PaymentTransactions => Set<PaymentTransaction>();

    public DbSet<InvoiceDocument> InvoiceDocuments => Set<InvoiceDocument>();

    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();

    public DbSet<InvoiceSequence> InvoiceSequences => Set<InvoiceSequence>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var roomBuilder = modelBuilder.Entity<Room>();
        roomBuilder.HasKey(room => room.Id);
        roomBuilder.Property(room => room.Number).IsRequired().HasMaxLength(12);
        roomBuilder.Property(room => room.Type).IsRequired().HasMaxLength(40);
        roomBuilder.Property(room => room.HousekeepingStatus).HasConversion<string>().HasMaxLength(16);
        roomBuilder.Property(room => room.Features).HasMaxLength(300);
        roomBuilder.Property(room => room.Notes).HasMaxLength(500);
        roomBuilder.HasIndex(room => room.Number).IsUnique();

        var categoryBuilder = modelBuilder.Entity<RoomCategory>();
        categoryBuilder.HasKey(category => category.Id);
        categoryBuilder.Property(category => category.Code).IsRequired().HasMaxLength(10);
        categoryBuilder.Property(category => category.Name).IsRequired().HasMaxLength(40);
        categoryBuilder.Property(category => category.Description).HasMaxLength(500);
        categoryBuilder.HasIndex(category => category.Code).IsUnique();
        categoryBuilder.HasIndex(category => category.Name).IsUnique();
        categoryBuilder.ToTable(table =>
        {
            table.HasCheckConstraint("CK_RoomCategories_StandardOccupancyPositive", "\"StandardOccupancy\" > 0");
            table.HasCheckConstraint("CK_RoomCategories_MaxOccupancyValid", "\"MaxOccupancy\" >= \"StandardOccupancy\"");
        });

        var reservationBuilder = modelBuilder.Entity<Reservation>();
        reservationBuilder.HasKey(reservation => reservation.Id);
        reservationBuilder.Property(reservation => reservation.GuestName).IsRequired().HasMaxLength(120);
        reservationBuilder.Property(reservation => reservation.Occupancy).HasDefaultValue(1);
        reservationBuilder.Property(reservation => reservation.Notes).HasMaxLength(500);
        reservationBuilder.Property(reservation => reservation.Status).HasConversion<string>().HasMaxLength(24);
        reservationBuilder.HasOne(reservation => reservation.Room)
            .WithMany(room => room.Reservations)
            .HasForeignKey(reservation => reservation.RoomId)
            .OnDelete(DeleteBehavior.Cascade);
        reservationBuilder.ToTable(table =>
        {
            table.HasCheckConstraint("CK_Reservations_CheckOutAfterCheckIn", "\"CheckOut\" > \"CheckIn\"");
            table.HasCheckConstraint("CK_Reservations_OccupancyPositive", "\"Occupancy\" > 0");
        });

        var roomRateRuleBuilder = modelBuilder.Entity<RoomRateRule>();
        roomRateRuleBuilder.HasKey(rule => rule.Id);
        roomRateRuleBuilder.Property(rule => rule.RoomType).IsRequired().HasMaxLength(40);
        roomRateRuleBuilder.Property(rule => rule.NightlyNetAmount).HasPrecision(12, 2);
        roomRateRuleBuilder.Property(rule => rule.VatRatePercent).HasPrecision(5, 2);
        roomRateRuleBuilder.Property(rule => rule.Currency).IsRequired().HasMaxLength(3);
        roomRateRuleBuilder.HasIndex(rule => new { rule.RoomType, rule.StartDate, rule.EndDate });
        roomRateRuleBuilder.ToTable(table =>
        {
            table.HasCheckConstraint("CK_RoomRateRules_EndAfterStart", "\"EndDate\" > \"StartDate\"");
            table.HasCheckConstraint("CK_RoomRateRules_OccupancyFromPositive", "\"OccupancyFrom\" > 0");
            table.HasCheckConstraint("CK_RoomRateRules_OccupancyToValid", "\"OccupancyTo\" IS NULL OR \"OccupancyTo\" >= \"OccupancyFrom\"");
            table.HasCheckConstraint("CK_RoomRateRules_NightlyNetNonNegative", "\"NightlyNetAmount\" >= 0");
            table.HasCheckConstraint("CK_RoomRateRules_VatRateAllowed", "\"VatRatePercent\" IN (0, 10, 13, 20)");
            table.HasCheckConstraint("CK_RoomRateRules_CurrencyEur", "\"Currency\" = 'EUR'");
        });

        var availabilityRuleBuilder = modelBuilder.Entity<RoomAvailabilityRule>();
        availabilityRuleBuilder.HasKey(rule => rule.Id);
        availabilityRuleBuilder.Property(rule => rule.RoomType).HasMaxLength(40);
        availabilityRuleBuilder.HasOne(rule => rule.Room)
            .WithMany(room => room.AvailabilityRules)
            .HasForeignKey(rule => rule.RoomId)
            .OnDelete(DeleteBehavior.Cascade);
        availabilityRuleBuilder.HasIndex(rule => new { rule.RoomId, rule.StartDate, rule.EndDate });
        availabilityRuleBuilder.HasIndex(rule => new { rule.RoomType, rule.StartDate, rule.EndDate });
        availabilityRuleBuilder.ToTable(table =>
        {
            table.HasCheckConstraint("CK_RoomAvailabilityRules_EndAfterStart", "\"EndDate\" > \"StartDate\"");
            table.HasCheckConstraint("CK_RoomAvailabilityRules_ScopeRequired", "\"RoomId\" IS NOT NULL OR \"RoomType\" IS NOT NULL");
            table.HasCheckConstraint("CK_RoomAvailabilityRules_MinStayPositive", "\"MinStayNights\" IS NULL OR \"MinStayNights\" > 0");
        });

        var folioEntryBuilder = modelBuilder.Entity<FolioEntry>();
        folioEntryBuilder.HasKey(entry => entry.Id);
        folioEntryBuilder.Property(entry => entry.EntryType).HasConversion<string>().HasMaxLength(16);
        folioEntryBuilder.Property(entry => entry.Description).IsRequired().HasMaxLength(250);
        folioEntryBuilder.Property(entry => entry.Quantity).HasPrecision(12, 3);
        folioEntryBuilder.Property(entry => entry.UnitNetAmount).HasPrecision(12, 2);
        folioEntryBuilder.Property(entry => entry.VatRatePercent).HasPrecision(5, 2);
        folioEntryBuilder.Property(entry => entry.Currency).IsRequired().HasMaxLength(3);
        folioEntryBuilder.Property(entry => entry.TaxExemptionReason).HasMaxLength(200);
        folioEntryBuilder.HasOne(entry => entry.Reservation)
            .WithMany(reservation => reservation.FolioEntries)
            .HasForeignKey(entry => entry.ReservationId)
            .OnDelete(DeleteBehavior.Cascade);
        folioEntryBuilder.HasIndex(entry => new { entry.ReservationId, entry.ServiceDate });
        folioEntryBuilder.ToTable(table =>
        {
            table.HasCheckConstraint("CK_FolioEntries_QuantityPositive", "\"Quantity\" > 0");
            table.HasCheckConstraint("CK_FolioEntries_UnitNetAmountNonNegative", "\"UnitNetAmount\" >= 0");
            table.HasCheckConstraint("CK_FolioEntries_VatRateAllowed", "\"VatRatePercent\" IN (0, 10, 13, 20)");
            table.HasCheckConstraint("CK_FolioEntries_CurrencyEur", "\"Currency\" = 'EUR'");
        });

        var paymentBuilder = modelBuilder.Entity<PaymentTransaction>();
        paymentBuilder.HasKey(payment => payment.Id);
        paymentBuilder.Property(payment => payment.Amount).HasPrecision(12, 2);
        paymentBuilder.Property(payment => payment.Currency).IsRequired().HasMaxLength(3);
        paymentBuilder.Property(payment => payment.Method).HasConversion<string>().HasMaxLength(24);
        paymentBuilder.Property(payment => payment.Reference).HasMaxLength(80);
        paymentBuilder.Property(payment => payment.Notes).HasMaxLength(250);
        paymentBuilder.HasOne(payment => payment.Reservation)
            .WithMany(reservation => reservation.Payments)
            .HasForeignKey(payment => payment.ReservationId)
            .OnDelete(DeleteBehavior.Cascade);
        paymentBuilder.HasIndex(payment => new { payment.ReservationId, payment.PaidAtUtc });
        paymentBuilder.ToTable(table =>
        {
            table.HasCheckConstraint("CK_PaymentTransactions_AmountPositive", "\"Amount\" > 0");
            table.HasCheckConstraint("CK_PaymentTransactions_CurrencyEur", "\"Currency\" = 'EUR'");
        });

        var invoiceBuilder = modelBuilder.Entity<InvoiceDocument>();
        invoiceBuilder.HasKey(document => document.Id);
        invoiceBuilder.Property(document => document.InvoiceNumber).IsRequired().HasMaxLength(24);
        invoiceBuilder.Property(document => document.Currency).IsRequired().HasMaxLength(3);
        invoiceBuilder.Property(document => document.SellerName).IsRequired().HasMaxLength(200);
        invoiceBuilder.Property(document => document.SellerAddress).IsRequired().HasMaxLength(300);
        invoiceBuilder.Property(document => document.SellerVatId).IsRequired().HasMaxLength(24);
        invoiceBuilder.Property(document => document.BuyerName).IsRequired().HasMaxLength(200);
        invoiceBuilder.Property(document => document.BuyerAddress).IsRequired().HasMaxLength(300);
        invoiceBuilder.Property(document => document.BuyerVatId).HasMaxLength(24);
        invoiceBuilder.Property(document => document.NetAmount).HasPrecision(12, 2);
        invoiceBuilder.Property(document => document.VatAmount).HasPrecision(12, 2);
        invoiceBuilder.Property(document => document.GrossAmount).HasPrecision(12, 2);
        invoiceBuilder.HasOne(document => document.Reservation)
            .WithMany(reservation => reservation.Invoices)
            .HasForeignKey(document => document.ReservationId)
            .OnDelete(DeleteBehavior.Cascade);
        invoiceBuilder.HasIndex(document => document.InvoiceNumber).IsUnique();
        invoiceBuilder.HasIndex(document => document.ReservationId).IsUnique();
        invoiceBuilder.ToTable(table =>
        {
            table.HasCheckConstraint("CK_InvoiceDocuments_CurrencyEur", "\"Currency\" = 'EUR'");
            table.HasCheckConstraint("CK_InvoiceDocuments_GrossNonNegative", "\"GrossAmount\" >= 0");
        });

        var invoiceLineBuilder = modelBuilder.Entity<InvoiceLine>();
        invoiceLineBuilder.HasKey(line => line.Id);
        invoiceLineBuilder.Property(line => line.Description).IsRequired().HasMaxLength(250);
        invoiceLineBuilder.Property(line => line.Quantity).HasPrecision(12, 3);
        invoiceLineBuilder.Property(line => line.UnitNetAmount).HasPrecision(12, 2);
        invoiceLineBuilder.Property(line => line.VatRatePercent).HasPrecision(5, 2);
        invoiceLineBuilder.Property(line => line.NetAmount).HasPrecision(12, 2);
        invoiceLineBuilder.Property(line => line.VatAmount).HasPrecision(12, 2);
        invoiceLineBuilder.Property(line => line.GrossAmount).HasPrecision(12, 2);
        invoiceLineBuilder.Property(line => line.TaxExemptionReason).HasMaxLength(200);
        invoiceLineBuilder.HasOne(line => line.InvoiceDocument)
            .WithMany(document => document.Lines)
            .HasForeignKey(line => line.InvoiceDocumentId)
            .OnDelete(DeleteBehavior.Cascade);
        invoiceLineBuilder.ToTable(table =>
        {
            table.HasCheckConstraint("CK_InvoiceLines_QuantityPositive", "\"Quantity\" > 0");
            table.HasCheckConstraint("CK_InvoiceLines_VatRateAllowed", "\"VatRatePercent\" IN (0, 10, 13, 20)");
        });

        var sequenceBuilder = modelBuilder.Entity<InvoiceSequence>();
        sequenceBuilder.HasKey(sequence => sequence.Year);
        sequenceBuilder.ToTable(table =>
        {
            table.HasCheckConstraint("CK_InvoiceSequences_YearRange", "\"Year\" >= 2000");
            table.HasCheckConstraint("CK_InvoiceSequences_LastNumberNonNegative", "\"LastNumber\" >= 0");
        });
    }
}
