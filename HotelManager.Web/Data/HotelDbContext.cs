using System.Reflection;
using HotelManager.Web.Domain;
using HotelManager.Web.Domain.Billing;
using HotelManager.Web.Infrastructure.Tenancy;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace HotelManager.Web.Data;

/// <summary>
/// Application database. Every <see cref="ITenantEntity"/> is automatically scoped to the hotel of the
/// current <see cref="ITenantContext"/>: queries only return that hotel's rows and inserts are stamped
/// with its id. Without a tenant, tenant data is invisible and cannot be written.
/// </summary>
public sealed class HotelDbContext(DbContextOptions<HotelDbContext> options, ITenantContext? tenantContext = null)
    : IdentityDbContext<ApplicationUser>(options), IDataProtectionKeyContext
{
    private static readonly MethodInfo ConfigureTenantEntityMethod = typeof(HotelDbContext)
        .GetMethod(nameof(ConfigureTenantEntity), BindingFlags.NonPublic | BindingFlags.Instance)!;

    /// <summary>Evaluated per query by the global filters; <see cref="Guid.Empty"/> matches nothing.</summary>
    private Guid CurrentHotelId => tenantContext?.HotelId ?? Guid.Empty;

    public DbSet<Hotel> Hotels => Set<Hotel>();

    public DbSet<HotelMembership> HotelMemberships => Set<HotelMembership>();

    /// <summary>
    /// When true the connection keeps the login (owner) role, so PostgreSQL row-level security is not applied.
    /// Only for migrations, database maintenance and explicit platform-wide operations.
    /// </summary>
    public bool BypassRowLevelSecurity { get; set; }

    /// <summary>Hotel the PostgreSQL session is bound to (row-level security); null when no hotel is selected.</summary>
    internal Guid? SessionHotelId => tenantContext?.HotelId;

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    public DbSet<Room> Rooms => Set<Room>();

    public DbSet<RoomCategory> RoomCategories => Set<RoomCategory>();

    public DbSet<Reservation> Reservations => Set<Reservation>();

    public DbSet<Guest> Guests => Set<Guest>();

    public DbSet<RoomRateRule> RoomRateRules => Set<RoomRateRule>();

    public DbSet<RoomAvailabilityRule> RoomAvailabilityRules => Set<RoomAvailabilityRule>();

    public DbSet<FolioEntry> FolioEntries => Set<FolioEntry>();

    public DbSet<PaymentTransaction> PaymentTransactions => Set<PaymentTransaction>();

    public DbSet<InvoiceDocument> InvoiceDocuments => Set<InvoiceDocument>();

    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();

    public DbSet<InvoiceSequence> InvoiceSequences => Set<InvoiceSequence>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyTenantRules();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ApplyTenantRules();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ApplyTenantRules()
    {
        var hotelId = tenantContext?.HotelId;
        foreach (var entry in ChangeTracker.Entries<ITenantEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    if (entry.Entity.HotelId == Guid.Empty)
                    {
                        entry.Entity.HotelId = hotelId
                            ?? throw new InvalidOperationException($"Cannot create {entry.Metadata.ClrType.Name} without a hotel context.");
                    }
                    else if (hotelId is not null && entry.Entity.HotelId != hotelId)
                    {
                        throw new InvalidOperationException($"Cannot create {entry.Metadata.ClrType.Name} for another hotel.");
                    }

                    break;
                case EntityState.Modified or EntityState.Deleted:
                    if (hotelId is null || entry.Entity.HotelId != hotelId || entry.Property(entity => entity.HotelId).IsModified)
                    {
                        throw new InvalidOperationException($"Cannot change {entry.Metadata.ClrType.Name} outside the current hotel.");
                    }

                    break;
            }
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var hotelBuilder = modelBuilder.Entity<Hotel>();
        hotelBuilder.HasKey(hotel => hotel.Id);
        hotelBuilder.Property(hotel => hotel.Name).IsRequired().HasMaxLength(120);
        hotelBuilder.Property(hotel => hotel.TimeZone).IsRequired().HasMaxLength(64);
        hotelBuilder.Property(hotel => hotel.Currency).IsRequired().HasMaxLength(3);
        hotelBuilder.Property(hotel => hotel.LegalName).HasMaxLength(200);
        hotelBuilder.Property(hotel => hotel.Street).HasMaxLength(200);
        hotelBuilder.Property(hotel => hotel.PostalCode).HasMaxLength(16);
        hotelBuilder.Property(hotel => hotel.City).HasMaxLength(100);
        hotelBuilder.Property(hotel => hotel.Country).HasMaxLength(100);
        hotelBuilder.Property(hotel => hotel.Phone).HasMaxLength(40);
        hotelBuilder.Property(hotel => hotel.Email).HasMaxLength(256);
        hotelBuilder.Property(hotel => hotel.VatId).HasMaxLength(24);
        hotelBuilder.Property(hotel => hotel.InvoiceNumberPrefix).IsRequired().HasMaxLength(10);
        hotelBuilder.Property(hotel => hotel.CashPaymentLimit).HasPrecision(12, 2);
        hotelBuilder.Property(hotel => hotel.InvoiceFooter).HasMaxLength(500);
        hotelBuilder.Property(hotel => hotel.IsActive).HasDefaultValue(true);
        hotelBuilder.Ignore(hotel => hotel.InvoiceSellerName);
        hotelBuilder.Ignore(hotel => hotel.InvoiceSellerAddress);

        var userBuilder = modelBuilder.Entity<ApplicationUser>();
        userBuilder.Property(user => user.DisplayName).IsRequired().HasMaxLength(120);
        userBuilder.HasOne(user => user.Hotel)
            .WithMany()
            .HasForeignKey(user => user.HotelId)
            .OnDelete(DeleteBehavior.Restrict);

        var membershipBuilder = modelBuilder.Entity<HotelMembership>();
        membershipBuilder.HasKey(membership => new { membership.UserId, membership.HotelId });
        membershipBuilder.Property(membership => membership.Role).IsRequired().HasMaxLength(32);
        membershipBuilder.Property(membership => membership.IsActive).HasDefaultValue(true);
        membershipBuilder.HasIndex(membership => membership.HotelId);
        membershipBuilder.HasOne(membership => membership.User)
            .WithMany(user => user.Memberships)
            .HasForeignKey(membership => membership.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        membershipBuilder.HasOne(membership => membership.Hotel)
            .WithMany()
            .HasForeignKey(membership => membership.HotelId)
            .OnDelete(DeleteBehavior.Cascade);

        var roomBuilder = modelBuilder.Entity<Room>();
        roomBuilder.HasKey(room => room.Id);
        roomBuilder.Property(room => room.Number).IsRequired().HasMaxLength(12);
        roomBuilder.Property(room => room.Type).IsRequired().HasMaxLength(40);
        roomBuilder.Property(room => room.HousekeepingStatus).HasConversion<string>().HasMaxLength(16);
        roomBuilder.Property(room => room.Features).HasMaxLength(300);
        roomBuilder.Property(room => room.Notes).HasMaxLength(500);
        roomBuilder.HasIndex(room => new { room.HotelId, room.Number }).IsUnique();

        var categoryBuilder = modelBuilder.Entity<RoomCategory>();
        categoryBuilder.HasKey(category => category.Id);
        categoryBuilder.Property(category => category.Code).IsRequired().HasMaxLength(10);
        categoryBuilder.Property(category => category.Name).IsRequired().HasMaxLength(40);
        categoryBuilder.Property(category => category.Description).HasMaxLength(500);
        categoryBuilder.HasIndex(category => new { category.HotelId, category.Code }).IsUnique();
        categoryBuilder.HasIndex(category => new { category.HotelId, category.Name }).IsUnique();
        categoryBuilder.ToTable(table =>
        {
            table.HasCheckConstraint("CK_RoomCategories_StandardOccupancyPositive", "\"StandardOccupancy\" > 0");
            table.HasCheckConstraint("CK_RoomCategories_MaxOccupancyValid", "\"MaxOccupancy\" >= \"StandardOccupancy\"");
        });

        var guestBuilder = modelBuilder.Entity<Guest>();
        guestBuilder.HasKey(guest => guest.Id);
        guestBuilder.Property(guest => guest.FirstName).HasMaxLength(80);
        guestBuilder.Property(guest => guest.LastName).IsRequired().HasMaxLength(120);
        guestBuilder.Property(guest => guest.Company).HasMaxLength(200);
        guestBuilder.Property(guest => guest.Email).HasMaxLength(256);
        guestBuilder.Property(guest => guest.Phone).HasMaxLength(40);
        guestBuilder.Property(guest => guest.Street).HasMaxLength(200);
        guestBuilder.Property(guest => guest.PostalCode).HasMaxLength(16);
        guestBuilder.Property(guest => guest.City).HasMaxLength(100);
        guestBuilder.Property(guest => guest.Country).HasMaxLength(100);
        guestBuilder.Property(guest => guest.Nationality).HasMaxLength(100);
        guestBuilder.Property(guest => guest.Language).HasMaxLength(8);
        guestBuilder.Property(guest => guest.VatId).HasMaxLength(24);
        guestBuilder.Property(guest => guest.Notes).HasMaxLength(1000);
        guestBuilder.Ignore(guest => guest.DisplayName);
        guestBuilder.Ignore(guest => guest.PostalAddress);
        guestBuilder.HasIndex(guest => new { guest.HotelId, guest.LastName });
        guestBuilder.HasIndex(guest => new { guest.HotelId, guest.Email });

        var reservationBuilder = modelBuilder.Entity<Reservation>();
        reservationBuilder.HasKey(reservation => reservation.Id);
        reservationBuilder.Property(reservation => reservation.GuestName).IsRequired().HasMaxLength(120);
        reservationBuilder.Property(reservation => reservation.Occupancy).HasDefaultValue(1);
        reservationBuilder.Property(reservation => reservation.Notes).HasMaxLength(500);
        reservationBuilder.Property(reservation => reservation.Status).HasConversion<string>().HasMaxLength(24);
        reservationBuilder.Property(reservation => reservation.Source).HasConversion<string>().HasMaxLength(24);
        reservationBuilder.Property(reservation => reservation.ExternalReference).HasMaxLength(40);
        reservationBuilder.HasIndex(reservation => new { reservation.HotelId, reservation.Source, reservation.ExternalReference })
            .IsUnique()
            .HasFilter("\"ExternalReference\" IS NOT NULL");
        reservationBuilder.HasOne(reservation => reservation.Guest)
            .WithMany(guest => guest.Reservations)
            .HasForeignKey(reservation => reservation.GuestId)
            .OnDelete(DeleteBehavior.SetNull);
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
        roomRateRuleBuilder.HasIndex(rule => new { rule.HotelId, rule.RoomType, rule.StartDate, rule.EndDate });
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
        availabilityRuleBuilder.HasIndex(rule => new { rule.HotelId, rule.RoomType, rule.StartDate, rule.EndDate });
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
        invoiceBuilder.Property(document => document.FooterText).HasMaxLength(500);
        invoiceBuilder.Property(document => document.NetAmount).HasPrecision(12, 2);
        invoiceBuilder.Property(document => document.VatAmount).HasPrecision(12, 2);
        invoiceBuilder.Property(document => document.GrossAmount).HasPrecision(12, 2);
        invoiceBuilder.HasOne(document => document.Reservation)
            .WithMany(reservation => reservation.Invoices)
            .HasForeignKey(document => document.ReservationId)
            .OnDelete(DeleteBehavior.Cascade);
        invoiceBuilder.HasIndex(document => new { document.HotelId, document.InvoiceNumber }).IsUnique();
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
        sequenceBuilder.HasKey(sequence => new { sequence.HotelId, sequence.Year });
        sequenceBuilder.ToTable(table =>
        {
            table.HasCheckConstraint("CK_InvoiceSequences_YearRange", "\"Year\" >= 2000");
            table.HasCheckConstraint("CK_InvoiceSequences_LastNumberNonNegative", "\"LastNumber\" >= 0");
        });

        foreach (var entityType in modelBuilder.Model.GetEntityTypes()
                     .Where(type => typeof(ITenantEntity).IsAssignableFrom(type.ClrType))
                     .ToList())
        {
            ConfigureTenantEntityMethod.MakeGenericMethod(entityType.ClrType).Invoke(this, [modelBuilder]);
        }
    }

    private void ConfigureTenantEntity<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, ITenantEntity
    {
        var builder = modelBuilder.Entity<TEntity>();
        builder.HasOne<Hotel>()
            .WithMany()
            .HasForeignKey(entity => entity.HotelId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasQueryFilter(entity => entity.HotelId == CurrentHotelId);
    }
}
