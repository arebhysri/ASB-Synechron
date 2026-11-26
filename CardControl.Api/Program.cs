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

// Add services to the container.
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));
builder.Services.Configure<DownstreamOptions>(builder.Configuration.GetSection(DownstreamOptions.SectionName));
builder.Services.Configure<EntitlementOptions>(builder.Configuration.GetSection(EntitlementOptions.SectionName));
builder.Services.Configure<JweOptions>(builder.Configuration.GetSection(JweOptions.SectionName));

var auth = builder.Configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>()!;
var downstream = builder.Configuration.GetSection(DownstreamOptions.SectionName).Get<DownstreamOptions>()!;

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = auth.Authority;
        options.Audience = auth.Audience;
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
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

builder.Services.AddAuthorizationBuilder().AddPolicy("CardControlWrite", p => p.RequireAuthenticatedUser().RequireRole(auth.RequiredRole));
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

builder.Services.AddSingleton<IPanDecryptor, JwePanDecryptor>();
builder.Services.AddSingleton<IEntitlementService, EntitlementService>();
builder.Services.AddScoped<ICardControlService, CardControlService>();

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<CardControlExceptionHandler>();


builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();
app.UseExceptionHandler();
app.UseStatusCodePages();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
public partial class Program { }
