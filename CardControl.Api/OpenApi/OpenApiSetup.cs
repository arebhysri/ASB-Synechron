using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace CardControl.Api.OpenApi
{
    /// <summary>
    /// OpenAPI document (built into .NET 10) and Swagger UI, exposed in Development only.
    /// Document: /openapi/v1.json. UI: /swagger.
    /// </summary>
    public static class OpenApiSetup
    {
        private const string BearerScheme = "Bearer";
        private const string DocumentUrl = "/openapi/v1.json";

        public static IServiceCollection AddCardControlOpenApi(this IServiceCollection services)
        {
            services.AddOpenApi(options => options.AddDocumentTransformer(AddInfoAndBearerScheme));
            return services;
        }

        public static WebApplication UseCardControlOpenApi(this WebApplication app)
        {
            // Not exposed outside Development: the API contract is shared with partners as a file instead.
            if (!app.Environment.IsDevelopment())
                return app;

            app.MapOpenApi();
            app.UseSwaggerUI(ui =>
            {
                ui.SwaggerEndpoint(DocumentUrl, "Card Control API v1");
                ui.DocumentTitle = "Card Control API";
            });
            return app;
        }

        /// <summary>
        /// Sets the title/description and adds a Bearer (JWT) security scheme, so Swagger UI shows an
        /// Authorize button where an access token from the identity provider can be pasted.
        /// </summary>
        private static Task AddInfoAndBearerScheme(
            OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
        {
            document.Info.Title = "Card Control API";
            document.Info.Description =
                "Lock and unlock a corporate card. The PAN is sent as a compact JWE (RSA-OAEP-256 / A256GCM) " +
                "encrypted with the bank's public key. Errors are RFC 9457 Problem Details with a 'code' field.";

            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes[BearerScheme] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "Access token from the identity provider with the CardControl.Write role.",
            };

            // Every endpoint needs the token.
            document.Security ??= [];
            document.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(BearerScheme, document)] = [],
            });
            return Task.CompletedTask;
        }
    }
}
