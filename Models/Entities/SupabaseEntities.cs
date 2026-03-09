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

    [Column("challenge_type")]
    public string ChallengeType { get; set; } = string.Empty;

    [Column("target_value")]
    public double TargetValue { get; set; }

    [Column("start_date")]
    public DateTimeOffset StartDate { get; set; }

    [Column("end_date")]
    public DateTimeOffset EndDate { get; set; }
}

// ─── user_activities ─────────────────────────────────────────────────────────
[Table("user_activities")]
public sealed class UserActivity : BaseModel
{
    [PrimaryKey("id", false)]
    public long Id { get; set; }

    [Column("user_id")]
    public Guid UserId { get; set; }

    [Column("challenge_id")]
    public Guid ChallengeId { get; set; }

    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Column("distance_km")]
    public double DistanceKm { get; set; }

    [Column("moving_time_minutes")]
    public double MovingTimeMinutes { get; set; }

    [Column("start_date")]
    public DateTimeOffset StartDate { get; set; }
}

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

    [Column("expires_at")]
    public long ExpiresAt { get; set; }

    [Column("scope")]
    public string Scope { get; set; } = string.Empty;

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }
}
