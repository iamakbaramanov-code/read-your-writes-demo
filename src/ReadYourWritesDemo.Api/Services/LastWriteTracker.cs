using Microsoft.Extensions.Caching.Distributed;
using System.Globalization;

namespace ReadYourWritesDemo.Api.Services;

public class LastWriteTracker : ILastWriteTracker
{
    private readonly IDistributedCache _cache;
    private readonly TimeSpan _ttl = TimeSpan.FromMinutes(10);

    public LastWriteTracker(IDistributedCache cache)
    {
        _cache = cache;
    }

    public async Task<DateTime?> GetLastWriteAsync(Guid userId)
    {
        var key = GetKey(userId);
        var val = await _cache.GetStringAsync(key);
        if (val == null) return null;

        return DateTime.Parse(val, null, DateTimeStyles.RoundtripKind);
    }

    public async Task RecordWriteAsync(Guid userId)
    {
        var key = GetKey(userId);
        var val = DateTime.UtcNow.ToString("O");

        await _cache.SetStringAsync(key, val, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = _ttl
        });
    }

    private static string GetKey(Guid userId) => $"user:{userId}:last_write_utc";
}
