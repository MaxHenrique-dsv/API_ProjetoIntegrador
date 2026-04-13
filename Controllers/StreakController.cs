using Microsoft.AspNetCore.Mvc;
using StravaIntegration.Models.Entities;

namespace StravaIntegration.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public sealed class StreakController : ControllerBase
{
    private readonly Supabase.Client _supabase;
    private readonly ILogger<StreakController> _logger;

    public StreakController(Supabase.Client supabase, ILogger<StreakController> logger)
    {
        _supabase = supabase;
        _logger = logger;
    }

    /// <summary>
    /// Gets the current streak for a given user.
    /// </summary>
    /// <param name="userId">The Supabase user ID</param>
    /// <returns>A JSON object containing currentStreak and lastActivityDate</returns>
    [HttpGet("{userId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetStreak(Guid userId)
    {
        if (userId == Guid.Empty)
        {
            return BadRequest(new { error = "userId é obrigatório." });
        }

        try
        {
            var streak = await _supabase
                .From<UserStreak>()
                .Where(x => x.UserId == userId)
                .Single();

            if (streak == null)
            {
                // Se não houver streak, retorna 0 com segurança (pois o usuário ainda não postou/sincronizou atividades).
                return Ok(new 
                { 
                    userId = userId, 
                    currentStreak = 0, 
                    lastActivityDate = (DateTimeOffset?)null 
                });
            }

            return Ok(new 
            {
                userId = streak.UserId,
                currentStreak = streak.CurrentStreak,
                lastActivityDate = streak.LastActivityDate
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao buscar streak para userId={UserId}", userId);
            return StatusCode(500, new { error = "Erro interno ao buscar streak." });
        }
    }
}
