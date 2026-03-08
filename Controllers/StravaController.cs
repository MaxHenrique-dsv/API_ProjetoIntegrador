using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using StravaIntegration.Exceptions;
using StravaIntegration.Models.Options;
using StravaIntegration.Services;
using System.Security.Claims;

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
    private readonly ILogger<StravaController> _logger;

    public StravaController(
        IStravaService stravaService,
        IOptions<AppOptions> appOptions,
        ILogger<StravaController> logger)
    {
        _stravaService = stravaService;
        _appOptions    = appOptions.Value;
        _logger        = logger;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // GET /api/strava/login
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Inicia o fluxo OAuth do Strava.
    /// Redireciona o usuário para a página de autorização do Strava.
    /// O userId do Supabase é passado como `state` para ser recuperado no callback.
    /// </summary>
    /// <remarks>
    /// O cliente deve passar o JWT do Supabase no header Authorization.
    /// O userId é extraído do token JWT pelo middleware de autenticação.
    /// </remarks>
    [HttpGet("login")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult Login()
    {
        // Extrai o user_id do claim do JWT do Supabase (sub = UUID do usuário)
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new { error = "Usuário não autenticado." });

        // O `state` codifica o userId para recuperá-lo no callback sem sessão
        var state = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes(userId));

        var authUrl = _stravaService.BuildAuthorizationUrl(state);

        _logger.LogInformation("Iniciando OAuth Strava para userId={UserId}", userId);
        return Ok(new { url = authUrl });
    }

    // ──────────────────────────────────────────────────────────────────────────
    // GET /api/strava/callback
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Callback OAuth do Strava. Recebe o authorization code e o state,
    /// troca pelo access token e salva no Supabase.
    /// </summary>
    [HttpGet("callback")]
    [AllowAnonymous] // O Strava redireciona sem JWT
    [ProducesResponseType(StatusCodes.Status302Found)]
    public async Task<IActionResult> Callback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        [FromQuery] string? scope,
        CancellationToken ct)
    {
        // Usuário recusou a autorização no Strava
        if (!string.IsNullOrEmpty(error))
        {
            _logger.LogWarning("Strava OAuth negado pelo usuário. Motivo: {Error}", error);
            return Redirect($"{_appOptions.FrontendErrorUrl}?reason=access_denied");
        }

        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
        {
            _logger.LogError("Callback sem code ou state.");
            return Redirect($"{_appOptions.FrontendErrorUrl}?reason=invalid_callback");
        }

        // Recupera o userId que foi codificado no state
        Guid userId;
        try
        {
            var userIdStr = System.Text.Encoding.UTF8.GetString(
                Convert.FromBase64String(state));
            userId = Guid.Parse(userIdStr);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "State inválido no callback Strava.");
            return Redirect($"{_appOptions.FrontendErrorUrl}?reason=invalid_state");
        }

        try
        {
            var result = await _stravaService.ExchangeCodeAndSaveTokenAsync(code, userId, ct);

            _logger.LogInformation(
                "Strava conectado. UserId={UserId}, StravaAthleteId={AthleteId}",
                result.UserId, result.StravaAthleteId);

            return Redirect(
                $"{_appOptions.FrontendCallbackUrl}?strava_athlete_id={result.StravaAthleteId}");
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
    [Authorize]
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
                DistanceKm       = Math.Round(a.DistanceKm, 2),
                PaceSecPerKm     = Math.Round(a.PaceSecPerKm, 0),
                ElevationGainM   = a.TotalElevationGain,
                MovingTimeSeconds = a.MovingTime,
                StartDate        = a.StartDateLocal,
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
    [Authorize]
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
}
