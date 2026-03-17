using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using StravaIntegration.Controllers;
using StravaIntegration.Middleware;
using StravaIntegration.Models.Options;
using StravaIntegration.Services;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// ═══════════════════════════════════════════════════════════════════════════════
// 1. CONFIGURAÇÃO – Options Pattern com validação em startup
// ═══════════════════════════════════════════════════════════════════════════════

builder.Services
    .AddOptions<StravaOptions>()
    .Bind(builder.Configuration.GetSection(StravaOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<SupabaseOptions>()
    .Bind(builder.Configuration.GetSection(SupabaseOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<AppOptions>()
    .Bind(builder.Configuration.GetSection(AppOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddHttpClient<StravaController>();

// ═══════════════════════════════════════════════════════════════════════════════
// 2. SUPABASE CLIENT (supabase-csharp)
// ═══════════════════════════════════════════════════════════════════════════════

var supabaseOptions = builder.Configuration
    .GetSection(SupabaseOptions.SectionName)
    .Get<SupabaseOptions>()!;


var supabaseClient = new Supabase.Client(
    supabaseOptions.Url,
    supabaseOptions.ServiceRoleKey, 
    new Supabase.SupabaseOptions
    {
        AutoRefreshToken = false,
        AutoConnectRealtime = false,
    }
);
await supabaseClient.InitializeAsync();
builder.Services.AddSingleton(supabaseClient);

// ═══════════════════════════════════════════════════════════════════════════════
// 3. HTTP CLIENT PARA O STRAVA
// ═══════════════════════════════════════════════════════════════════════════════

builder.Services.AddHttpClient<IStravaService, StravaService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.Add("Accept", "application/json");
    client.DefaultRequestHeaders.Add("User-Agent", "RunApp-StravaIntegration/1.0");
});

// ═══════════════════════════════════════════════════════════════════════════════
// 4. SERVIÇOS DE DOMÍNIO
// ═══════════════════════════════════════════════════════════════════════════════

builder.Services.AddScoped<IChallengeValidationService, ChallengeValidationService>();
builder.Services.AddScoped<IJoinChallengeService, JoinChallengeService>();
builder.Services.AddScoped<IRewardService, RewardService>();
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority            = $"{supabaseOptions.Url}/auth/v1";
        options.MetadataAddress      = $"{supabaseOptions.Url}/auth/v1/.well-known/openid-configuration";
        options.RequireHttpsMetadata = true;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,

            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(supabaseOptions.JwtSecret)),

            IssuerSigningKeyResolver = (token, securityToken, kid, parameters) =>
            {
                if (string.IsNullOrEmpty(kid))
                    return [new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(supabaseOptions.JwtSecret))];
                return [];
            },

            ValidateIssuer   = true,
            ValidIssuer      = $"{supabaseOptions.Url}/auth/v1",

            ValidateAudience = true,
            ValidAudience    = "authenticated",

            ValidateLifetime = true,
            ClockSkew        = TimeSpan.FromSeconds(60),
            NameClaimType    = "sub",
        };

        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = ctx =>
            {
                var logger = ctx.HttpContext.RequestServices
                    .GetRequiredService<ILogger<Program>>();
                var header = ctx.Request.Headers.Authorization.ToString();
                var preview = header.Length > 40 ? header[..40] + "..." : header;
                logger.LogWarning("JWT inválido: {Error} | Header: {Header}",
                    ctx.Exception.Message, preview);
                return Task.CompletedTask;
            },
            OnTokenValidated = ctx =>
            {
                var logger = ctx.HttpContext.RequestServices
                    .GetRequiredService<ILogger<Program>>();
                var userId = ctx.Principal?.FindFirst("sub")?.Value;
                logger.LogDebug("JWT validado. UserId={UserId}", userId);
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

// ═══════════════════════════════════════════════════════════════════════════════
// 6. CONTROLLERS, CORS E SWAGGER
// ═══════════════════════════════════════════════════════════════════════════════

builder.Services.AddControllers();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy
            .AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title   = "Strava Integration API",
        Version = "v1",
        Description = "Microsserviço de integração com o Strava para validação de desafios de corrida."
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT do Supabase. Formato: Bearer {token}",
        Name        = "Authorization",
        In          = ParameterLocation.Header,
        Type        = SecuritySchemeType.ApiKey,
        Scheme      = "Bearer"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id   = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseGlobalExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Strava Integration API v1");
        c.RoutePrefix = string.Empty;
    });
}

app.UseHttpsRedirection();
app.UseCors("AllowFrontend");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
