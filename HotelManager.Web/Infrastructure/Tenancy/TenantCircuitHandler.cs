using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;

namespace HotelManager.Web.Infrastructure.Tenancy;

/// <summary>Keeps the circuit's <see cref="TenantContext"/> in sync with the Blazor authentication state.</summary>
internal sealed class TenantCircuitHandler(AuthenticationStateProvider authenticationStateProvider, TenantContext tenantContext)
    : CircuitHandler, IDisposable
{
    public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        authenticationStateProvider.AuthenticationStateChanged += OnAuthenticationChanged;
        return Task.CompletedTask;
    }

    public override async Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        var state = await authenticationStateProvider.GetAuthenticationStateAsync();
        tenantContext.SetUser(state.User);
    }

    public void Dispose() => authenticationStateProvider.AuthenticationStateChanged -= OnAuthenticationChanged;

    private void OnAuthenticationChanged(Task<AuthenticationState> stateTask) => _ = UpdateAsync(stateTask);

    private async Task UpdateAsync(Task<AuthenticationState> stateTask)
    {
        try
        {
            tenantContext.SetUser((await stateTask).User);
        }
        catch
        {
            tenantContext.SetUser(null);
        }
    }
}

public static class TenantMiddlewareExtensions
{
    /// <summary>Copies the authenticated HTTP user into the request's <see cref="TenantContext"/>.</summary>
    public static IApplicationBuilder UseTenantContext(this IApplicationBuilder app) =>
        app.Use((httpContext, next) =>
        {
            httpContext.RequestServices.GetRequiredService<TenantContext>().SetUser(httpContext.User);
            return next(httpContext);
        });
}
