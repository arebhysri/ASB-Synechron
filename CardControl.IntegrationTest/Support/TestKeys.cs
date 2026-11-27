using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace CardControl.IntegrationTest.Support
{
    /// <summary>
    /// Everything the "outside world" signs or encrypts, created locally so tests need no running services:
    /// - an identity-provider signing key, to issue JWTs like the mock IdP does;
    /// - the bank's JWE key pair, to encrypt PANs like the partner does.
    /// </summary>
    public sealed class TestKeys : IDisposable
    {
        public const string Issuer = "http://localhost:5090";        // same as appsettings Auth:Authority
        public const string Audience = "api://card-control";          // same as appsettings Auth:Audience
        public const string Role = "CardControl.Write";
        public const string JweKeyId = "test-key-1";

        private readonly RSA _idpKey = RSA.Create(2048);
        private readonly RSA _bankKey = RSA.Create(2048);

        public TestKeys()
        {
            SigningKey = new RsaSecurityKey(_idpKey) { KeyId = "test-idp-key" };

            // The API reads the bank private key from a PEM file (Jwe:PrivateKeyPath).
            BankPrivateKeyPath = Path.Combine(Path.GetTempPath(), $"card-control-it-{Guid.NewGuid():N}.pem");
            File.WriteAllText(BankPrivateKeyPath, _bankKey.ExportPkcs8PrivateKeyPem());
        }

        /// <summary>Public/private key the fake IdP signs with; the API is told to trust it.</summary>
        public RsaSecurityKey SigningKey { get; }

        public string BankPrivateKeyPath { get; }

        /// <summary>Creates an access token like the mock IdP (RS256, iss, aud, roles, exp).</summary>
        public string CreateToken(
            string[]? roles = null,
            string audience = Audience,
            DateTime? expires = null,
            SecurityKey? signWith = null)
        {
            var now = DateTime.UtcNow;
            var claims = new Dictionary<string, object> { ["azp"] = "partner-app" };
            if (roles is { Length: > 0 })
                claims["roles"] = roles;   // JSON array, like Entra ID app roles

            var descriptor = new SecurityTokenDescriptor
            {
                Issuer = Issuer,
                Audience = audience,
                NotBefore = now.AddMinutes(-10),
                IssuedAt = now.AddMinutes(-10),
                Expires = expires ?? now.AddMinutes(30),
                Claims = claims,
                SigningCredentials = new SigningCredentials(signWith ?? SigningKey, SecurityAlgorithms.RsaSha256),
            };
            return new JsonWebTokenHandler().CreateToken(descriptor);
        }

        /// <summary>Encrypts a PAN as compact JWE, like starter/tools/jwe-tool.cs.</summary>
        public string EncryptPan(string pan)
        {
            var encodedHeader = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(
                $$"""{"alg":"RSA-OAEP-256","enc":"A256GCM","kid":"{{JweKeyId}}"}"""));
            var cek = RandomNumberGenerator.GetBytes(32);
            var iv = RandomNumberGenerator.GetBytes(12);
            var data = Encoding.UTF8.GetBytes(pan);
            var cipherText = new byte[data.Length];
            var tag = new byte[16];
            using (var aes = new AesGcm(cek, tagSizeInBytes: 16))
                aes.Encrypt(iv, data, cipherText, tag, Encoding.ASCII.GetBytes(encodedHeader));
            var encryptedKey = _bankKey.Encrypt(cek, RSAEncryptionPadding.OaepSHA256);

            return string.Join('.', encodedHeader, Base64Url.EncodeToString(encryptedKey),
                Base64Url.EncodeToString(iv), Base64Url.EncodeToString(cipherText), Base64Url.EncodeToString(tag));
        }

        public void Dispose()
        {
            _idpKey.Dispose();
            _bankKey.Dispose();
            File.Delete(BankPrivateKeyPath);
        }
    }
}
