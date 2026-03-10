using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using StravaIntegration.Exceptions;
using StravaIntegration.Models.Options;
using StravaIntegration.Services;
using System.Security.Claims; 
using System.Security.Cryptography;
using System.Text;

namespace StravaIntegration.Controllers;

/// <summary>
/// Gerencia o fluxo OAuth 2.0 com o Strava.
/// </summary>
[ApiController]
[Route("api/strava")]
[Produces("application/json")]
public sealed class StravaController : ControllerBase
{
    private readonly IStravaService _stravaService;
    private readonly AppOptions _appOptions;
    private readonly StravaOptions _stravaOptions;
    private readonly ILogger<StravaController> _logger;
    private readonly HttpClient _httpClient;

    public StravaController(
        IStravaService stravaService,
        IOptions<AppOptions> appOptions,
        IOptions<StravaOptions> stravaOptions,
        ILogger<StravaController> logger,
        HttpClient httpClient)
    {
        _stravaService = stravaService;
        _appOptions = appOptions.Value;
        _stravaOptions = stravaOptions.Value;
        _logger = logger;
        _httpClient    = httpClient;
    }
private Guid GetCurrentUserId()
    {
        var raw = User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier)
               ?? User.FindFirstValue("sub");

        return Guid.TryParse(raw, out var id) ? id : Guid.Empty;
    }
    // ──────────────────────────────────────────────────────────────────────────
    // GET /api/strava/login?userId={supabase-user-uuid}
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Inicia o fluxo OAuth do Strava.
    ///
    /// COMO USAR:
    /// GET /api/strava/login?userId={uuid-do-supabase}
    /// </summary>
    [HttpGet("login")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult Login([FromQuery] Guid userId)
    {
        if (userId == Guid.Empty)
            return BadRequest(new
            {
                error = "userId é obrigatório.",
                example = "/api/strava/login?userId=xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
            });

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var payload = $"{userId}|{timestamp}";
        var hmac = ComputeHmac(payload, _stravaOptions.ClientSecret);
        var state = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{payload}|{hmac}"));

        var authUrl = _stravaService.BuildAuthorizationUrl(state);

        _logger.LogInformation(
            "Iniciando OAuth Strava para userId={UserId}. Redirect → Strava",
            userId);

        return Redirect(authUrl);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // GET /api/strava/callback  ← Strava redireciona aqui após autorização
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Callback OAuth do Strava. Recebe o authorization code e o state,
    /// valida o HMAC, troca pelo access token e salva no Supabase.
    /// </summary>
    [HttpGet("callback")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status302Found)]
    public async Task<IActionResult> Callback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        [FromQuery] string? scope,
        CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(error))
        {
            _logger.LogWarning("Strava OAuth negado pelo usuário. Motivo: {Error}", error);
            return Redirect($"{_appOptions.FrontendErrorUrl}?reason=access_denied");
        }

        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
        {
            _logger.LogError("Callback recebido sem 'code' ou 'state'.");
            return Redirect($"{_appOptions.FrontendErrorUrl}?reason=invalid_callback");
        }

        if (!TryParseState(state, out var userId, out var stateError))
        {
            _logger.LogError("State inválido no callback Strava: {Error}", stateError);
            return Redirect($"{_appOptions.FrontendErrorUrl}?reason=invalid_state");
        }

        try
        {
            var result = await _stravaService.ExchangeCodeAndSaveTokenAsync(code, userId, ct);

            _logger.LogInformation(
                "✅ Strava conectado. UserId={UserId}, StravaAthleteId={AthleteId}",
                result.UserId, result.StravaAthleteId);

            return Redirect(
                $"{_appOptions.FrontendCallbackUrl}" +
                $"?strava_athlete_id={result.StravaAthleteId}" +
                $"&user_id={result.UserId}");
        }
        catch (StravaAuthException ex)
        {
            _logger.LogError(ex, "Falha ao conectar Strava para userId={UserId}", userId);
            return Redirect($"{_appOptions.FrontendErrorUrl}?reason=token_exchange_failed");
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // GET /api/strava/activities?userId={uuid}&count={n}
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Retorna as atividades recentes do usuário informado via query param.
    /// </summary>
    [HttpGet("activities")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRecentActivities(
        [FromQuery] Guid userId,
        [FromQuery] int count = 10,
        CancellationToken ct = default)
    {
        if (userId == Guid.Empty)
            return BadRequest(new
            {
                error = "userId é obrigatório.",
                example = "/api/strava/activities?userId=xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx&count=10"
            });

        try
        {
            var activities = await _stravaService.GetRecentActivitiesAsync(userId, count, ct);

            var summaries = activities.Select(a => new
            {
                a.Id,
                a.Name,
                a.SportType,
                DistanceKm = Math.Round(a.DistanceKm, 2),
                PaceSecPerKm = Math.Round(a.PaceSecPerKm, 0),
                ElevationGainM = a.TotalElevationGain,
                MovingTimeSeconds = a.MovingTime,
                StartDate = a.StartDateLocal,
                a.StravaUrl
            });

            return Ok(summaries);
        }
        catch (TokenNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (StravaApiException ex)
        {
            _logger.LogError(ex, "Erro ao buscar atividades Strava para userId={UserId}", userId);
            return StatusCode(ex.StatusCode ?? 500, new { error = ex.Message });
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // DELETE /api/strava/disconnect?userId={uuid}
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Remove a vinculação do Strava do usuário informado via query param.
    /// </summary>
[HttpDelete("disconnect")]
[ProducesResponseType(StatusCodes.Status204NoContent)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
public async Task<IActionResult> Disconnect(
    [FromServices] Supabase.Client supabase,
    CancellationToken ct)
{
    var userId = GetCurrentUserId();
    if (userId == Guid.Empty) return Unauthorized();

    // ── 1. Busca o token salvo ────────────────────────────────────────────
    var tokenRecord = await supabase
        .From<Models.Entities.UserStravaToken>()
        .Where(t => t.UserId == userId)
        .Single();

    if (tokenRecord is null)
    {
        _logger.LogWarning("Disconnect: nenhum token encontrado para userId={UserId}", userId);
        return NotFound(new { error = "Conta Strava não está conectada." });
    }

    // ── 2. Renova o token se estiver expirado ─────────────────────────────
    // O Strava IGNORA silenciosamente a revogação de tokens expirados.
    // Por isso precisamos garantir um token válido antes do deauthorize.
    var accessToken = tokenRecord.AccessToken;
    var nowUnix     = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    if (tokenRecord.ExpiresAt <= nowUnix + 60)
    {
        _logger.LogInformation(
            "Token expirado antes do disconnect. Renovando para userId={UserId}", userId);
        try
        {
            var refreshContent = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"]     = _stravaOptions.ClientId,
                ["client_secret"] = _stravaOptions.ClientSecret,
                ["refresh_token"] = tokenRecord.RefreshToken,
                ["grant_type"]    = "refresh_token"
            });

            var refreshResponse = await _httpClient.PostAsync(
                _stravaOptions.TokenUrl, refreshContent, ct);

            if (refreshResponse.IsSuccessStatusCode)
            {
                var refreshed = await refreshResponse.Content
                    .ReadFromJsonAsync<Models.Strava.StravaTokenResponse>(
                        new System.Text.Json.JsonSerializerOptions(
                            System.Text.Json.JsonSerializerDefaults.Web), ct);

                if (refreshed is not null)
                    accessToken = refreshed.AccessToken;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Erro ao renovar token antes do disconnect. UserId={UserId}", userId);
        }
    }

    // ── 3. Revoga o acesso no Strava ──────────────────────────────────────
    try
    {
        var deauthorizeContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["access_token"] = accessToken
        });

        var response = await _httpClient.PostAsync(
            "https://www.strava.com/oauth/deauthorize", deauthorizeContent, ct);

        if (response.IsSuccessStatusCode)
            _logger.LogInformation(
                "✅ Acesso revogado no Strava. UserId={UserId}", userId);
        else
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning(
                "Strava deauthorize retornou {Status}. Body: {Body}",
                response.StatusCode, body);
        }
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex,
            "Erro ao chamar deauthorize no Strava. UserId={UserId}", userId);
    }

    // ── 4. Remove o token do Supabase (sempre) ────────────────────────────
    await supabase
        .From<Models.Entities.UserStravaToken>()
        .Where(t => t.UserId == userId)
        .Delete();

    _logger.LogInformation("Strava desconectado. UserId={UserId}", userId);
    return NoContent();
}

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private bool TryParseState(string state, out Guid userId, out string? error)
    {
        userId = Guid.Empty;
        error = null;

        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(state));
            var parts = decoded.Split('|');

            if (parts.Length != 3)
            {
                error = "Formato inválido (esperado 3 partes).";
                return false;
            }

            var userIdStr = parts[0];
            var timestamp = parts[1];
            var receivedHmac = parts[2];

            var payload = $"{userIdStr}|{timestamp}";
            var expectedHmac = ComputeHmac(payload, _stravaOptions.ClientSecret);

            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(receivedHmac),
                    Encoding.UTF8.GetBytes(expectedHmac)))
            {
                error = "Assinatura HMAC inválida — possível adulteração.";
                return false;
            }

            var issuedAt = DateTimeOffset.FromUnixTimeSeconds(long.Parse(timestamp));
            if (DateTimeOffset.UtcNow - issuedAt > TimeSpan.FromMinutes(15))
            {
                error = "State expirado. Inicie o fluxo novamente.";
                return false;
            }

            if (!Guid.TryParse(userIdStr, out userId))
            {
                error = "userId no state não é um UUID válido.";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string ComputeHmac(string payload, string secret)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var dataBytes = Encoding.UTF8.GetBytes(payload);
        var hash = HMACSHA256.HashData(keyBytes, dataBytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}