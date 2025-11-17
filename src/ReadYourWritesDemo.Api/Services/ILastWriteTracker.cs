namespace ReadYourWritesDemo.Api.Services;

public interface ILastWriteTracker
{
    Task<DateTime?> GetLastWriteAsync(Guid userId);
    Task RecordWriteAsync(Guid userId);
}
