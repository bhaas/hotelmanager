namespace HotelManager.Web.Features.Billing;

public static class BillingApi
{
    public static IEndpointRouteBuilder MapBillingApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/billing")
            .WithTags("Billing");

        group.MapGet("/reservations/{reservationId:guid}/folio", async (
            Guid reservationId,
            IBillingService billingService,
            CancellationToken cancellationToken) =>
        {
            return await ExecuteBillingOperationAsync(() => billingService.GetFolioAsync(reservationId, cancellationToken));
        });

        group.MapPost("/reservations/{reservationId:guid}/entries", async (
            Guid reservationId,
            BillingAddFolioEntryRequest request,
            IBillingService billingService,
            CancellationToken cancellationToken) =>
        {
            return await ExecuteBillingOperationAsync(() => billingService.AddFolioEntryAsync(reservationId, request, cancellationToken));
        });

        group.MapPost("/reservations/{reservationId:guid}/payments", async (
            Guid reservationId,
            BillingRegisterPaymentRequest request,
            IBillingService billingService,
            CancellationToken cancellationToken) =>
        {
            return await ExecuteBillingOperationAsync(() => billingService.RegisterPaymentAsync(reservationId, request, cancellationToken));
        });

        group.MapPost("/reservations/{reservationId:guid}/issue-invoice", async (
            Guid reservationId,
            BillingIssueInvoiceRequest request,
            IBillingService billingService,
            CancellationToken cancellationToken) =>
        {
            return await ExecuteBillingOperationAsync(() => billingService.IssueInvoiceAsync(reservationId, request, cancellationToken));
        });

        group.MapGet("/invoices/{invoiceId:guid}", async (
            Guid invoiceId,
            IBillingService billingService,
            CancellationToken cancellationToken) =>
        {
            return await ExecuteBillingOperationAsync(() => billingService.GetInvoiceAsync(invoiceId, cancellationToken));
        });

        group.MapGet("/invoices/{invoiceId:guid}/pdf", async (
            Guid invoiceId,
            bool? download,
            IBillingService billingService,
            IInvoicePdfService invoicePdfService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var invoice = await billingService.GetInvoiceAsync(invoiceId, cancellationToken);
                var pdfBytes = invoicePdfService.Generate(invoice);
                return download == true
                    ? Results.File(pdfBytes, "application/pdf", $"{invoice.InvoiceNumber}.pdf")
                    : Results.File(pdfBytes, "application/pdf");
            }
            catch (BillingValidationException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
            catch (BillingConflictException exception)
            {
                return Results.Conflict(new { error = exception.Message });
            }
            catch (BillingNotFoundException exception)
            {
                return Results.NotFound(new { error = exception.Message });
            }
        });

        return app;
    }

    private static async Task<IResult> ExecuteBillingOperationAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return Results.Ok(await operation());
        }
        catch (BillingValidationException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
        catch (BillingConflictException exception)
        {
            return Results.Conflict(new { error = exception.Message });
        }
        catch (BillingNotFoundException exception)
        {
            return Results.NotFound(new { error = exception.Message });
        }
    }
}
