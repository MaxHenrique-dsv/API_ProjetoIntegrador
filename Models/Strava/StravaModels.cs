using System.Text.Json.Serialization;

namespace StravaIntegration.Models.Strava;

// ─── OAuth Token Exchange ──────────────────────────────────────────────────────
public sealed record StravaTokenResponse
{
    [JsonPropertyName("token_type")]
    public string TokenType { get; init; } = string.Empty;

    [JsonPropertyName("expires_at")]
    public long ExpiresAt { get; init; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; init; }

    [JsonPropertyName("refresh_token")]
    public string RefreshToken { get; init; } = string.Empty;

    [JsonPropertyName("access_token")]
    public string AccessToken { get; init; } = string.Empty;

    [JsonPropertyName("athlete")]
    public StravaAthlete? Athlete { get; init; }
}

// ─── Athlete ──────────────────────────────────────────────────────────────────
public sealed record StravaAthlete
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("firstname")]
    public string FirstName { get; init; } = string.Empty;

    [JsonPropertyName("lastname")]
    public string LastName { get; init; } = string.Empty;

    [JsonPropertyName("profile")]
    public string ProfileImageUrl { get; init; } = string.Empty;
}

// ─── Activity ─────────────────────────────────────────────────────────────────
public sealed record StravaActivity
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>Distância em metros (campo bruto da API do Strava).</summary>
    [JsonPropertyName("distance")]
    public double Distance { get; init; }

    /// <summary>Tempo decorrido total em segundos.</summary>
    [JsonPropertyName("elapsed_time")]
    public int ElapsedTime { get; init; }

    /// <summary>Tempo em movimento em segundos (usado para calcular pace).</summary>
    [JsonPropertyName("moving_time")]
    public int MovingTime { get; init; }

    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    /// <summary>Ex: "Run", "TrailRun", "VirtualRun".</summary>
    [JsonPropertyName("sport_type")]
    public string SportType { get; init; } = string.Empty;

    [JsonPropertyName("start_date")]
    public DateTimeOffset StartDate { get; init; }

    [JsonPropertyName("start_date_local")]
    public DateTimeOffset StartDateLocal { get; init; }

    /// <summary>Ganho total de elevação em metros.</summary>
    [JsonPropertyName("total_elevation_gain")]
    public double TotalElevationGain { get; init; }

    /// <summary>Velocidade média em m/s (campo bruto da API do Strava).</summary>
    [JsonPropertyName("average_speed")]
    public double AverageSpeed { get; init; }

    [JsonPropertyName("max_speed")]
    public double MaxSpeed { get; init; }

    // ── Propriedades calculadas ───────────────────────────────────────────────

    /// <summary>Distância em quilômetros.</summary>
    public double DistanceKm => Distance / 1000.0;

    /// <summary>
    /// Pace médio em minutos por km (decimal).
    /// Ex: 6.5 = 6 minutos e 30 segundos por km.
    /// Calculado com MovingTime para não penalizar paradas.
    /// </summary>
    public double PaceMinPerKm => DistanceKm > 0
        ? (MovingTime / 60.0) / DistanceKm
        : double.MaxValue;

    /// <summary>
    /// Pace médio em segundos por km (legado — use PaceMinPerKm para validações).
    /// </summary>
    public double PaceSecPerKm => DistanceKm > 0
        ? MovingTime / DistanceKm
        : double.MaxValue;

    /// <summary>URL pública da atividade no Strava.</summary>
    public string StravaUrl => $"https://www.strava.com/activities/{Id}";
}
