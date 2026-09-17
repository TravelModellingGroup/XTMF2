using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace XTMF2.Bus;

/// <summary>
/// Shared TLS certificate and token-authentication helpers for remote RunServers.
/// </summary>
public static class RunServerSecurity
{
    public const int NonceLength = 32;
    public const int TokenMinimumLength = 16;

    public static X509Certificate2 CreateCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=XTMF2 RunServer",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, false));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(10));
    }

    public static string CreateToken()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

    public static string GetFingerprint(X509Certificate2 certificate)
        => Convert.ToHexString(SHA256.HashData(certificate.RawData));

    public static bool FingerprintMatches(X509Certificate2 certificate, string expectedFingerprint)
    {
        var normalized = expectedFingerprint.Replace(":", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Trim();
        if (normalized.Length != 64)
            return false;

        try
        {
            return CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(certificate.RawData),
                Convert.FromHexString(normalized));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static byte[] ComputeProof(string token, ReadOnlySpan<byte> nonce)
        => HMACSHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token), nonce);

    public static void SaveSetup(string directory, out string token, out string fingerprint)
    {
        Directory.CreateDirectory(directory);
        using var certificate = CreateCertificate();
        token = CreateToken();
        fingerprint = GetFingerprint(certificate);
        File.WriteAllText(Path.Combine(directory, "runserver-token.txt"), token + Environment.NewLine);
        File.WriteAllText(Path.Combine(directory, "runserver-cert.pem"), certificate.ExportCertificatePem());
        using var rsa = certificate.GetRSAPrivateKey() ?? throw new InvalidOperationException("The generated certificate has no private key.");
        File.WriteAllText(Path.Combine(directory, "runserver-key.pem"), rsa.ExportPkcs8PrivateKeyPem());
    }

    public static X509Certificate2 LoadCertificate(string directory)
    {
        // CreateFromPemFile uses an ephemeral private key. Windows Schannel requires
        // the server certificate key to be persisted before TLS authentication.
        using var pemCertificate = X509Certificate2.CreateFromPemFile(
            Path.Combine(directory, "runserver-cert.pem"),
            Path.Combine(directory, "runserver-key.pem"));
        var pkcs12 = pemCertificate.Export(X509ContentType.Pkcs12);
        return X509CertificateLoader.LoadPkcs12(
            pkcs12,
            password: null,
            X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.UserKeySet);
    }

    public static string LoadToken(string directory)
        => File.ReadAllText(Path.Combine(directory, "runserver-token.txt")).Trim();
}
