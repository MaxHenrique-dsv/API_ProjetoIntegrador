using StravaIntegration.Models.DTOs;
using StravaIntegration.Models.Strava;

namespace StravaIntegration.Services;

public interface IStravaService
{
    string BuildAuthorizationUrl(string state);
    Task<StravaConnectResult> ExchangeCodeAndSaveTokenAsync(string code, Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<StravaActivity>> GetRecentActivitiesAsync(Guid userId, int count = 10, CancellationToken ct = default);
    Task<StravaActivity> GetActivityByIdAsync(Guid userId, long activityId, CancellationToken ct = default);
    
    Task<IReadOnlyList<StravaActivity>> GetActivitiesByDateRangeAsync(
    Guid userId,
    DateTimeOffset from,
    DateTimeOffset to,
    CancellationToken ct = default);
    Task<string> GetAveragePaceOfLastRunsAsync(Guid userId, int runCount = 10, CancellationToken ct = default);
}

