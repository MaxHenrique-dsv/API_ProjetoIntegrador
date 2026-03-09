using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StravaIntegration.Exceptions;
using StravaIntegration.Models.DTOs;
using StravaIntegration.Services;
using System.Security.Claims;

namespace StravaIntegration.Controllers;

/// <summary>
/// Endpoints para validação de desafios e liberação de prêmios.
/// </summary>
[ApiController]
[Route("api/challenges")]
[Authorize]
[Produces("application/json")]
public sealed class ChallengeController : ControllerBase
{
    private readonly IChallengeValidationService _validationService;
    private readonly ILogger<ChallengeController> _logger;

private readonly IJoinChallengeService _joinService; // ← campo novo

public ChallengeController(
    IChallengeValidationService validationService,
    IJoinChallengeService joinService,             // ← parâmetro novo
    ILogger<ChallengeController> logger)
{
    _validationService = validationService;
    _joinService       = joinService;              // ← atribuição nova
    _logger            = logger;
}


    [HttpPost("{challengeId:guid}/join")]
[ProducesResponseType(StatusCodes.Status200OK)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
public async Task<IActionResult> JoinChallenge(
    Guid challengeId,
    CancellationToken ct = default)
{
    var userId = GetCurrentUserId();
    if (userId == Guid.Empty) return Unauthorized();

    try
    {
        var result = await _joinService.JoinAndSyncActivitiesAsync(userId, challengeId, ct);

        return Ok(new
        {
            result.ChallengeId,
            result.ChallengeTitle,
            period = new { start = result.PeriodStart, end = result.PeriodEnd },
            result.ActivitiesSynced,
            message = result.ActivitiesSynced > 0
                ? $"{result.ActivitiesSynced} corrida(s) sincronizada(s) do Strava."
                : "Nenhuma corrida encontrada no período do desafio.",
            activities = result.Activities
        });
    }
    catch (ChallengeNotFoundException ex)
    {
        return NotFound(new { error = ex.Message });
    }
    catch (TokenNotFoundException ex)
    {
        return NotFound(new { error = ex.Message, hint = "Conecte o Strava em /api/strava/login" });
    }
}
    // ──────────────────────────────────────────────────────────────────────────
    // POST /api/challenges/{challengeId}/sync
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Busca as atividades recentes do usuário no Strava e verifica
    /// se alguma delas completa o desafio. Concede o prêmio automaticamente
    /// se o critério for atingido.
    /// </summary>
    [HttpPost("{challengeId:guid}/sync")]
    [ProducesResponseType(typeof(ChallengeValidationResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SyncAndValidate(
        Guid challengeId,
        [FromQuery] int recentCount = 10,
        CancellationToken ct = default)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        _logger.LogInformation(
            "Sync solicitado. UserId={UserId}, ChallengeId={ChallengeId}",
            userId, challengeId);

        try
        {
            var request = new SyncActivitiesRequest(userId, challengeId, recentCount);
            var result  = await _validationService.SyncAndValidateAsync(request, ct);

            return Ok(new
            {
                result.ChallengeCompleted,
                result.RewardHistoryId,
                result.ChallengeTitle,
                result.FailureReason,
                Message = result.ChallengeCompleted
                    ? $"🏆 Parabéns! Desafio '{result.ChallengeTitle}' concluído!"
                    : $"Desafio ainda não concluído: {result.FailureReason}"
            });
        }
        catch (TokenNotFoundException ex)
        {
            return NotFound(new { error = ex.Message, hint = "Conecte o Strava em /api/strava/login" });
        }
        catch (ChallengeNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (RewardNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (AlreadyRewardedException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // POST /api/challenges/{challengeId}/validate-activity
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Valida uma atividade Strava específica contra um desafio.
    /// Útil quando o frontend já tem o ID da atividade (ex: via Webhook do Strava).
    /// </summary>
    [HttpPost("{challengeId:guid}/validate-activity")]
    [ProducesResponseType(typeof(ChallengeValidationResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ValidateSingleActivity(
        Guid challengeId,
        [FromQuery] long stravaActivityId,
        CancellationToken ct = default)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        if (stravaActivityId <= 0)
            return BadRequest(new { error = "stravaActivityId inválido." });

        try
        {
            var request = new ValidateActivityRequest(userId, challengeId, stravaActivityId);
            var result  = await _validationService.ValidateSingleActivityAsync(request, ct);

            return Ok(new
            {
                result.ChallengeCompleted,
                result.RewardHistoryId,
                result.ChallengeTitle,
                result.FailureReason,
                StravaActivityUrl = $"https://www.strava.com/activities/{stravaActivityId}"
            });
        }
        catch (TokenNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (ChallengeNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (RewardNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (StravaApiException ex) when (ex.StatusCode == 404)
        {
            return NotFound(new { error = $"Atividade {stravaActivityId} não encontrada no Strava." });
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    private Guid GetCurrentUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? User.FindFirstValue("sub");

        return Guid.TryParse(raw, out var id) ? id : Guid.Empty;
    }
    
}

