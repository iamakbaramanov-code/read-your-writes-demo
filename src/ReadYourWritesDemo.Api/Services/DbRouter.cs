using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace ReadYourWritesDemo.Api.Services;

public class DbRouter
{
    private readonly IDbConnectionFactory _factory;
    private readonly ILastWriteTracker _lastWriteTracker;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TimeSpan _leaderWindow;

    public DbRouter(
        IDbConnectionFactory factory,
        ILastWriteTracker lastWriteTracker,
        IHttpContextAccessor httpContextAccessor,
        IConfiguration configuration)
    {
        _factory = factory;
        _lastWriteTracker = lastWriteTracker;
        _httpContextAccessor = httpContextAccessor;

        var seconds = configuration.GetValue<int?>("ReadYourWrites:LeaderWindowSeconds") ?? 5;
        _leaderWindow = TimeSpan.FromSeconds(seconds);
    }

    public NpgsqlConnection GetConnectionForWrite()
        => _factory.CreateConnection(DbRole.Leader);

    public async Task<NpgsqlConnection> GetConnectionForReadAsync(bool requiresReadYourWrites)
    {
        if (!requiresReadYourWrites)
        {
            return _factory.CreateConnection(DbRole.Follower);
        }

        var userId = GetCurrentUserId();
        if (userId == null)
        {
            return _factory.CreateConnection(DbRole.Follower);
        }

        var lastWrite = await _lastWriteTracker.GetLastWriteAsync(userId.Value);
        if (lastWrite == null)
        {
            return _factory.CreateConnection(DbRole.Follower);
        }

        var now = DateTime.UtcNow;
        if (now - lastWrite < _leaderWindow)
        {
            // recent write -> ensure read-your-writes
            return _factory.CreateConnection(DbRole.Leader);
        }

        return _factory.CreateConnection(DbRole.Follower);
    }

    private Guid? GetCurrentUserId()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null) return null;

        var user = httpContext.User;
        var idClaim = user.FindFirst("sub") ?? user.FindFirst(ClaimTypes.NameIdentifier);

        if (idClaim == null) return null;

        return Guid.TryParse(idClaim.Value, out var id) ? id : null;
    }
}
