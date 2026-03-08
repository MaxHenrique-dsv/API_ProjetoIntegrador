using StravaIntegration.Models.DTOs;

namespace StravaIntegration.Services;

public interface IChallengeValidationService
{
    Task<ChallengeValidationResult> SyncAndValidateAsync(SyncActivitiesRequest request, CancellationToken ct = default);
    Task<ChallengeValidationResult> ValidateSingleActivityAsync(ValidateActivityRequest request, CancellationToken ct = default);
}