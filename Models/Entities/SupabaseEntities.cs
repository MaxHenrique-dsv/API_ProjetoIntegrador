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

    [Column("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Tipos suportados vindos do Supabase:
    ///   "corrida"  → distância mínima em km  (target_value = km exigidos, ex: 10.0)
    ///   "pace"     → pace máximo em min/km   (target_value = minutos decimais, ex: 6.5 = 6:30/km)
    /// </summary>
    [Column("challenge_type")]
    public string ChallengeType { get; set; } = string.Empty;

    /// <summary>
    /// Valor alvo numérico conforme o challenge_type:
    ///   "corrida" → quilômetros mínimos  (ex: 10.0 = deve correr ao menos 10 km)
    ///   "pace"    → pace máximo em min/km (ex: 6.5 = 6:30/km — deve correr igual ou mais rápido)
    /// </summary>
    [Column("target_value")]
    public double TargetValue { get; set; }

    [Column("start_date")]
    public DateTimeOffset StartDate { get; set; }

    [Column("end_date")]
    public DateTimeOffset EndDate { get; set; }

    /// <summary>"all" ou "specific" — conforme constraint do Supabase.</summary>
    [Column("target_audience")]
    public string TargetAudience { get; set; } = "all";

    [Column("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }
}

// ─── user_activities ──────────────────────────────────────────────────────────
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

// ─── user_strava_tokens ───────────────────────────────────────────────────────
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
// Adiciona as novas tabelas ao ficheiro SupabaseEntities.cs

// ─── posts ───────────────────────────────────────────────────────────────────
[Table("posts")]
public sealed class Post : BaseModel
{
    [PrimaryKey("id", false)]
    public Guid Id { get; set; }

    [Column("user_id")]
    public Guid UserId { get; set; }

    [Column("image_url")]
    public string ImageUrl { get; set; } = string.Empty;

    [Column("caption")]
    public string? Caption { get; set; }

    [Column("challenge_id")]
    public Guid? ChallengeId { get; set; }

    [Column("activity_id")]
    public long? ActivityId { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }
}

// ─── post_likes ──────────────────────────────────────────────────────────────
[Table("post_likes")]
public sealed class PostLike : BaseModel
{
    [PrimaryKey("id", false)]
    public Guid Id { get; set; }

    [Column("post_id")]
    public Guid PostId { get; set; }

    [Column("user_id")]
    public Guid UserId { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }
}

// ─── post_comments ───────────────────────────────────────────────────────────
[Table("post_comments")]
public sealed class PostComment : BaseModel
{
    [PrimaryKey("id", false)]
    public Guid Id { get; set; }

    [Column("post_id")]
    public Guid PostId { get; set; }

    [Column("user_id")]
    public Guid UserId { get; set; }

    [Column("content")]
    public string Content { get; set; } = string.Empty;

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }
}