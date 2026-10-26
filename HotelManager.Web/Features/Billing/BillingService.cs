using System.Data;
using HotelManager.Web.Data;
using HotelManager.Web.Domain;
using HotelManager.Web.Domain.Billing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace HotelManager.Web.Features.Billing;

public sealed class BillingService(
    IDbContextFactory<HotelDbContext> dbContextFactory,
    IStringLocalizer<BillingService> localizer,
    IOptions<BillingComplianceOptions> complianceOptions) : IBillingService
{
    private static readonly decimal[] AllowedVatRates = [0m, 10m, 13m, 20m];
    private const string BillingCurrency = "EUR";

    public async Task<BillingFolioSnapshot> GetFolioAsync(Guid reservationId, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await GetFolioSnapshotInternalAsync(dbContext, reservationId, cancellationToken);
    }

    public async Task<BillingFolioSnapshot> AddFolioEntryAsync(
        Guid reservationId,
        BillingAddFolioEntryRequest request,
        CancellationToken cancellationToken)
    {
        ValidateFolioEntryRequest(request);

        return await ExecuteSerializableAsync(async dbContext =>
        {
            _ = await GetReservationInternalAsync(dbContext, reservationId, cancellationToken);

            var entry = new FolioEntry
            {
                Id = Guid.NewGuid(),
                ReservationId = reservationId,
                Description = request.Description.Trim(),
                EntryType = request.EntryType,
                Quantity = request.Quantity,
                UnitNetAmount = RoundCurrency(request.UnitNetAmount),
                VatRatePercent = request.VatRatePercent,
                ServiceDate = request.ServiceDate,
                Currency = BillingCurrency,
                TaxExemptionReason = NormalizeOptionalText(request.TaxExemptionReason)
            };

            dbContext.Set<FolioEntry>().Add(entry);
            await dbContext.SaveChangesAsync(cancellationToken);

            return await GetFolioSnapshotInternalAsync(dbContext, reservationId, cancellationToken);
        }, cancellationToken);
    }

    public async Task<BillingFolioSnapshot> RegisterPaymentAsync(
        Guid reservationId,
        BillingRegisterPaymentRequest request,
        CancellationToken cancellationToken)
    {
        ValidatePaymentRequest(request);

        return await ExecuteSerializableAsync(async dbContext =>
        {
            _ = await GetReservationInternalAsync(dbContext, reservationId, cancellationToken);
            var snapshot = await GetFolioSnapshotInternalAsync(dbContext, reservationId, cancellationToken);

            if (snapshot.GrossAmount <= 0)
            {
                throw new BillingValidationException(localizer["At least one charge is required before payment can be posted."]);
            }

            if (request.Amount > snapshot.OpenAmount + 0.01m)
            {
                throw new BillingConflictException(localizer["Payment exceeds the open folio amount."]);
            }

            var payment = new PaymentTransaction
            {
                Id = Guid.NewGuid(),
                ReservationId = reservationId,
                Amount = RoundCurrency(request.Amount),
                Currency = BillingCurrency,
                Method = request.Method,
                PaidAtUtc = request.PaidAtUtc?.ToUniversalTime() ?? DateTimeOffset.UtcNow,
                Reference = NormalizeOptionalText(request.Reference),
                Notes = NormalizeOptionalText(request.Notes)
            };

            dbContext.Set<PaymentTransaction>().Add(payment);
            await dbContext.SaveChangesAsync(cancellationToken);

            return await GetFolioSnapshotInternalAsync(dbContext, reservationId, cancellationToken);
        }, cancellationToken);
    }

    public async Task<BillingInvoiceDocumentDto> IssueInvoiceAsync(
        Guid reservationId,
        BillingIssueInvoiceRequest request,
        CancellationToken cancellationToken)
    {
        ValidateIssueInvoiceRequest(request);

        return await ExecuteSerializableAsync(async dbContext =>
        {
            var reservation = await GetReservationInternalAsync(dbContext, reservationId, cancellationToken);
            var folioSnapshot = await GetFolioSnapshotInternalAsync(dbContext, reservationId, cancellationToken);

            if (folioSnapshot.Entries.Count == 0)
            {
                throw new BillingValidationException(localizer["Cannot issue invoice without folio entries."]);
            }

            var existingInvoice = await dbContext.Set<InvoiceDocument>()
                .AnyAsync(invoice => invoice.ReservationId == reservationId, cancellationToken);
            if (existingInvoice)
            {
                throw new BillingConflictException(localizer["An invoice has already been issued for this reservation."]);
            }

            var invoiceNumber = await GetNextInvoiceNumberAsync(dbContext, cancellationToken);
            var invoice = new InvoiceDocument
            {
                Id = Guid.NewGuid(),
                ReservationId = reservationId,
                InvoiceNumber = invoiceNumber,
                IssuedAtUtc = DateTimeOffset.UtcNow,
                ServiceFrom = reservation.CheckIn,
                ServiceTo = reservation.CheckOut,
                DueDate = request.DueDate,
                Currency = BillingCurrency,
                SellerName = complianceOptions.Value.SellerName,
                SellerAddress = complianceOptions.Value.SellerAddress,
                SellerVatId = complianceOptions.Value.SellerVatId,
                BuyerName = request.BuyerName.Trim(),
                BuyerAddress = request.BuyerAddress.Trim(),
                BuyerVatId = NormalizeOptionalText(request.BuyerVatId),
                NetAmount = RoundCurrency(folioSnapshot.NetAmount),
                VatAmount = RoundCurrency(folioSnapshot.VatAmount),
                GrossAmount = RoundCurrency(folioSnapshot.GrossAmount)
            };

            foreach (var line in folioSnapshot.Entries)
            {
                invoice.Lines.Add(new InvoiceLine
                {
                    Id = Guid.NewGuid(),
                    Description = line.Description,
                    Quantity = line.Quantity,
                    UnitNetAmount = line.UnitNetAmount,
                    VatRatePercent = line.VatRatePercent,
                    NetAmount = line.NetAmount,
                    VatAmount = line.VatAmount,
                    GrossAmount = line.GrossAmount,
                    TaxExemptionReason = line.TaxExemptionReason
                });
            }

            dbContext.Set<InvoiceDocument>().Add(invoice);
            await dbContext.SaveChangesAsync(cancellationToken);

            return MapInvoice(invoice);
        }, cancellationToken);
    }

    public async Task<BillingInvoiceDocumentDto> GetInvoiceAsync(Guid invoiceId, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var invoice = await dbContext.Set<InvoiceDocument>()
            .AsNoTracking()
            .Include(document => document.Lines)
            .Include(document => document.Reservation)
            .SingleOrDefaultAsync(document => document.Id == invoiceId, cancellationToken)
            ?? throw new BillingNotFoundException(localizer["Invoice not found."]);

        var paidAmount = await dbContext.Set<PaymentTransaction>()
            .Where(payment => payment.ReservationId == invoice.ReservationId)
            .SumAsync(payment => payment.Amount, cancellationToken);

        return MapInvoice(invoice);
    }

    private async Task<Reservation> GetReservationInternalAsync(
        HotelDbContext dbContext,
        Guid reservationId,
        CancellationToken cancellationToken)
    {
        return await dbContext.Reservations
            .SingleOrDefaultAsync(reservation => reservation.Id == reservationId, cancellationToken)
            ?? throw new BillingNotFoundException(localizer["Reservation not found."]);
    }

    private async Task<BillingFolioSnapshot> GetFolioSnapshotInternalAsync(
        HotelDbContext dbContext,
        Guid reservationId,
        CancellationToken cancellationToken)
    {
        var reservation = await dbContext.Reservations
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == reservationId, cancellationToken)
            ?? throw new BillingNotFoundException(localizer["Reservation not found."]);

        var entries = await dbContext.Set<FolioEntry>()
            .AsNoTracking()
            .Where(entry => entry.ReservationId == reservationId)
            .OrderBy(entry => entry.ServiceDate)
            .ThenBy(entry => entry.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var payments = await dbContext.Set<PaymentTransaction>()
            .AsNoTracking()
            .Where(payment => payment.ReservationId == reservationId)
            .OrderByDescending(payment => payment.PaidAtUtc)
            .ToListAsync(cancellationToken);

        var invoices = await dbContext.Set<InvoiceDocument>()
            .AsNoTracking()
            .Where(invoice => invoice.ReservationId == reservationId)
            .OrderByDescending(invoice => invoice.IssuedAtUtc)
            .ToListAsync(cancellationToken);

        var mappedEntries = entries.Select(MapFolioEntry).ToList();
        var netAmount = RoundCurrency(mappedEntries.Sum(item => item.NetAmount));
        var vatAmount = RoundCurrency(mappedEntries.Sum(item => item.VatAmount));
        var grossAmount = RoundCurrency(mappedEntries.Sum(item => item.GrossAmount));
        var paidAmount = RoundCurrency(payments.Sum(payment => payment.Amount));
        var openAmount = RoundCurrency(grossAmount - paidAmount);

        return new BillingFolioSnapshot(
            reservation.Id,
            reservation.GuestName,
            reservation.CheckIn,
            reservation.CheckOut,
            netAmount,
            vatAmount,
            grossAmount,
            paidAmount,
            openAmount,
            BillingCurrency,
            mappedEntries,
            payments.Select(payment => new BillingPaymentItem(
                payment.Id,
                payment.Amount,
                payment.Method,
                payment.PaidAtUtc,
                payment.Reference,
                payment.Notes)).ToList(),
            invoices.Select(invoice => new BillingInvoiceSummary(
                invoice.Id,
                invoice.InvoiceNumber,
                invoice.IssuedAtUtc,
                invoice.DueDate,
                invoice.GrossAmount,
                openAmount)).ToList());
    }

    private async Task<string> GetNextInvoiceNumberAsync(HotelDbContext dbContext, CancellationToken cancellationToken)
    {
        var year = DateTime.UtcNow.Year;
        var sequence = await dbContext.Set<InvoiceSequence>()
            .SingleOrDefaultAsync(item => item.Year == year, cancellationToken);

        if (sequence is null)
        {
            sequence = new InvoiceSequence
            {
                Year = year,
                LastNumber = 0
            };
            dbContext.Set<InvoiceSequence>().Add(sequence);
        }

        sequence.LastNumber++;
        return $"AT-{year}-{sequence.LastNumber:D6}";
    }

    private void ValidateFolioEntryRequest(BillingAddFolioEntryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Description))
        {
            throw new BillingValidationException(localizer["Description is required."]);
        }

        if (request.Description.Trim().Length > 250)
        {
            throw new BillingValidationException(localizer["Description cannot exceed 250 characters."]);
        }

        if (request.Quantity <= 0)
        {
            throw new BillingValidationException(localizer["Quantity must be greater than 0."]);
        }

        if (request.UnitNetAmount < 0)
        {
            throw new BillingValidationException(localizer["Unit net amount cannot be negative."]);
        }

        if (!AllowedVatRates.Contains(request.VatRatePercent))
        {
            throw new BillingValidationException(localizer["VAT rate must be one of the supported Austrian rates (0, 10, 13, 20)."]);
        }

        if (request.VatRatePercent == 0m && string.IsNullOrWhiteSpace(request.TaxExemptionReason))
        {
            throw new BillingValidationException(localizer["A tax exemption reason is required for 0% VAT lines."]);
        }
    }

    private void ValidatePaymentRequest(BillingRegisterPaymentRequest request)
    {
        if (request.Amount <= 0)
        {
            throw new BillingValidationException(localizer["Payment amount must be greater than 0."]);
        }

        if (request.Method == PaymentMethod.Cash && request.Amount > complianceOptions.Value.CashPaymentLimitEur)
        {
            throw new BillingValidationException(localizer["Cash payment exceeds the configured AML limit. Use non-cash settlement or split according to policy."]);
        }

        if (request.Reference is not null && request.Reference.Trim().Length > 80)
        {
            throw new BillingValidationException(localizer["Payment reference cannot exceed 80 characters."]);
        }
    }

    private void ValidateIssueInvoiceRequest(BillingIssueInvoiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.BuyerName))
        {
            throw new BillingValidationException(localizer["Buyer name is required on invoices."]);
        }

        if (string.IsNullOrWhiteSpace(request.BuyerAddress))
        {
            throw new BillingValidationException(localizer["Buyer address is required on invoices."]);
        }
    }

    private static BillingFolioEntryItem MapFolioEntry(FolioEntry entry)
    {
        var factor = entry.EntryType == FolioEntryType.Credit ? -1m : 1m;
        var netAmount = RoundCurrency(entry.Quantity * entry.UnitNetAmount) * factor;
        var vatAmount = RoundCurrency(netAmount * entry.VatRatePercent / 100m);
        var grossAmount = RoundCurrency(netAmount + vatAmount);

        return new BillingFolioEntryItem(
            entry.Id,
            entry.Description,
            entry.EntryType,
            entry.Quantity,
            entry.UnitNetAmount,
            entry.VatRatePercent,
            netAmount,
            vatAmount,
            grossAmount,
            entry.ServiceDate,
            entry.TaxExemptionReason);
    }

    private static BillingInvoiceDocumentDto MapInvoice(InvoiceDocument invoice)
    {
        return new BillingInvoiceDocumentDto(
            invoice.Id,
            invoice.InvoiceNumber,
            invoice.IssuedAtUtc,
            invoice.SellerName,
            invoice.SellerAddress,
            invoice.SellerVatId,
            invoice.BuyerName,
            invoice.BuyerAddress,
            invoice.BuyerVatId,
            invoice.ServiceFrom,
            invoice.ServiceTo,
            invoice.DueDate,
            invoice.NetAmount,
            invoice.VatAmount,
            invoice.GrossAmount,
            invoice.Lines
                .OrderBy(line => line.Description)
                .Select(line => new BillingInvoiceLineDto(
                    line.Description,
                    line.Quantity,
                    line.UnitNetAmount,
                    line.VatRatePercent,
                    line.NetAmount,
                    line.VatAmount,
                    line.GrossAmount,
                    line.TaxExemptionReason))
                .ToList());
    }

    private static decimal RoundCurrency(decimal value)
    {
        return decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    }

    private static string? NormalizeOptionalText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private async Task<T> ExecuteSerializableAsync<T>(Func<HotelDbContext, Task<T>> operation, CancellationToken cancellationToken)
    {
        await using var strategyContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var strategy = strategyContext.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var result = await operation(dbContext);
            await transaction.CommitAsync(cancellationToken);
            return result;
        });
    }
}
