using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StravaIntegration.Exceptions;
using StravaIntegration.Models.DTOs;
using StravaIntegration.Services;

namespace StravaIntegration.Controllers;

[ApiController]
[Route("api/challenges")]
[AllowAnonymous]
[Produces("application/json")]
public sealed class ChallengeController : ControllerBase
{
    private readonly IChallengeValidationService _validationService;
    private readonly IJoinChallengeService _joinService;
    private readonly ILogger<ChallengeController> _logger;

    public ChallengeController(
        IChallengeValidationService validationService,
        IJoinChallengeService joinService,
        ILogger<ChallengeController> logger)
    {
        _validationService = validationService;
        _joinService = joinService;
        _logger = logger;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // POST /api/challenges/{challengeId}/join?userId={uuid}
    // ──────────────────────────────────────────────────────────────────────────

    [HttpPost("{challengeId:guid}/join")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> JoinChallenge(
        Guid challengeId,
        [FromQuery] Guid userId,
        CancellationToken ct = default)
    {
        if (userId == Guid.Empty)
            return BadRequest(new
            {
                error = "userId é obrigatório.",
                example = $"/api/challenges/{challengeId}/join?userId=xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
            });

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
    // POST /api/challenges/{challengeId}/sync?userId={uuid}&recentCount={n}
    // ──────────────────────────────────────────────────────────────────────────

    [HttpPost("{challengeId:guid}/sync")]
    [ProducesResponseType(typeof(ChallengeValidationResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SyncAndValidate(
        Guid challengeId,
        [FromQuery] Guid userId,
        [FromQuery] int recentCount = 10,
        CancellationToken ct = default)
    {
        if (userId == Guid.Empty)
            return BadRequest(new
            {
                error = "userId é obrigatório.",
                example = $"/api/challenges/{challengeId}/sync?userId=xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
            });

        _logger.LogInformation(
            "Sync solicitado. UserId={UserId}, ChallengeId={ChallengeId}",
            userId, challengeId);

        try
        {
            var request = new SyncActivitiesRequest(userId, challengeId, recentCount);
            var result = await _validationService.SyncAndValidateAsync(request, ct);

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
    // POST /api/challenges/{challengeId}/validate-activity?userId={uuid}&stravaActivityId={id}
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Valida uma atividade Strava específica contra um desafio.
    /// Útil quando o frontend já tem o ID da atividade (ex: via Webhook do Strava).
    /// </summary>
    [HttpPost("{challengeId:guid}/validate-activity")]
    [ProducesResponseType(typeof(ChallengeValidationResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ValidateSingleActivity(
        Guid challengeId,
        [FromQuery] Guid userId,
        [FromQuery] long stravaActivityId,
        CancellationToken ct = default)
    {
        if (userId == Guid.Empty)
            return BadRequest(new
            {
                error = "userId é obrigatório.",
                example = $"/api/challenges/{challengeId}/validate-activity?userId=xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx&stravaActivityId=123456"
            });

        if (stravaActivityId <= 0)
            return BadRequest(new { error = "stravaActivityId inválido." });

        try
        {
            var request = new ValidateActivityRequest(userId, challengeId, stravaActivityId);
            var result = await _validationService.ValidateSingleActivityAsync(request, ct);

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
}