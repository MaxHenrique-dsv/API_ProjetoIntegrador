using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
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

// ═══════════════════════════════════════════════════════════════════════════════
// 2. SUPABASE CLIENT (supabase-csharp)
// ═══════════════════════════════════════════════════════════════════════════════

var supabaseOptions = builder.Configuration
    .GetSection(SupabaseOptions.SectionName)
    .Get<SupabaseOptions>()!;

// O SDK do Supabase usa o service role key para operações server-side
// (bypass de RLS), garantindo que o microsserviço tenha acesso total.
var supabaseClient = new Supabase.Client(
    supabaseOptions.Url,
    supabaseOptions.ServiceRoleKey,  // ⚠️ NUNCA expor no frontend
    new Supabase.SupabaseOptions
    {
        AutoRefreshToken = false,
        AutoConnectRealtime = false,
    }
);
await supabaseClient.InitializeAsync();

// Registra como Singleton – o cliente é thread-safe
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

// ═══════════════════════════════════════════════════════════════════════════════
// 5. AUTENTICAÇÃO – Valida JWT do Supabase (HS256 e ES256)
// ═══════════════════════════════════════════════════════════════════════════════

// O Supabase emite dois tipos de JWT dependendo do método de login:
//   HS256 (simétrico)   → login com email/senha  → validado com JwtSecret
//   ES256 (assimétrico) → login com OAuth Google → validado via JWKS endpoint
//
// Usando Authority + MetadataAddress, o middleware busca automaticamente
// a chave pública correta pelo "kid" do token ES256, e usa IssuerSigningKey
// como fallback para tokens HS256.
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // JWKS: o Supabase expõe as chaves públicas neste endpoint.
        // O middleware baixa e cacheia automaticamente.
        options.Authority            = $"{supabaseOptions.Url}/auth/v1";
        options.MetadataAddress      = $"{supabaseOptions.Url}/auth/v1/.well-known/openid-configuration";
        options.RequireHttpsMetadata = true;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,

            // Chave simétrica para tokens HS256 (login email/senha)
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(supabaseOptions.JwtSecret)),

            // Resolver: tokens ES256 não têm kid vazio — o middleware
            // os resolve automaticamente via MetadataAddress/Authority.
            // Tokens HS256 não têm kid, caem aqui como fallback.
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

    // Suporte a JWT no Swagger UI
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

// ═══════════════════════════════════════════════════════════════════════════════
// 7. HEALTH CHECKS
// ═══════════════════════════════════════════════════════════════════════════════

builder.Services.AddHealthChecks();

// ═══════════════════════════════════════════════════════════════════════════════
// PIPELINE
// ═══════════════════════════════════════════════════════════════════════════════

var app = builder.Build();

// ① Global exception handler – deve ser o primeiro middleware
app.UseGlobalExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Strava Integration API v1");
        c.RoutePrefix = string.Empty; // Swagger na raiz em dev
    });
}

app.UseHttpsRedirection();
app.UseCors("AllowFrontend");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
