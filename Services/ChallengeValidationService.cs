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
        _supabase      = supabase;
        _logger        = logger;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // SyncAndValidate — busca atividades recentes e valida contra o desafio
    // ──────────────────────────────────────────────────────────────────────────

    public async Task<ChallengeValidationResult> SyncAndValidateAsync(
        SyncActivitiesRequest request,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Iniciando sync. UserId={UserId}, ChallengeId={ChallengeId}",
            request.UserId, request.ChallengeId);

        var challenge = await FetchChallengeAsync(request.ChallengeId, ct);
        var reward    = await FetchRewardAsync(request.ChallengeId, ct);

        _logger.LogInformation(
            "Desafio: '{Title}' | Tipo: {Type} | Meta: {Target} | Período: {Start:dd/MM} → {End:dd/MM}",
            challenge.Title, challenge.ChallengeType, challenge.TargetValue,
            challenge.StartDate, challenge.EndDate);

        // Verifica duplicidade antes de chamar o Strava
        if (await AlreadyRewardedAsync(request.UserId, reward.Id, ct))
        {
            _logger.LogInformation("Usuário {UserId} já possui o prêmio {RewardId}.", request.UserId, reward.Id);
            return ChallengeValidationResult.AlreadyRewarded(challenge.Title);
        }

        var activities = await _stravaService.GetRecentActivitiesAsync(
            request.UserId, request.RecentCount, ct);

        var runs = activities
            .Where(a => a.SportType.Contains("Run", StringComparison.OrdinalIgnoreCase))
            .ToList();

        _logger.LogInformation("{Runs} corridas nas últimas {Total} atividades.", runs.Count, activities.Count);

        // ── Valida cada corrida e registra detalhes de todas ─────────────────
        var allDetails = new List<(StravaActivity Activity, ActivityValidationDetail Detail)>();

        foreach (var run in runs)
        {
            var detail = ChallengeValidator.Validate(run, challenge);
            allDetails.Add((run, detail));

            _logger.LogDebug(
                "Atividade {Id} ({Name}): passou={Passed} | " +
                "distância={Dist:F2}km | pace={Pace}/km | progresso={Progress}%",
                run.Id, run.Name, detail.Passed,
                detail.ActualDistanceKm, detail.ActualPaceFormatted, detail.ProgressPercent);
                await SaveActivityToDatabaseAsync(request.UserId, challenge.Id, run);
            if (!detail.Passed) continue;

            // ✅ Primeira atividade aprovada → concede o prêmio
            var historyId = await GrantRewardAsync(request.UserId, reward.Id, run.StravaUrl, ct);

            _logger.LogInformation(
                "✅ Prêmio concedido! UserId={UserId} | RewardHistoryId={HId} | ActivityId={AId}",
                request.UserId, historyId, run.Id);

            return ChallengeValidationResult.Success(
                challenge: challenge,
                rewardHistoryId: historyId,
                winningActivity: run,
                winningDetail: detail);
        }

        // Nenhuma atividade passou — retorna detalhes da melhor tentativa
        var best = allDetails
            .OrderByDescending(x => x.Detail.ProgressPercent)
            .FirstOrDefault();

        return ChallengeValidationResult.Failed(
            challenge: challenge,
            bestActivity: best.Activity,
            bestDetail: best.Detail,
            totalRunsChecked: runs.Count);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // ValidateSingleActivity — valida uma atividade específica por ID
    // ──────────────────────────────────────────────────────────────────────────

    public async Task<ChallengeValidationResult> ValidateSingleActivityAsync(
        ValidateActivityRequest request,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Validando atividade {ActivityId}. UserId={UserId}, ChallengeId={ChallengeId}",
            request.StravaActivityId, request.UserId, request.ChallengeId);

        var challenge = await FetchChallengeAsync(request.ChallengeId, ct);
        var reward    = await FetchRewardAsync(request.ChallengeId, ct);

        if (await AlreadyRewardedAsync(request.UserId, reward.Id, ct))
            return ChallengeValidationResult.AlreadyRewarded(challenge.Title);

        var activity = await _stravaService.GetActivityByIdAsync(
            request.UserId, request.StravaActivityId, ct);

        var detail = ChallengeValidator.Validate(activity, challenge);

        _logger.LogInformation(
            "Resultado: passou={Passed} | dist={Dist:F2}km | pace={Pace}/km | progresso={Progress}%",
            detail.Passed, detail.ActualDistanceKm, detail.ActualPaceFormatted, detail.ProgressPercent);
            await SaveActivityToDatabaseAsync(request.UserId, challenge.Id, activity);
        if (!detail.Passed)
            return ChallengeValidationResult.Failed(challenge, activity, detail, totalRunsChecked: 1);

        var historyId = await GrantRewardAsync(request.UserId, reward.Id, activity.StravaUrl, ct);

        return ChallengeValidationResult.Success(challenge, historyId, activity, detail);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Supabase helpers
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

    private async Task<Guid> GrantRewardAsync(
        Guid userId, Guid rewardId, string stravaActivityUrl, CancellationToken ct)
    {
        var record = new RewardHistory
        {
            Id        = Guid.NewGuid(),
            RewardId  = rewardId,
            UserId    = userId,
            EarnedAt  = DateTimeOffset.UtcNow,
            ProofType = "strava_sync",
            ProofUrl  = stravaActivityUrl
        };

        var response = await _supabase.From<RewardHistory>().Insert(record);

        return response.Models.FirstOrDefault()?.Id
            ?? throw new InvalidOperationException("Falha ao inserir reward_history no Supabase.");
    }
    private async Task SaveActivityToDatabaseAsync(Guid userId, Guid challengeId, StravaActivity activity)
{
    var record = new UserActivity
    {
        Id = activity.Id, 
        UserId = userId,
        ChallengeId = challengeId,
        Name = activity.Name,
        DistanceKm = activity.Distance / 1000.0,
        MovingTimeMinutes = activity.MovingTime / 60.0,
        StartDate = activity.StartDate
    };
    await _supabase.From<UserActivity>().Upsert(record);
}
}

