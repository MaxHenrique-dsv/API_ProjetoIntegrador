using StravaIntegration.Models.Entities;
using StravaIntegration.Models.Strava;

namespace StravaIntegration.Services;

// ─────────────────────────────────────────────────────────────────────────────
// Resultado detalhado de validação de uma atividade
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Detalha o que o usuário fez vs o que era exigido pelo desafio.
/// </summary>
public sealed record ActivityValidationDetail
{
    /// <summary>A atividade passou na validação?</summary>
    public bool Passed { get; init; }

    /// <summary>Tipo do desafio: "corrida" ou "pace".</summary>
    public string ChallengeType { get; init; } = string.Empty;

    // ── Valores do Strava ─────────────────────────────────────────────────────

    /// <summary>Distância real percorrida em km.</summary>
    public double ActualDistanceKm { get; init; }

    /// <summary>Pace real em min/km (ex: 6.5 = 6:30/km).</summary>
    public double ActualPaceMinPerKm { get; init; }

    /// <summary>Pace real formatado como string "mm:ss /km".</summary>
    public string ActualPaceFormatted { get; init; } = string.Empty;

    /// <summary>Tempo em movimento em minutos.</summary>
    public double ActualMovingTimeMinutes { get; init; }

    // ── Valores exigidos pelo desafio ─────────────────────────────────────────

    /// <summary>Distância mínima exigida (apenas para challenge_type "corrida").</summary>
    public double? RequiredDistanceKm { get; init; }

    /// <summary>Pace máximo exigido em min/km (apenas para challenge_type "pace").</summary>
    public double? RequiredPaceMinPerKm { get; init; }

    /// <summary>Pace máximo exigido formatado como string "mm:ss /km".</summary>
    public string? RequiredPaceFormatted { get; init; }

    // ── Diagnóstico ───────────────────────────────────────────────────────────

    /// <summary>Motivo da falha, ou null se passou.</summary>
    public string? FailureReason { get; init; }

    /// <summary>
    /// Percentual de progresso em relação à meta (cap em 100%).
    /// Ex: corrida de 8 km para meta de 10 km → 80%.
    /// </summary>
    public int ProgressPercent { get; init; }
}

// ─────────────────────────────────────────────────────────────────────────────
// Motor de validação estático — sem dependências externas
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Valida uma atividade do Strava contra os critérios de um desafio.
/// Suporta challenge_type: "corrida" e "pace".
/// </summary>
public static class ChallengeValidator
{
    /// <summary>
    /// Valida a atividade e retorna um resultado detalhado com o que foi alcançado
    /// vs o que era exigido.
    /// </summary>
    public static ActivityValidationDetail Validate(StravaActivity activity, Challenge challenge)
    {
        // ── Métricas reais da atividade Strava ────────────────────────────────
        var distanceKm      = activity.DistanceKm;
        var paceMinPerKm    = activity.PaceMinPerKm;   // min/km em decimal (ex: 6.5)
        var movingMinutes   = activity.MovingTime / 60.0;

        return challenge.ChallengeType.ToLowerInvariant() switch
        {
            // ─── CORRIDA: validação por distância mínima ──────────────────────
            // target_value = quilômetros que o corredor deve percorrer no mínimo
            // Exemplo: target_value = 10.0 → precisa correr pelo menos 10 km
            "corrida" => ValidateDistance(distanceKm, paceMinPerKm, movingMinutes, challenge.TargetValue),

            // ─── PACE: validação por velocidade máxima ────────────────────────
            // target_value = pace máximo permitido em min/km (decimal)
            // Exemplo: target_value = 6.5 → precisa correr a no máximo 6:30/km
            // Regra: pace MENOR = mais rápido = melhor → usuário precisa ter pace <= target
            "pace" => ValidatePace(distanceKm, paceMinPerKm, movingMinutes, challenge.TargetValue),

            // ─── Tipo não reconhecido ─────────────────────────────────────────
            _ => new ActivityValidationDetail
            {
                Passed             = false,
                ChallengeType      = challenge.ChallengeType,
                ActualDistanceKm   = Math.Round(distanceKm, 2),
                ActualPaceMinPerKm = Math.Round(paceMinPerKm, 2),
                ActualPaceFormatted    = FormatPace(paceMinPerKm),
                ActualMovingTimeMinutes = Math.Round(movingMinutes, 1),
                FailureReason      = $"Tipo de desafio '{challenge.ChallengeType}' não reconhecido. " +
                                     "Tipos válidos: 'corrida', 'pace'.",
                ProgressPercent    = 0
            }
        };
    }

