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

    public StravaController(
        IStravaService stravaService,
        IOptions<AppOptions> appOptions,
        IOptions<StravaOptions> stravaOptions,
        ILogger<StravaController> logger)
    {
        _stravaService = stravaService;
        _appOptions = appOptions.Value;
        _stravaOptions = stravaOptions.Value;
        _logger = logger;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // GET /api/strava/login?userId={supabase-user-uuid}
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Inicia o fluxo OAuth do Strava.
    /// 
    /// ⚠️ POR QUE NÃO USAR [Authorize] AQUI:
    /// Este endpoint é acessado via redirect de browser (link/botão no app).
    /// Browsers não enviam o header "Authorization: Bearer ..." em redirecionamentos,
    /// então qualquer [Authorize] resulta em 401 antes de chegar no Strava.
    /// A segurança é garantida pelo HMAC assinado no `state`.
    /// 
    /// COMO USAR:
    /// GET /api/strava/login?userId={uuid-do-supabase}
    /// O frontend obtém o userId do Supabase Auth e passa como query param.
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

        // ── Monta o state com HMAC para evitar CSRF / adulteração ─────────────
        // state = Base64( userId + "." + timestamp ) + "." + HMAC-SHA256
        // O callback valida o HMAC antes de processar o code.
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
        // Usuário clicou "Negar" na página do Strava
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

        // ── Valida e extrai o userId do state ─────────────────────────────────
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
    // GET /api/strava/activities
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Retorna as atividades recentes do usuário autenticado no Strava.
    /// </summary>
    [HttpGet("activities")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRecentActivities(
        [FromQuery] int count = 10,
        CancellationToken ct = default)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

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
            _logger.LogError(ex, "Erro ao buscar atividades Strava");
            return StatusCode(ex.StatusCode ?? 500, new { error = ex.Message });
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // DELETE /api/strava/disconnect
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Remove a vinculação do Strava do usuário (apaga o token salvo).
    /// </summary>
    [HttpDelete("disconnect")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Disconnect(
        [FromServices] Supabase.Client supabase,
        CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await supabase
            .From<Models.Entities.UserStravaToken>()
            .Where(t => t.UserId == userId)
            .Delete();

        _logger.LogInformation("Token Strava removido para userId={UserId}", userId);
        return NoContent();
    }

    // ──────────────────────────────────────────────────────────────────────────
    private Guid GetCurrentUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? User.FindFirstValue("sub");

        return Guid.TryParse(raw, out var id) ? id : Guid.Empty;
    }

    // ── Valida o state assinado com HMAC e extrai o userId ───────────────────
    private bool TryParseState(string state, out Guid userId, out string? error)
    {
        userId = Guid.Empty;
        error = null;

        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(state));
            var parts = decoded.Split('|');

            // Espera: userId | timestamp | hmac
            if (parts.Length != 3)
            {
                error = "Formato inválido (esperado 3 partes).";
                return false;
            }

            var userIdStr = parts[0];
            var timestamp = parts[1];
            var receivedHmac = parts[2];

            // Valida HMAC
            var payload = $"{userIdStr}|{timestamp}";
            var expectedHmac = ComputeHmac(payload, _stravaOptions.ClientSecret);

            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(receivedHmac),
                    Encoding.UTF8.GetBytes(expectedHmac)))
            {
                error = "Assinatura HMAC inválida — possível adulteração.";
                return false;
            }

            // Valida expiração do state (15 minutos para o usuário autorizar)
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