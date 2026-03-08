namespace StravaIntegration.Models.DTOs;

// ─── Requests ─────────────────────────────────────────────────────────────────

public sealed record SyncActivitiesRequest(
    Guid UserId,
    Guid ChallengeId,
    /// <summary>Quantas atividades recentes buscar (máx 30).</summary>
    int RecentCount = 10
);

public sealed record ValidateActivityRequest(
    Guid UserId,
    Guid ChallengeId,
    long StravaActivityId
);

// ─── Responses ────────────────────────────────────────────────────────────────

public sealed record StravaConnectResult(
    bool Success,
    Guid UserId,
    long StravaAthleteId,
    string? ErrorMessage = null
);

public sealed record ChallengeValidationResult(
    bool ChallengeCompleted,
    Guid? RewardHistoryId,
    string? ChallengeTitle,
    string? FailureReason = null
);

public sealed record ActivitySummary(
    long StravaId,
    string Name,
    double DistanceKm,
    double PaceSecPerKm,
    double ElevationGainM,
    int MovingTimeSeconds,
    DateTimeOffset StartDate,
    string SportType,
    string StravaUrl
);
