using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StravaIntegration.Services;

namespace StravaIntegration.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize] // Garante que a rota só é acedida com um token JWT do Supabase válido
public class RewardController : ControllerBase
{
    private readonly IRewardService _rewardService;

    public RewardController(IRewardService rewardService)
    {
        _rewardService = rewardService;
    }

    [HttpPost("{rewardId}/claim")]
    public async Task<IActionResult> ClaimReward(Guid rewardId)
    {
        // Extrai o UserId do token JWT do utilizador logado
        var userIdClaim = User.FindFirst("sub")?.Value;
        if (!Guid.TryParse(userIdClaim, out var userId))
            return Unauthorized();

        try
        {
            // Chama o serviço (que contém o try/catch da base de dados)
            await _rewardService.ClaimRewardAsync(userId, rewardId);
            
            return Ok(new { message = "Prémio resgatado com sucesso!" });
        }
        catch (Exception ex)
        {
            // 📍 Apanha a exceção lançada pelo serviço ("Já resgataste este prémio...")
            // e devolve um 400 Bad Request
            return BadRequest(new { error = ex.Message });
        }
    }
}