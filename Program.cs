using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using StravaIntegration.Middleware;
using StravaIntegration.Models.Options;
using StravaIntegration.Services;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

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

builder.Services.AddHttpClient<IStravaService, StravaService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.Add("Accept", "application/json");
    client.DefaultRequestHeaders.Add("User-Agent", "RunApp-StravaIntegration/1.0");
});

builder.Services.AddScoped<IChallengeValidationService, ChallengeValidationService>();

var httpClient = new HttpClient();
var jwksUrl = $"{supabaseOptions.Url}/auth/v1/.well-known/jwks.json";
var jwksJson = await httpClient.GetStringAsync(jwksUrl);
var jwks = new JsonWebKeySet(jwksJson);

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = jwks.GetSigningKeys(), 

            ValidateIssuer = true,
            ValidIssuer = $"{supabaseOptions.Url}/auth/v1",

            ValidateAudience = true,
            ValidAudience = "authenticated",

            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "sub"
        };

        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = ctx =>
            {
                var logger = ctx.HttpContext.RequestServices
                    .GetRequiredService<ILogger<Program>>();
                logger.LogWarning("JWT inválido: {Error}", ctx.Exception.Message);
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddControllers();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        var frontendUrl = builder.Configuration["App:FrontendUrl"] ?? "https://runnerproject.vercel.app"; 
        
        policy
            .WithOrigins(frontendUrl)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

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