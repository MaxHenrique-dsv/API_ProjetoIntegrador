using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using StravaIntegration.Exceptions;
using StravaIntegration.Models.DTOs;
using StravaIntegration.Models.Entities;
using StravaIntegration.Models.Options;
using StravaIntegration.Models.Strava;

namespace StravaIntegration.Services;

public sealed class StravaService : IStravaService
{
    private readonly HttpClient _httpClient;
    private readonly StravaOptions _options;
    private readonly Supabase.Client _supabase;
    private readonly ILogger<StravaService> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public StravaService(
        HttpClient httpClient,
        IOptions<StravaOptions> options,
        Supabase.Client supabase,
        ILogger<StravaService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _supabase = supabase;
        _logger = logger;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // OAuth – Etapa 1: Gera URL de Autorização
    // ──────────────────────────────────────────────────────────────────────────

    public string BuildAuthorizationUrl(string state)
    {
        // O `state` carrega o user_id do Supabase para referenciar após o callback.
        var query = new Dictionary<string, string>
        {
            ["client_id"]       = _options.ClientId,
            ["redirect_uri"]    = _options.RedirectUri,
            ["response_type"]   = "code",
            ["approval_prompt"] = _options.ApprovalPrompt,
            ["scope"]           = _options.Scope,
            ["state"]           = state
        };

        var qs = string.Join("&", query.Select(kv =>
            $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));

        return $"{_options.AuthorizeUrl}?{qs}";
    }

    // ──────────────────────────────────────────────────────────────────────────
    // OAuth – Etapa 2: Troca o code pelo token e persiste
    // ──────────────────────────────────────────────────────────────────────────

    public async Task<StravaConnectResult> ExchangeCodeAndSaveTokenAsync(
        string code,
        Guid userId,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Trocando authorization code Strava para userId={UserId}", userId);

        var formContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"]     = _options.ClientId,
            ["client_secret"] = _options.ClientSecret,
            ["code"]          = code,
            ["grant_type"]    = "authorization_code"
        });

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsync(_options.TokenUrl, formContent, ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Falha na requisição ao endpoint de token do Strava");
            throw new StravaAuthException("Erro de rede ao comunicar com Strava.", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Strava token exchange falhou: {Status} – {Body}",
                response.StatusCode, errorBody);
            throw new StravaAuthException(
                $"Strava recusou o code: HTTP {(int)response.StatusCode}");
        }

        var tokenResponse = await response.Content.ReadFromJsonAsync<StravaTokenResponse>(
            JsonOpts, ct)
            ?? throw new StravaAuthException("Resposta de token do Strava veio nula.");

        await PersistTokenAsync(userId, tokenResponse, ct);

        _logger.LogInformation(
            "Token Strava salvo. AthleteId={AthleteId}, UserId={UserId}",
            tokenResponse.Athlete?.Id, userId);

        return new StravaConnectResult(
            Success: true,
            UserId: userId,
            StravaAthleteId: tokenResponse.Athlete?.Id ?? 0);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Atividades Recentes
    // ──────────────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<StravaActivity>> GetRecentActivitiesAsync(
        Guid userId,
        int count = 10,
        CancellationToken ct = default)
    {
        var accessToken = await GetValidAccessTokenAsync(userId, ct);

        count = Math.Clamp(count, 1, 30);
        var url = $"{_options.ApiBaseUrl}/athlete/activities?per_page={count}&page=1";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _httpClient.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Strava /athlete/activities falhou: {Status} – {Body}",
                response.StatusCode, body);
            throw new StravaApiException(
                $"Erro ao buscar atividades: HTTP {(int)response.StatusCode}",
                (int)response.StatusCode);
        }

        var activities = await response.Content.ReadFromJsonAsync<List<StravaActivity>>(
            JsonOpts, ct) ?? [];

        _logger.LogInformation("{Count} atividades recuperadas para userId={UserId}",
            activities.Count, userId);

        return activities;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Atividade Específica
    // ──────────────────────────────────────────────────────────────────────────

    public async Task<StravaActivity> GetActivityByIdAsync(
        Guid userId,
        long activityId,
        CancellationToken ct = default)
    {
        var accessToken = await GetValidAccessTokenAsync(userId, ct);

        var url = $"{_options.ApiBaseUrl}/activities/{activityId}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _httpClient.SendAsync(request, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new StravaApiException($"Atividade {activityId} não encontrada no Strava.", 404);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new StravaApiException(
                $"Erro ao buscar atividade {activityId}: HTTP {(int)response.StatusCode}",
                (int)response.StatusCode);
        }

        return await response.Content.ReadFromJsonAsync<StravaActivity>(JsonOpts, ct)
            ?? throw new StravaApiException("Resposta de atividade veio nula.");
    }
