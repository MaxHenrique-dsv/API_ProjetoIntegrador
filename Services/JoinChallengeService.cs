using StravaIntegration.Exceptions;
using StravaIntegration.Models.Entities;

namespace StravaIntegration.Services;

public interface IJoinChallengeService
{
    /// <summary>
    /// Chamado quando o usuário clica em "Participar" no frontend.
    /// Busca todas as corridas do período do desafio no Strava
    /// e salva na tabela user_activities do Supabase.
    /// Retorna o resumo das atividades salvas.
    /// </summary>
    Task<JoinChallengeResult> JoinAndSyncActivitiesAsync(
        Guid userId,
        Guid challengeId,
        CancellationToken ct = default);
}

public sealed record JoinChallengeResult(
    Guid ChallengeId,
    string ChallengeTitle,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    int ActivitiesSynced,
    IReadOnlyList<SyncedActivitySummary> Activities
);

public sealed record SyncedActivitySummary(
    long StravaId,
    string Name,
    double DistanceKm,
    double MovingTimeMinutes,
    DateTimeOffset StartDate,
    string StravaUrl
);

// ─────────────────────────────────────────────────────────────────────────────

public sealed class JoinChallengeService : IJoinChallengeService
{
    private readonly IStravaService _stravaService;
    private readonly Supabase.Client _supabase;
    private readonly ILogger<JoinChallengeService> _logger;

    public JoinChallengeService(
        IStravaService stravaService,
        Supabase.Client supabase,
        ILogger<JoinChallengeService> logger)
    {
        _stravaService = stravaService;
        _supabase      = supabase;
        _logger        = logger;
    }

    public async Task<JoinChallengeResult> JoinAndSyncActivitiesAsync(
        Guid userId,
        Guid challengeId,
        CancellationToken ct = default)
    {
        // ── 1. Busca o desafio para obter as datas do período ─────────────────
        var challenge = await _supabase
            .From<Challenge>()
            .Where(c => c.Id == challengeId)
            .Single()
            ?? throw new ChallengeNotFoundException(challengeId);

        _logger.LogInformation(
            "Participando do desafio '{Title}' ({ChallengeId}). " +
            "Período: {Start:dd/MM/yyyy} → {End:dd/MM/yyyy}. UserId={UserId}",
            challenge.Title, challengeId,
            challenge.StartDate, challenge.EndDate, userId);

        // ── 2. Busca todas as corridas do período no Strava ───────────────────
        var stravaActivities = await _stravaService.GetActivitiesByDateRangeAsync(
            userId,
            challenge.StartDate,
            challenge.EndDate,
            ct);

        if (stravaActivities.Count == 0)
        {
            _logger.LogInformation(
                "Nenhuma corrida encontrada no período para userId={UserId}", userId);

            return new JoinChallengeResult(
                ChallengeId:      challengeId,
                ChallengeTitle:   challenge.Title,
                PeriodStart:      challenge.StartDate,
                PeriodEnd:        challenge.EndDate,
                ActivitiesSynced: 0,
                Activities:       []);
        }

        // ── 3. Remove atividades já salvas (evita duplicatas no upsert) ───────
        var existingIds = await GetExistingActivityIdsAsync(userId, challengeId, ct);

        var newActivities = stravaActivities
            .Where(a => !existingIds.Contains(a.Id))
            .ToList();

        _logger.LogInformation(
            "{New} novas corridas para inserir ({Existing} já existentes).",
            newActivities.Count, existingIds.Count);

        // ── 4. Monta os registros e faz upsert no Supabase ────────────────────
        var records = newActivities.Select(a => new UserActivity
        {
            Id                 = a.Id,
            UserId             = userId,
            ChallengeId        = challengeId,
            Name               = a.Name,
            DistanceKm         = Math.Round(a.DistanceKm, 2),
            MovingTimeMinutes  = Math.Round(a.MovingTime / 60.0, 1),
            StartDate          = a.StartDate,
        }).ToList();

        if (records.Count > 0)
        {
            await _supabase
                .From<UserActivity>()
                .Upsert(records);

            _logger.LogInformation(
                "✅ {Count} atividades salvas em user_activities para userId={UserId}",
                records.Count, userId);
        }

        // ── 5. Monta o resultado com todas as atividades do período ───────────
        // Retorna todas (novas + já existentes) para o frontend exibir
        var allSynced = stravaActivities
            .Select(a => new SyncedActivitySummary(
                StravaId:          a.Id,
                Name:              a.Name,
                DistanceKm:        Math.Round(a.DistanceKm, 2),
                MovingTimeMinutes: Math.Round(a.MovingTime / 60.0, 1),
                StartDate:         a.StartDate,
                StravaUrl:         a.StravaUrl))
            .OrderByDescending(a => a.StartDate)
            .ToList();

        return new JoinChallengeResult(
            ChallengeId:      challengeId,
            ChallengeTitle:   challenge.Title,
            PeriodStart:      challenge.StartDate,
            PeriodEnd:        challenge.EndDate,
            ActivitiesSynced: records.Count,
            Activities:       allSynced);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Retorna os IDs das atividades já salvas para evitar duplicatas
    // ─────────────────────────────────────────────────────────────────────────
    private async Task<HashSet<long>> GetExistingActivityIdsAsync(
        Guid userId,
        Guid challengeId,
        CancellationToken ct)
    {
        var existing = await _supabase
            .From<UserActivity>()
            .Where(a => a.UserId == userId && a.ChallengeId == challengeId)
            .Get();

        return existing.Models
            .Select(a => a.Id)
            .ToHashSet();
    }
}