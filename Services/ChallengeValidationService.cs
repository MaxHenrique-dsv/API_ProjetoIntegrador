using StravaIntegration.Exceptions;
using StravaIntegration.Models.DTOs;
using StravaIntegration.Models.Entities;
using StravaIntegration.Models.Strava;

namespace StravaIntegration.Services;

/// <summary>
/// Orquestra a validação de atividades Strava contra desafios do Supabase
/// e a inserção de registros em reward_history quando concluídos.
/// </summary>
public sealed class ChallengeValidationService : IChallengeValidationService
{
    private readonly IStravaService _stravaService;
    private readonly Supabase.Client _supabase;
    private readonly ILogger<ChallengeValidationService> _logger;

    public ChallengeValidationService(
        IStravaService stravaService,
        Supabase.Client supabase,
        ILogger<ChallengeValidationService> logger)
    {
        _stravaService = stravaService;
        _supabase = supabase;
        _logger = logger;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Sync & Validate: busca N atividades recentes e valida cada uma
    // ──────────────────────────────────────────────────────────────────────────

    public async Task<ChallengeValidationResult> SyncAndValidateAsync(
        SyncActivitiesRequest request,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Iniciando sync para UserId={UserId}, ChallengeId={ChallengeId}",
            request.UserId, request.ChallengeId);

        var challenge = await FetchChallengeAsync(request.ChallengeId, ct);
        var reward    = await FetchRewardAsync(request.ChallengeId, ct);

        // Verifica se o usuário já ganhou este prêmio
        if (await AlreadyRewardedAsync(request.UserId, reward.Id, ct))
        {
            _logger.LogInformation(
                "Usuário {UserId} já possui o prêmio {RewardId}. Ignorando.",
                request.UserId, reward.Id);

            return new ChallengeValidationResult(
                ChallengeCompleted: false,
                RewardHistoryId: null,
                ChallengeTitle: challenge.Title,
                FailureReason: "Prêmio já concedido anteriormente.");
        }

        var activities = await _stravaService.GetRecentActivitiesAsync(
            request.UserId, request.RecentCount, ct);

        // Filtra apenas corridas (Run / TrailRun / VirtualRun)
        var runs = activities
            .Where(a => a.SportType.Contains("Run", StringComparison.OrdinalIgnoreCase))
            .ToList();

        _logger.LogInformation("{RunCount} corridas encontradas nas últimas {Total} atividades.",
            runs.Count, activities.Count);

        foreach (var activity in runs)
        {
            if (!MeetsChallenge(activity, challenge, out var failReason))
            {
                _logger.LogDebug(
                    "Atividade {ActivityId} não cumpre desafio: {Reason}",
                    activity.Id, failReason);
                continue;
            }

            // ✅ Primeiro match → concede o prêmio
            var historyId = await GrantRewardAsync(
                request.UserId, reward.Id, activity.StravaUrl, ct);

            _logger.LogInformation(
                "✅ Prêmio concedido! UserId={UserId}, RewardHistoryId={HistoryId}, ActivityId={ActivityId}",
                request.UserId, historyId, activity.Id);

            return new ChallengeValidationResult(
                ChallengeCompleted: true,
                RewardHistoryId: historyId,
                ChallengeTitle: challenge.Title);
        }

        return new ChallengeValidationResult(
            ChallengeCompleted: false,
            RewardHistoryId: null,
            ChallengeTitle: challenge.Title,
            FailureReason: "Nenhuma atividade recente cumpre os requisitos do desafio.");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Validate Single Activity: valida atividade específica por ID
    // ──────────────────────────────────────────────────────────────────────────

    public async Task<ChallengeValidationResult> ValidateSingleActivityAsync(
        ValidateActivityRequest request,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Validando atividade {ActivityId} para UserId={UserId}, ChallengeId={ChallengeId}",
            request.StravaActivityId, request.UserId, request.ChallengeId);

        var challenge = await FetchChallengeAsync(request.ChallengeId, ct);
        var reward    = await FetchRewardAsync(request.ChallengeId, ct);

        if (await AlreadyRewardedAsync(request.UserId, reward.Id, ct))
        {
            return new ChallengeValidationResult(
                ChallengeCompleted: false,
                RewardHistoryId: null,
                ChallengeTitle: challenge.Title,
                FailureReason: "Prêmio já concedido anteriormente.");
        }

        var activity = await _stravaService.GetActivityByIdAsync(
            request.UserId, request.StravaActivityId, ct);

        if (!MeetsChallenge(activity, challenge, out var failReason))
        {
            return new ChallengeValidationResult(
                ChallengeCompleted: false,
                RewardHistoryId: null,
                ChallengeTitle: challenge.Title,
                FailureReason: failReason);
        }

        var historyId = await GrantRewardAsync(
            request.UserId, reward.Id, activity.StravaUrl, ct);

        return new ChallengeValidationResult(
            ChallengeCompleted: true,
            RewardHistoryId: historyId,
            ChallengeTitle: challenge.Title);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Lógica de Validação – regras por tipo de desafio
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Retorna true se a atividade cumpre o desafio.
    /// </summary>
    private static bool MeetsChallenge(
        StravaActivity activity,
        Challenge challenge,
        out string? failReason)
    {
        failReason = null;

        switch (challenge.ChallengeType.ToLowerInvariant())
        {
            // Desafio de distância mínima em km
            case "distance_km":
                if (activity.DistanceKm >= challenge.TargetValue)
                    return true;

                failReason = $"Distância {activity.DistanceKm:F2} km abaixo de {challenge.TargetValue} km.";
                return false;

            // Desafio de pace máximo (segundos/km) — menor é melhor
            // target_value = pace máximo permitido em seg/km
            case "pace_min_per_km":
                if (activity.PaceSecPerKm <= challenge.TargetValue)
                    return true;

                var actualPace  = TimeSpan.FromSeconds(activity.PaceSecPerKm);
                var targetPace  = TimeSpan.FromSeconds(challenge.TargetValue);
                failReason = $"Pace {actualPace:mm\\:ss}/km acima do limite {targetPace:mm\\:ss}/km.";
                return false;

            // Desafio de ganho de elevação mínimo em metros
            case "elevation_m":
                if (activity.TotalElevationGain >= challenge.TargetValue)
                    return true;

                failReason = $"Elevação {activity.TotalElevationGain:F0} m abaixo de {challenge.TargetValue} m.";
                return false;

            // Desafio de duração mínima em minutos
            case "duration_min":
                var movingMin = activity.MovingTime / 60.0;
                if (movingMin >= challenge.TargetValue)
                    return true;

                failReason = $"Duração {movingMin:F1} min abaixo de {challenge.TargetValue} min.";
                return false;

            default:
                failReason = $"Tipo de desafio desconhecido: '{challenge.ChallengeType}'.";
                return false;
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Supabase – Queries
    // ──────────────────────────────────────────────────────────────────────────

    private async Task<Challenge> FetchChallengeAsync(Guid challengeId, CancellationToken ct)
    {
        var result = await _supabase
            .From<Challenge>()
            .Where(c => c.Id == challengeId)
            .Single();

        return result ?? throw new ChallengeNotFoundException(challengeId);
    }

    private async Task<Reward> FetchRewardAsync(Guid challengeId, CancellationToken ct)
    {
        var result = await _supabase
            .From<Reward>()
            .Where(r => r.ChallengeId == challengeId)
            .Single();

        return result ?? throw new RewardNotFoundException(challengeId);
    }

    private async Task<bool> AlreadyRewardedAsync(Guid userId, Guid rewardId, CancellationToken ct)
    {
        var existing = await _supabase
            .From<RewardHistory>()
            .Where(h => h.UserId == userId && h.RewardId == rewardId)
            .Single();

        return existing is not null;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Supabase – Inserção em reward_history
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Insere o registro de recompensa na tabela reward_history do Supabase.
    /// </summary>
    private async Task<Guid> GrantRewardAsync(
        Guid userId,
        Guid rewardId,
        string stravaActivityUrl,
        CancellationToken ct)
    {
        var historyRecord = new RewardHistory
        {
            Id        = Guid.NewGuid(),
            RewardId  = rewardId,
            UserId    = userId,
            EarnedAt  = DateTimeOffset.UtcNow,
            ProofType = "strava_sync",
            ProofUrl  = stravaActivityUrl
        };

        var response = await _supabase
            .From<RewardHistory>()
            .Insert(historyRecord);

        var inserted = response.Models.FirstOrDefault()
            ?? throw new InvalidOperationException("Falha ao inserir reward_history no Supabase.");

        return inserted.Id;
    }
}
