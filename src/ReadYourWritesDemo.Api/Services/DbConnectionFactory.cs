using Microsoft.Extensions.Configuration;
using Npgsql;

namespace ReadYourWritesDemo.Api.Services;

public class DbConnectionFactory : IDbConnectionFactory
{
    private readonly string _leaderConn;
    private readonly string _replicaConn;

    public DbConnectionFactory(IConfiguration config)
    {
        _leaderConn  = config.GetConnectionString("PostgresPrimary")
                        ?? throw new InvalidOperationException("PostgresPrimary not configured");
        _replicaConn = config.GetConnectionString("PostgresReplica")
                        ?? throw new InvalidOperationException("PostgresReplica not configured");
    }

    public NpgsqlConnection CreateConnection(DbRole role)
    {
        var cs = role == DbRole.Leader ? _leaderConn : _replicaConn;
        return new NpgsqlConnection(cs);
    }
}