public async Task<IReadOnlyList<StravaActivity>> GetActivitiesByDateRangeAsync(
    Guid userId,
    DateTimeOffset from,
    DateTimeOffset to,
    CancellationToken ct = default)
{
    var accessToken = await GetValidAccessTokenAsync(userId, ct);

    var afterUnix  = from.ToUnixTimeSeconds();
    var beforeUnix = to.ToUnixTimeSeconds();

    var allActivities = new List<StravaActivity>();
    var page = 1;
    const int pageSize = 50;

    while (true)
    {
        var url = $"{_options.ApiBaseUrl}/athlete/activities" +
                  $"?after={afterUnix}&before={beforeUnix}" +
                  $"&per_page={pageSize}&page={page}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _httpClient.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
            throw new StravaApiException(
                $"Erro ao buscar atividades: HTTP {(int)response.StatusCode}",
                (int)response.StatusCode);

        var pageActivities = await response.Content
            .ReadFromJsonAsync<List<StravaActivity>>(JsonOpts, ct) ?? [];

        if (pageActivities.Count == 0) break;

        var runs = pageActivities
            .Where(a => a.SportType.Contains("Run", StringComparison.OrdinalIgnoreCase))
            .ToList();

        allActivities.AddRange(runs);

        if (pageActivities.Count < pageSize) break;

        page++;
    }

    return allActivities;
}
    // ──────────────────────────────────────────────────────────────────────────
    // Helpers Privados
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Retorna um access token válido.
    /// Se expirado, usa o refresh token para obter um novo automaticamente.
    /// </summary>
    private async Task<string> GetValidAccessTokenAsync(Guid userId, CancellationToken ct)
    {
        var tokenRecord = await GetStoredTokenAsync(userId, ct);

        var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        // Renova com 5 min de margem
        if (tokenRecord.ExpiresAt > nowUnix + 300)
            return tokenRecord.AccessToken;

        _logger.LogInformation("Access token expirado para userId={UserId}. Renovando...", userId);
        return await RefreshAndSaveTokenAsync(userId, tokenRecord.RefreshToken, ct);
    }

    private async Task<string> RefreshAndSaveTokenAsync(
        Guid userId,
        string refreshToken,
        CancellationToken ct)
    {
        var formContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"]     = _options.ClientId,
            ["client_secret"] = _options.ClientSecret,
            ["refresh_token"] = refreshToken,
            ["grant_type"]    = "refresh_token"
        });

        var response = await _httpClient.PostAsync(_options.TokenUrl, formContent, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Falha ao renovar token: {Status} – {Body}",
                response.StatusCode, body);
            throw new StravaAuthException("Falha ao renovar access token do Strava. Reconecte.");
        }

        var tokenResponse = await response.Content.ReadFromJsonAsync<StravaTokenResponse>(
            JsonOpts, ct)
            ?? throw new StravaAuthException("Resposta de refresh token veio nula.");

        await PersistTokenAsync(userId, tokenResponse, ct);
        _logger.LogInformation("Token renovado com sucesso para userId={UserId}", userId);

        return tokenResponse.AccessToken;
    }

    private async Task PersistTokenAsync(
        Guid userId,
        StravaTokenResponse tokenResponse,
        CancellationToken ct)
    {
        // Verifica se já existe um registro para o usuário (upsert manual)
        var existing = await _supabase
            .From<UserStravaToken>()
            .Where(t => t.UserId == userId)
            .Single();

        var record = existing ?? new UserStravaToken { Id = Guid.NewGuid(), UserId = userId };

        record.AccessToken      = tokenResponse.AccessToken;
        record.RefreshToken     = tokenResponse.RefreshToken;
        record.ExpiresAt        = tokenResponse.ExpiresAt;
        record.Scope            = _options.Scope;
        record.UpdatedAt        = DateTimeOffset.UtcNow;

        if (tokenResponse.Athlete is not null)
            record.StravaAthleteId = tokenResponse.Athlete.Id;

        await _supabase.From<UserStravaToken>().Upsert(record);
    }

    private async Task<UserStravaToken> GetStoredTokenAsync(Guid userId, CancellationToken ct)
    {
        var record = await _supabase
            .From<UserStravaToken>()
            .Where(t => t.UserId == userId)
            .Single();

        return record ?? throw new TokenNotFoundException(userId);
    }

    public async Task<string> GetAveragePaceOfLastRunsAsync(
    Guid userId, 
    int runCount = 10, 
    CancellationToken ct = default)
{
    var recentActivities = await GetRecentActivitiesAsync(userId, 30, ct);

    var lastRuns = recentActivities
        .Where(a => a.SportType.Contains("Run", StringComparison.OrdinalIgnoreCase))
        .Take(runCount)
        .ToList();

    if (lastRuns.Count == 0)
    {
        return "0:00 /km";
    }

    double totalMovingTimeSeconds = lastRuns.Sum(r => r.MovingTime); 
    double totalDistanceKm = lastRuns.Sum(r => r.DistanceKm);

    if (totalDistanceKm == 0) return "0:00 /km";

    double averagePaceDecimal = (totalMovingTimeSeconds / 60.0) / totalDistanceKm;

    TimeSpan paceTimeSpan = TimeSpan.FromMinutes(averagePaceDecimal);

    string formattedPace = $"{(int)paceTimeSpan.TotalMinutes}:{paceTimeSpan.Seconds:D2} /km";

    return formattedPace;
}
}

