using Postgrest.Attributes;
using Postgrest.Models;

namespace StravaIntegration.Models.Entities;

// ─── challenges ───────────────────────────────────────────────────────────────
[Table("challenges")]
public sealed class Challenge : BaseModel
{
    [PrimaryKey("id")]
    public Guid Id { get; set; }

    [Column("club_id")]
    public Guid ClubId { get; set; }

    [Column("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Examples: "distance_km", "pace_min_per_km", "elevation_m", "duration_min"
    /// </summary>
    [Column("challenge_type")]
    public string ChallengeType { get; set; } = string.Empty;

    /// <summary>
    /// Numeric target: distance in km, pace in sec/km, elevation in meters, etc.
    /// </summary>
    [Column("target_value")]
    public double TargetValue { get; set; }
}

// ─── rewards ──────────────────────────────────────────────────────────────────
[Table("rewards")]
public sealed class Reward : BaseModel
{
    [PrimaryKey("id")]
    public Guid Id { get; set; }

    [Column("club_id")]
    public Guid ClubId { get; set; }

    [Column("challenge_id")]
    public Guid ChallengeId { get; set; }
}

// ─── reward_history ───────────────────────────────────────────────────────────
[Table("reward_history")]
public sealed class RewardHistory : BaseModel
{
    [PrimaryKey("id")]
    public Guid Id { get; set; }

    [Column("reward_id")]
    public Guid RewardId { get; set; }

    [Column("user_id")]
    public Guid UserId { get; set; }

    [Column("earned_at")]
    public DateTimeOffset EarnedAt { get; set; }

    [Column("proof_type")]
    public string ProofType { get; set; } = "strava_sync";

    [Column("proof_url")]
    public string ProofUrl { get; set; } = string.Empty;
}

// ─── user_strava_tokens (tabela de suporte – crie no Supabase) ────────────────
[Table("user_strava_tokens")]
public sealed class UserStravaToken : BaseModel
{
    [PrimaryKey("id")]
    public Guid Id { get; set; }

    [Column("user_id")]
    public Guid UserId { get; set; }

    [Column("strava_athlete_id")]
    public long StravaAthleteId { get; set; }

    [Column("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [Column("refresh_token")]
    public string RefreshToken { get; set; } = string.Empty;

    /// <summary>Unix timestamp de expiração do access token.</summary>
    [Column("expires_at")]
    public long ExpiresAt { get; set; }

    [Column("scope")]
    public string Scope { get; set; } = string.Empty;

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }
}
