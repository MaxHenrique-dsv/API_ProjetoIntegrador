using StravaIntegration.Models.Entities;
using StravaIntegration.Models.Strava;
using StravaIntegration.Services;

namespace StravaIntegration.Models.DTOs;

// ─── Requests ─────────────────────────────────────────────────────────────────

public sealed record SyncActivitiesRequest(
    Guid UserId,
    Guid ChallengeId,
    int RecentCount = 10
);

public sealed record ValidateActivityRequest(
    Guid UserId,
    Guid ChallengeId,
    long StravaActivityId
);

// ─── ChallengeValidationResult ────────────────────────────────────────────────

/// <summary>
/// Resultado completo da validação de um desafio.
/// Contém o que foi exigido, o que o usuário alcançou e se o prêmio foi liberado.
/// </summary>
public sealed class ChallengeValidationResult
{
    // ── Status geral ──────────────────────────────────────────────────────────
    public bool   ChallengeCompleted { get; init; }
    public Guid?  RewardHistoryId    { get; init; }
    public string ChallengeTitle     { get; init; } = string.Empty;
    public string ChallengeType      { get; init; } = string.Empty;
    public string Message            { get; init; } = string.Empty;
    public string? FailureReason     { get; init; }

    // ── Requisito do desafio ──────────────────────────────────────────────────

    /// <summary>Distância mínima exigida (type = "corrida").</summary>
    public double? RequiredDistanceKm { get; init; }

    /// <summary>Pace máximo exigido em min/km — ex: 6.5 = 6:30/km (type = "pace").</summary>
    public double? RequiredPaceMinPerKm { get; init; }

    /// <summary>Pace máximo exigido formatado — ex: "6:30 /km".</summary>
    public string? RequiredPaceFormatted { get; init; }

    // ── O que a atividade vencedora / melhor tentativa alcançou ───────────────

    public long?   ActivityStravaId          { get; init; }
    public string? ActivityName              { get; init; }
    public double? ActivityDistanceKm        { get; init; }
    public double? ActivityPaceMinPerKm      { get; init; }
    public string? ActivityPaceFormatted     { get; init; }
    public double? ActivityMovingTimeMinutes { get; init; }
    public string? ActivityStravaUrl         { get; init; }

    /// <summary>% de progresso em relação à meta (0–100).</summary>
    public int ProgressPercent { get; init; }

    /// <summary>Quantas corridas foram avaliadas no sync.</summary>
    public int TotalRunsChecked { get; init; }

    // ─── Factory Methods ──────────────────────────────────────────────────────

    public static ChallengeValidationResult Success(
        Challenge challenge,
        Guid rewardHistoryId,
        StravaActivity winningActivity,
        ActivityValidationDetail winningDetail) => new()
    {
        ChallengeCompleted       = true,
        RewardHistoryId          = rewardHistoryId,
        ChallengeTitle           = challenge.Title,
        ChallengeType            = challenge.ChallengeType,
        Message                  = $"🏆 Parabéns! Desafio '{challenge.Title}' concluído!",

        RequiredDistanceKm       = winningDetail.RequiredDistanceKm,
        RequiredPaceMinPerKm     = winningDetail.RequiredPaceMinPerKm,
        RequiredPaceFormatted    = winningDetail.RequiredPaceFormatted,

        ActivityStravaId         = winningActivity.Id,
        ActivityName             = winningActivity.Name,
        ActivityDistanceKm       = winningDetail.ActualDistanceKm,
        ActivityPaceMinPerKm     = winningDetail.ActualPaceMinPerKm,
        ActivityPaceFormatted    = winningDetail.ActualPaceFormatted,
        ActivityMovingTimeMinutes = winningDetail.ActualMovingTimeMinutes,
        ActivityStravaUrl        = winningActivity.StravaUrl,
        ProgressPercent          = 100,
        TotalRunsChecked         = 1
    };

    public static ChallengeValidationResult Failed(
        Challenge challenge,
        StravaActivity? bestActivity,
        ActivityValidationDetail? bestDetail,
        int totalRunsChecked) => new()
    {
        ChallengeCompleted       = false,
        ChallengeTitle           = challenge.Title,
        ChallengeType            = challenge.ChallengeType,
        FailureReason            = bestDetail?.FailureReason
                                   ?? "Nenhuma corrida encontrada no período do desafio.",
        Message                  = "Desafio ainda não concluído.",

        RequiredDistanceKm       = bestDetail?.RequiredDistanceKm,
        RequiredPaceMinPerKm     = bestDetail?.RequiredPaceMinPerKm,
        RequiredPaceFormatted    = bestDetail?.RequiredPaceFormatted,

        ActivityStravaId         = bestActivity?.Id,
        ActivityName             = bestActivity?.Name,
        ActivityDistanceKm       = bestDetail?.ActualDistanceKm,
        ActivityPaceMinPerKm     = bestDetail?.ActualPaceMinPerKm,
        ActivityPaceFormatted    = bestDetail?.ActualPaceFormatted,
        ActivityMovingTimeMinutes = bestDetail?.ActualMovingTimeMinutes,
        ActivityStravaUrl        = bestActivity?.StravaUrl,
        ProgressPercent          = bestDetail?.ProgressPercent ?? 0,
        TotalRunsChecked         = totalRunsChecked
    };

    public static ChallengeValidationResult AlreadyRewarded(string challengeTitle) => new()
    {
        ChallengeCompleted = false,
        ChallengeTitle     = challengeTitle,
        FailureReason      = "Prêmio já concedido anteriormente.",
        Message            = "Você já completou este desafio!",
        ProgressPercent    = 100
    };
}

// ─── Outros DTOs ──────────────────────────────────────────────────────────────

public sealed record StravaConnectResult(
    bool Success,
    Guid UserId,
    long StravaAthleteId,
    string? ErrorMessage = null
);

public sealed record ActivitySummary(
    long StravaId,
    string Name,
    double DistanceKm,
    double PaceMinPerKm,
    string PaceFormatted,
    double ElevationGainM,
    double MovingTimeMinutes,
    DateTimeOffset StartDate,
    string SportType,
    string StravaUrl
);
