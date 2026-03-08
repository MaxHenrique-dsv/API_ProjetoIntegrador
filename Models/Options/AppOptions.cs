namespace StravaIntegration.Models.Options;

public sealed class StravaOptions
{
    public const string SectionName = "Strava";

    public string ClientId { get; init; } = string.Empty;
    public string ClientSecret { get; init; } = string.Empty;
    public string RedirectUri { get; init; } = string.Empty;
    public string Scope { get; init; } = "activity:read_all";
    public string AuthorizeUrl { get; init; } = "https://www.strava.com/oauth/authorize";
    public string TokenUrl { get; init; } = "https://www.strava.com/api/v3/oauth/token";
    public string ApiBaseUrl { get; init; } = "https://www.strava.com/api/v3";
    public string ApprovalPrompt { get; init; } = "auto";
}

public sealed class SupabaseOptions
{
    public const string SectionName = "Supabase";

    public string Url { get; init; } = string.Empty;
    public string AnonKey { get; init; } = string.Empty;
    public string ServiceRoleKey { get; init; } = string.Empty;
    public string JwtSecret { get; init; } = string.Empty;
}

public sealed class AppOptions
{
    public const string SectionName = "App";

    public string FrontendCallbackUrl { get; init; } = string.Empty;
    public string FrontendErrorUrl { get; init; } = string.Empty;
}
