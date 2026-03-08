namespace StravaIntegration.Exceptions;

public sealed class StravaAuthException(string message, Exception? inner = null)
    : Exception(message, inner);

public sealed class StravaApiException(string message, int? statusCode = null, Exception? inner = null)
    : Exception(message, inner)
{
    public int? StatusCode { get; } = statusCode;
}

public sealed class TokenNotFoundException(Guid userId)
    : Exception($"Nenhum token Strava encontrado para o usuário {userId}. Conecte o Strava primeiro.");

public sealed class ChallengeNotFoundException(Guid challengeId)
    : Exception($"Desafio {challengeId} não encontrado.");

public sealed class RewardNotFoundException(Guid challengeId)
    : Exception($"Nenhum prêmio vinculado ao desafio {challengeId}.");

public sealed class AlreadyRewardedException(Guid userId, Guid rewardId)
    : Exception($"Usuário {userId} já recebeu o prêmio {rewardId}.");
