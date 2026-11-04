using Npgsql;

namespace HotelManager.Web.Infrastructure.Hosting;

/// <summary>
/// Resolves the PostgreSQL connection string from configuration. Accepts classic Npgsql strings
/// (<c>Host=...;Username=...</c>) and the URL form used by Neon, Vercel, Render and Heroku
/// (<c>postgresql://user:password@host/db?sslmode=require</c>).
/// </summary>
public static class DatabaseConnectionString
{
    /// <summary>Configuration keys checked in order; the first non-empty value wins.</summary>
    public static readonly IReadOnlyList<string> ConfigurationKeys =
    [
        "ConnectionStrings:HotelDb",
        "HOTEL_DB_CONNECTION",
        "DATABASE_URL",
        "POSTGRES_URL"
    ];

    public static string Resolve(IConfiguration configuration)
    {
        foreach (var key in ConfigurationKeys)
        {
            var value = configuration[key];
            if (!string.IsNullOrWhiteSpace(value))
            {
                return Normalize(value);
            }
        }

        throw new InvalidOperationException(
            $"Database connection string missing. Set one of: {string.Join(", ", ConfigurationKeys)}.");
    }

    public static string Normalize(string value)
    {
        value = value.Trim();
        if (!value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) &&
            !value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        var uri = new Uri(value);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort || uri.Port <= 0 ? 5432 : uri.Port,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'))
        };

        var userInfo = uri.UserInfo.Split(':', 2);
        if (userInfo[0].Length > 0)
        {
            builder.Username = Uri.UnescapeDataString(userInfo[0]);
        }

        if (userInfo.Length == 2)
        {
            builder.Password = Uri.UnescapeDataString(userInfo[1]);
        }

        foreach (var (key, parameterValue) in ParseQuery(uri.Query))
        {
            switch (key.ToLowerInvariant())
            {
                case "sslmode":
                    builder.SslMode = parameterValue.ToLowerInvariant() switch
                    {
                        "disable" => SslMode.Disable,
                        "allow" => SslMode.Allow,
                        "prefer" => SslMode.Prefer,
                        "verify-ca" => SslMode.VerifyCA,
                        "verify-full" => SslMode.VerifyFull,
                        _ => SslMode.Require
                    };
                    break;
                case "channel_binding":
                    builder.ChannelBinding = parameterValue.ToLowerInvariant() switch
                    {
                        "disable" => ChannelBinding.Disable,
                        "require" => ChannelBinding.Require,
                        _ => ChannelBinding.Prefer
                    };
                    break;
                case "application_name":
                    builder.ApplicationName = parameterValue;
                    break;
                case "connect_timeout":
                    if (int.TryParse(parameterValue, out var timeout))
                    {
                        builder.Timeout = timeout;
                    }

                    break;
            }
        }

        return builder.ConnectionString;
    }

    private static IEnumerable<(string Key, string Value)> ParseQuery(string query)
    {
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            yield return (Uri.UnescapeDataString(pair[0]), pair.Length == 2 ? Uri.UnescapeDataString(pair[1]) : string.Empty);
        }
    }
}
