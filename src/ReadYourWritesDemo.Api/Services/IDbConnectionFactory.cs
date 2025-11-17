using Npgsql;

namespace ReadYourWritesDemo.Api.Services;

public enum DbRole
{
    Leader,
    Follower
}

public interface IDbConnectionFactory
{
    NpgsqlConnection CreateConnection(DbRole role);
}
