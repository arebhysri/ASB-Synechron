using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace CardControl.UnitTest.Support
{
    /// <summary>
    /// Plays the partner's side in tests: creates an RSA key pair and encrypts a PAN as a compact JWE,
    /// exactly like starter/tools/jwe-tool.cs does (RSA-OAEP-256 + A256GCM).
    /// </summary>
    public sealed class TestJwe : IDisposable
    {
        // The key ID we put in the JWE header and in JweOptions.KeyId.
        public const string KeyId = "test-key-1";

        // The bank's RSA key pair. 2048 bits is enough for tests and faster than the tool's 3072.
        private readonly RSA _rsa = RSA.Create(2048);

        public TestJwe()
        {
            // JwePanDecryptor reads the private key from a PEM file, so we write one to a temp file.
            PrivateKeyPath = Path.Combine(Path.GetTempPath(), $"card-control-test-{Guid.NewGuid():N}.pem");
            File.WriteAllText(PrivateKeyPath, _rsa.ExportPkcs8PrivateKeyPem());
        }

        /// <summary>Path of the temp PEM file holding the private key.</summary>
        public string PrivateKeyPath { get; }

        /// <summary>Encrypts <paramref name="plaintext"/> the way the partner does. Header fields can be overridden for negative tests.</summary>
        public string Encrypt(string plaintext, string alg = "RSA-OAEP-256", string enc = "A256GCM", string kid = KeyId)
        {
            // 1. Protected header (JSON), base64url encoded. It is also the AES-GCM "associated data".
            var header = $$"""{"alg":"{{alg}}","enc":"{{enc}}","kid":"{{kid}}"}""";
            var encodedHeader = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(header));

            // 2. Random 256-bit content encryption key (CEK) and 96-bit IV for AES-GCM.
            var cek = RandomNumberGenerator.GetBytes(32);
            var iv = RandomNumberGenerator.GetBytes(12);

            // 3. Encrypt the PAN with AES-GCM; the tag proves nothing was changed.
            var data = Encoding.UTF8.GetBytes(plaintext);
            var cipherText = new byte[data.Length];
            var tag = new byte[16];
            using (var aes = new AesGcm(cek, tagSizeInBytes: 16))
                aes.Encrypt(iv, data, cipherText, tag, Encoding.ASCII.GetBytes(encodedHeader));

            // 4. Wrap the CEK with the bank's RSA public key (RSA-OAEP with SHA-256).
            var encryptedKey = _rsa.Encrypt(cek, RSAEncryptionPadding.OaepSHA256);

            // 5. Compact serialisation: header.encryptedKey.iv.ciphertext.tag
            return string.Join('.',
                encodedHeader,
                Base64Url.EncodeToString(encryptedKey),
                Base64Url.EncodeToString(iv),
                Base64Url.EncodeToString(cipherText),
                Base64Url.EncodeToString(tag));
        }

        public void Dispose()
        {
            // Clean up: the key object and the temp PEM file.
            _rsa.Dispose();
            File.Delete(PrivateKeyPath);
        }
    }
}
