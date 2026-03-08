using System.Net;
using System.Text.Json;
using StravaIntegration.Exceptions;

namespace StravaIntegration.Middleware;

/// <summary>
/// Middleware global de tratamento de exceções.
/// Garante que exceções de domínio retornem respostas HTTP bem formadas
/// sem vazar stack traces para o cliente.
/// </summary>
public sealed class GlobalExceptionHandlerMiddleware(
    RequestDelegate next,
    ILogger<GlobalExceptionHandlerMiddleware> logger)
{
    private static readonly JsonSerializerOptions JsonOpts =
        new(JsonSerializerDefaults.Web);

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var (statusCode, message) = exception switch
        {
            TokenNotFoundException e    => (HttpStatusCode.NotFound, e.Message),
            ChallengeNotFoundException e => (HttpStatusCode.NotFound, e.Message),
            RewardNotFoundException e    => (HttpStatusCode.NotFound, e.Message),
            AlreadyRewardedException e  => (HttpStatusCode.Conflict, e.Message),
            StravaAuthException e       => (HttpStatusCode.BadGateway, e.Message),
            StravaApiException e when e.StatusCode == 429
                                        => (HttpStatusCode.TooManyRequests,
                                            "Limite de requisições do Strava atingido. Tente novamente em instantes."),
            StravaApiException e        => (HttpStatusCode.BadGateway, e.Message),
            OperationCanceledException  => (HttpStatusCode.RequestTimeout, "Requisição cancelada."),
            _                           => (HttpStatusCode.InternalServerError,
                                            "Erro interno do servidor.")
        };

        // Log detalhado apenas para 5xx
        if ((int)statusCode >= 500)
            logger.LogError(exception, "Erro não tratado: {Message}", exception.Message);
        else
            logger.LogWarning(exception, "Erro de domínio: {Message}", exception.Message);

        context.Response.StatusCode  = (int)statusCode;
        context.Response.ContentType = "application/json";

        var body = JsonSerializer.Serialize(new
        {
            error      = message,
            statusCode = (int)statusCode,
            traceId    = context.TraceIdentifier
        }, JsonOpts);

        await context.Response.WriteAsync(body);
    }
}

// Extension para facilitar o registro no pipeline
public static class GlobalExceptionHandlerExtensions
{
    public static IApplicationBuilder UseGlobalExceptionHandler(this IApplicationBuilder app)
        => app.UseMiddleware<GlobalExceptionHandlerMiddleware>();
}
