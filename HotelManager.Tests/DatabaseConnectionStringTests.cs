using HotelManager.Web.Infrastructure.Hosting;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace HotelManager.Tests;

public sealed class DatabaseConnectionStringTests
{
    [Fact]
    public void Neon_url_is_converted_to_npgsql_settings()
    {
        var result = new NpgsqlConnectionStringBuilder(DatabaseConnectionString.Normalize(
            "postgresql://neondb_owner:p%40ss@ep-cool-1.eu-central-1.aws.neon.tech/neondb?sslmode=require&channel_binding=require"));

        Assert.Equal("ep-cool-1.eu-central-1.aws.neon.tech", result.Host);
        Assert.Equal(5432, result.Port);
        Assert.Equal("neondb", result.Database);
        Assert.Equal("neondb_owner", result.Username);
        Assert.Equal("p@ss", result.Password);
        Assert.Equal(SslMode.Require, result.SslMode);
        Assert.Equal(ChannelBinding.Require, result.ChannelBinding);
    }

    [Fact]
    public void Classic_connection_string_is_kept_and_explicit_setting_wins()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DATABASE_URL"] = "postgres://u:p@url-host:6543/db",
            ["ConnectionStrings:HotelDb"] = "Host=explicit;Database=hm"
        }).Build();

        Assert.Equal("Host=explicit;Database=hm", DatabaseConnectionString.Resolve(configuration));
        Assert.Throws<InvalidOperationException>(() => DatabaseConnectionString.Resolve(new ConfigurationBuilder().Build()));
    }
}
