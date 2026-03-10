using Supabase;
using StravaIntegration.Models.Entities;

namespace StravaIntegration.Services;

public interface IRewardService
{
    Task ClaimRewardAsync(Guid userId, Guid rewardId);
}

public class RewardService : IRewardService
{
    private readonly Client _supabase;

    public RewardService(Client supabase)
    {
        _supabase = supabase;
    }

    public async Task ClaimRewardAsync(Guid userId, Guid rewardId)
    {
        var rewardHistory = new RewardHistory
        {
            UserId = userId,
            RewardId = rewardId,
            EarnedAt = DateTimeOffset.UtcNow,
            ProofType = "strava_sync"
        };

        try
        {
            // 📍 O TRY/CATCH FICA EXATAMENTE AQUI
            await _supabase.From<RewardHistory>().Insert(rewardHistory);
        }
        catch (Postgrest.Exceptions.PostgrestException ex)
        {
            // Se a constraint unique_reward_per_user for violada
            if (ex.Message.Contains("unique_reward_per_user"))
            {
                // Recomendo usares as tuas próprias exceções se tiveres (ex: DomainException)
                throw new Exception("Já resgataste este prémio anteriormente."); 
            }
            
            // Se o trigger enforce_user_reward_limit barrar a inserção
            if (ex.Message.Contains("enforce_user_reward_limit"))
            {
                throw new Exception("Atingiste o limite de resgates mensais.");
            }

            // Se for outro erro qualquer da base de dados, lança-o para cima
            throw;
        }
    }
}