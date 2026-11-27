using CardControl.Interface;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace CardControl.IntegrationTest.Support
{
    /// <summary>
    /// Runs the real API in memory (real Program.cs, controller, auth, decryptor, exception handler),
    /// swapping only what lives outside it: the IdP keys, the bank key file and the two downstream services.
    /// </summary>
    public sealed class CardControlApiFactory : WebApplicationFactory<Program>
    {
        public TestKeys Keys { get; } = new();
        public FakeTokenisation Tokenisation { get; } = new();
        public FakeCardPlatform CardPlatform { get; } = new();
        public CapturingLoggerProvider Logs { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");

            // Point the decryptor at the test bank key instead of starter/.dev-keys.
            builder.UseSetting("Jwe:PrivateKeyPath", Keys.BankPrivateKeyPath);
            builder.UseSetting("Jwe:KeyId", TestKeys.JweKeyId);

            // Capture all logs (Trace and up) for the PAN-leak test.
            builder.ConfigureLogging(logging =>
            {
                logging.AddProvider(Logs);
                logging.SetMinimumLevel(LogLevel.Trace);
            });

            builder.ConfigureTestServices(services =>
            {
                // Replace the HTTP clients with in-memory fakes.
                services.RemoveAll<ITokenisationClient>();
                services.RemoveAll<ICardPlatformClient>();
                services.AddSingleton<ITokenisationClient>(Tokenisation);
                services.AddSingleton<ICardPlatformClient>(CardPlatform);

                // Trust our test signing key instead of downloading JWKS from localhost:5090.
                // Everything else (issuer, audience, lifetime, role) is validated exactly as in production code.
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    var config = new OpenIdConnectConfiguration { Issuer = TestKeys.Issuer };
                    config.SigningKeys.Add(Keys.SigningKey);
                    options.Configuration = config;
                    options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(config);
                });
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
                Keys.Dispose();
        }
    }
}
