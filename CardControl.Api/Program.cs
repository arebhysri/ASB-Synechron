using CardControl.Api.Errors;
using CardControl.Data.Options;
using CardControl.Interface;
using CardControl.Repository.Downstream;
using CardControl.Repository.Entitlements;
using CardControl.Service.CardControl;
using CardControl.Service.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// ---- Options from appsettings.json, validated at start-up (fail fast on missing settings) ----
builder.Services.AddOptions<AuthOptions>().Bind(builder.Configuration.GetSection(AuthOptions.SectionName))
    .ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<DownstreamOptions>().Bind(builder.Configuration.GetSection(DownstreamOptions.SectionName))
    .ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<EntitlementOptions>().Bind(builder.Configuration.GetSection(EntitlementOptions.SectionName))
    .ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<JweOptions>().Bind(builder.Configuration.GetSection(JweOptions.SectionName))
    .ValidateDataAnnotations().ValidateOnStart();

var auth = builder.Configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();
var downstream = builder.Configuration.GetSection(DownstreamOptions.SectionName).Get<DownstreamOptions>() ?? new DownstreamOptions();

// ---- Authentication: JWT from the identity provider (signature via JWKS, issuer, audience, lifetime) ----
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = auth.Authority;
        options.Audience = auth.Audience;
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment(); // mock IdP is plain HTTP
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = auth.Authority,
            ValidateAudience = true,
            ValidAudience = auth.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            RoleClaimType = "roles",
        };
    });

// ---- Authorization: token must carry the CardControl.Write role ----
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(AuthOptions.PolicyName, p => p.RequireAuthenticatedUser().RequireRole(auth.RequiredRole));

// ---- Downstream HTTP clients ----
builder.Services.AddHttpClient<ITokenisationClient, TokenisationClient>(c =>
{
    c.BaseAddress = new Uri(downstream.TokenisationBaseUrl.TrimEnd('/') + "/");
    c.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddHttpClient<ICardPlatformClient, CardPlatformClient>(c =>
{
    c.BaseAddress = new Uri(downstream.CardPlatformBaseUrl.TrimEnd('/') + "/");
    c.Timeout = TimeSpan.FromSeconds(10);
});

// ---- Application services ----
builder.Services.AddSingleton<IPanDecryptor, JwePanDecryptor>();
builder.Services.AddSingleton<IEntitlementService, EntitlementService>();
builder.Services.AddScoped<ICardControlService, CardControlService>();

// ---- Errors as RFC 9457 Problem Details ----
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<CardControlExceptionHandler>();

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program { } // for WebApplicationFactory in integration tests
