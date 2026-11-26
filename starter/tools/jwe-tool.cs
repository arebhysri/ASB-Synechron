#:property PublishAot=false
#:property Nullable=enable
#:property ImplicitUsings=enable

// jwe-tool: dev helper for the take-home exercise. Run with: dotnet run tools/jwe-tool.cs -- <command> ...
//
//   keygen   Create the bank's dev RSA key pair. Your API holds the private key; the partner encrypts
//            with the public key.
//   encrypt  Encrypt a PAN as a compact JWE (alg RSA-OAEP-256, enc A256GCM), the way the partner would.
//
// Do not treat this as an example of production code - it is deliberately minimal.

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

try
{
    return args switch
    {
        ["keygen", .. var rest] => KeyGen(new CommandLine(rest)),
        ["encrypt", .. var rest] => Encrypt(new CommandLine(rest)),
        _ => Usage(),
    };
}
catch (Exception ex) when (ex is ToolException or IOException or CryptographicException or ArgumentException)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 1;
}

static int KeyGen(CommandLine cmd)
{
    var kid = ValidKid(cmd.Get("kid") ?? "bank-dev-1");
    var outDir = cmd.Get("out") ?? ".dev-keys";
    var privatePath = Path.Combine(outDir, $"{kid}.private.pem");
    var publicPath = Path.Combine(outDir, $"{kid}.public.pem");

    if (!cmd.Has("force") && (File.Exists(privatePath) || File.Exists(publicPath)))
        throw new ToolException($"'{privatePath}' already exists. Use --force to overwrite.");

    Directory.CreateDirectory(outDir);
    using var rsa = RSA.Create(3072);
    File.WriteAllText(privatePath, rsa.ExportPkcs8PrivateKeyPem());
    File.WriteAllText(publicPath, rsa.ExportSubjectPublicKeyInfoPem());

    Console.WriteLine($"kid:         {kid}");
    Console.WriteLine($"private key: {privatePath}  (for your API)");
    Console.WriteLine($"public key:  {publicPath}  (what the partner encrypts with)");
    return 0;
}

static int Encrypt(CommandLine cmd)
{
    var keyPath = cmd.Get("key") ?? throw new ToolException("--key <public-key.pem> is required.");
    var kid = ValidKid(cmd.Get("kid") ?? Path.GetFileName(keyPath).Replace(".public.pem", "", StringComparison.Ordinal));

    if (cmd.Positional is not [var pan])
        throw new ToolException("Expected exactly one PAN.");
    if (pan.Length is < 12 or > 19 || !pan.All(char.IsAsciiDigit))
        throw new ToolException("PAN must be 12-19 digits.");

    using var rsa = RSA.Create();
    rsa.ImportFromPem(File.ReadAllText(keyPath));

    Console.WriteLine(Jwe.Encrypt(Encoding.UTF8.GetBytes(pan), rsa, kid));
    return 0;
}

static string ValidKid(string kid) =>
    Regex.IsMatch(kid, "^[A-Za-z0-9._-]{1,64}$")
        ? kid
        : throw new ToolException("kid may only contain letters, digits, '.', '_' and '-' (max 64).");

static int Usage()
{
    Console.Error.WriteLine("""
        Usage:
          dotnet run tools/jwe-tool.cs -- keygen [--kid bank-dev-1] [--out .dev-keys] [--force]
          dotnet run tools/jwe-tool.cs -- encrypt --key .dev-keys/bank-dev-1.public.pem [--kid bank-dev-1] <PAN>

        keygen writes <kid>.private.pem (PKCS#8) and <kid>.public.pem (SPKI), RSA 3072.
        encrypt prints a compact JWE: alg RSA-OAEP-256, enc A256GCM, kid in the protected header,
        plaintext = the PAN digits as UTF-8. kid defaults to the key file name.
        """);
    return 2;
}

static class Jwe
{
    // RFC 7516 compact serialisation: header.encryptedKey.iv.ciphertext.tag
    public static string Encrypt(byte[] plaintext, RSA recipientPublicKey, string kid)
    {
        var protectedHeader = $$"""{"alg":"RSA-OAEP-256","enc":"A256GCM","kid":"{{kid}}"}""";
        var encodedHeader = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(protectedHeader));

        var cek = RandomNumberGenerator.GetBytes(32);
        try
        {
            var iv = RandomNumberGenerator.GetBytes(12);
            var ciphertext = new byte[plaintext.Length];
            var tag = new byte[16];

            using (var aes = new AesGcm(cek, tagSizeInBytes: 16))
                aes.Encrypt(iv, plaintext, ciphertext, tag, associatedData: Encoding.ASCII.GetBytes(encodedHeader));

            var encryptedKey = recipientPublicKey.Encrypt(cek, RSAEncryptionPadding.OaepSHA256);

            return string.Join('.',
                encodedHeader,
                Base64Url.EncodeToString(encryptedKey),
                Base64Url.EncodeToString(iv),
                Base64Url.EncodeToString(ciphertext),
                Base64Url.EncodeToString(tag));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(cek);
        }
    }
}

sealed class CommandLine
{
    private static readonly HashSet<string> Flags = ["force"];
    private readonly Dictionary<string, string> _named = [];

    public CommandLine(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
            {
                Positional.Add(args[i]);
                continue;
            }

            var name = args[i][2..];
            if (Flags.Contains(name))
                _named[name] = "true";
            else if (i + 1 < args.Length)
                _named[name] = args[++i];
            else
                throw new ToolException($"--{name} needs a value.");
        }
    }

    public List<string> Positional { get; } = [];
    public string? Get(string name) => _named.GetValueOrDefault(name);
    public bool Has(string name) => _named.ContainsKey(name);
}

sealed class ToolException(string message) : Exception(message);
