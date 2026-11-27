using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CardControl.Data.Options;
using CardControl.Interface;
using CardControl.Model.Errors;
using Microsoft.Extensions.Options;

namespace CardControl.Service.Security
{
    /// <summary>
    /// Decrypts the partner's compact JWE (RSA-OAEP-256 + A256GCM), the reverse of starter/tools/jwe-tool.cs.
    /// </summary>
    public sealed class JwePanDecryptor : IPanDecryptor, IDisposable
    {
        private readonly RSA _privateKey;
        private readonly string _keyId;

        public JwePanDecryptor(IOptions<JweOptions> options)
        {
            _keyId = options.Value.KeyId;
            _privateKey = RSA.Create();
            _privateKey.ImportFromPem(File.ReadAllText(options.Value.PrivateKeyPath));
        }

        public string Decrypt(string encryptedPan)
        {
            // Compact JWE: header.encryptedKey.iv.ciphertext.tag
            var parts = encryptedPan?.Split('.') ?? [];
            if (parts.Length != 5)
                throw Invalid();

            try
            {
                using var header = JsonDocument.Parse(Base64Url.DecodeFromChars(parts[0]));
                var root = header.RootElement;
                if (root.GetProperty("alg").GetString() != "RSA-OAEP-256" ||
                    root.GetProperty("enc").GetString() != "A256GCM" ||
                    (root.TryGetProperty("kid", out var kid) && kid.GetString() != _keyId))
                    throw Invalid();

                var cek = _privateKey.Decrypt(Base64Url.DecodeFromChars(parts[1]), RSAEncryptionPadding.OaepSHA256);
                var iv = Base64Url.DecodeFromChars(parts[2]);
                var cipherText = Base64Url.DecodeFromChars(parts[3]);
                var tag = Base64Url.DecodeFromChars(parts[4]);
                var plainText = new byte[cipherText.Length];

                using (var aes = new AesGcm(cek, tagSizeInBytes: 16))
                    aes.Decrypt(iv, cipherText, tag, plainText, Encoding.ASCII.GetBytes(parts[0]));

                var pan = Encoding.UTF8.GetString(plainText);
                if (pan.Length is < 12 or > 19 || !pan.All(char.IsAsciiDigit))
                    throw Invalid();

                return pan;
            }
            catch (CardControlException)
            {
                throw;
            }
            catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException
                                           or KeyNotFoundException or InvalidOperationException or ArgumentException)
            {
                // Crypto/format errors become one generic 400: no details or PAN leave this class.
                throw Invalid();
            }
        }

        private static CardControlException Invalid() =>
            new(HttpStatusCode.BadRequest, ErrorCodes.InvalidEncryptedPan,
                "The encrypted card number could not be decrypted.");

        public void Dispose() => _privateKey.Dispose();
    }
}