    // ── Validação de Distância ────────────────────────────────────────────────

    private static ActivityValidationDetail ValidateDistance(
        double distanceKm,
        double paceMinPerKm,
        double movingMinutes,
        double requiredKm)
    {
        var passed   = distanceKm >= requiredKm;
        var progress = requiredKm > 0
            ? (int)Math.Min(100, Math.Round(distanceKm / requiredKm * 100))
            : 0;

        return new ActivityValidationDetail
        {
            Passed                  = passed,
            ChallengeType           = "corrida",
            ActualDistanceKm        = Math.Round(distanceKm, 2),
            ActualPaceMinPerKm      = Math.Round(paceMinPerKm, 2),
            ActualPaceFormatted     = FormatPace(paceMinPerKm),
            ActualMovingTimeMinutes = Math.Round(movingMinutes, 1),
            RequiredDistanceKm      = requiredKm,
            ProgressPercent         = progress,
            FailureReason           = passed ? null :
                $"Distância insuficiente: percorreu {distanceKm:F2} km " +
                $"de {requiredKm:F2} km exigidos ({progress}% concluído)."
        };
    }

    // ── Validação de Pace ─────────────────────────────────────────────────────

    private static ActivityValidationDetail ValidatePace(
        double distanceKm,
        double paceMinPerKm,
        double movingMinutes,
        double requiredMaxPace)
    {
        // Pace menor = mais rápido. O usuário precisa ter pace <= target.
        var passed = paceMinPerKm <= requiredMaxPace && distanceKm > 0;

        // Progresso: quanto mais rápido que o target, mais próximo de 100%
        // Se pace atual = 5.0 e target = 6.0 → está 17% mais rápido que o necessário
        int progress;
        if (distanceKm <= 0)
        {
            progress = 0;
        }
        else if (passed)
        {
            progress = 100;
        }
        else
        {
            // Inverte a lógica: distância percorrida do pace atual até o target
            progress = requiredMaxPace > 0
                ? (int)Math.Min(99, Math.Round(requiredMaxPace / paceMinPerKm * 100))
                : 0;
        }

        string? failureReason = null;
        if (!passed)
        {
            failureReason = distanceKm <= 0
                ? "Atividade sem distância registrada — não é uma corrida válida."
                : $"Pace muito lento: {FormatPace(paceMinPerKm)}/km " +
                  $"(limite máximo: {FormatPace(requiredMaxPace)}/km). " +
                  $"Precisa ser {paceMinPerKm - requiredMaxPace:F2} min/km mais rápido.";
        }

        return new ActivityValidationDetail
        {
            Passed                  = passed,
            ChallengeType           = "pace",
            ActualDistanceKm        = Math.Round(distanceKm, 2),
            ActualPaceMinPerKm      = Math.Round(paceMinPerKm, 2),
            ActualPaceFormatted     = FormatPace(paceMinPerKm),
            ActualMovingTimeMinutes = Math.Round(movingMinutes, 1),
            RequiredPaceMinPerKm    = requiredMaxPace,
            RequiredPaceFormatted   = FormatPace(requiredMaxPace),
            ProgressPercent         = progress,
            FailureReason           = failureReason
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Converte pace decimal (min/km) para string "mm:ss".
    /// Ex: 6.5 → "6:30"  |  6.0 → "6:00"  |  5.75 → "5:45"
    /// </summary>
    public static string FormatPace(double paceMinPerKm)
    {
        if (paceMinPerKm <= 0 || double.IsInfinity(paceMinPerKm) || double.IsNaN(paceMinPerKm))
            return "--:--";

        var totalSeconds = (int)Math.Round(paceMinPerKm * 60);
        var minutes      = totalSeconds / 60;
        var seconds      = totalSeconds % 60;
        return $"{minutes}:{seconds:D2}";
    }
}
