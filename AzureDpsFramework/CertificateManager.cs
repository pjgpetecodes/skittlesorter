using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace AzureDpsFramework
{
    public static class CertificateManager
    {
        public static (string csrPem, string keyPem) GenerateCsr(string commonName, string algorithm = "RSA", int rsaKeySize = 2048, string hashAlg = "SHA256")
        {
            if (algorithm.Equals("RSA", StringComparison.OrdinalIgnoreCase))
            {
                using var rsa = RSA.Create(rsaKeySize);
                var dn = new X500DistinguishedName($"CN={commonName}");
                var req = new CertificateRequest(dn, rsa, MapHash(hashAlg), RSASignaturePadding.Pkcs1);
                byte[] csrDer = req.CreateSigningRequest();
                string csrPem = WritePem("CERTIFICATE REQUEST", csrDer);
                byte[] pkcs8 = rsa.ExportPkcs8PrivateKey();
                string keyPem = WritePem("PRIVATE KEY", pkcs8);
                return (csrPem, keyPem);
            }
            else
            {
                using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
                var dn = new X500DistinguishedName($"CN={commonName}");
                var req = new CertificateRequest(dn, ecdsa, MapHash(hashAlg));
                byte[] csrDer = req.CreateSigningRequest();
                string csrPem = WritePem("CERTIFICATE REQUEST", csrDer);
                byte[] pkcs8 = ecdsa.ExportPkcs8PrivateKey();
                string keyPem = WritePem("PRIVATE KEY", pkcs8);
                return (csrPem, keyPem);
            }
        }

        public static void SaveText(string path, string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            File.WriteAllText(path, content);
        }

        public static void SaveIssuedCertificatePem(string path, string pemChain)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            File.WriteAllText(path, pemChain);
        }

        public static X509Certificate2 LoadX509WithPrivateKey(string certPemPath, string keyPemPath)
        {
            // Combine cert + key from PEM files
            var cert = X509Certificate2.CreateFromPemFile(certPemPath, keyPemPath);
            // Ensure private key is persisted (optional):
            return cert;
        }

        private static HashAlgorithmName MapHash(string hash)
        {
            return hash.ToUpperInvariant() switch
            {
                "SHA384" => HashAlgorithmName.SHA384,
                "SHA512" => HashAlgorithmName.SHA512,
                _ => HashAlgorithmName.SHA256
            };
        }

        private static string WritePem(string label, byte[] der)
        {
            var builder = new StringBuilder();
            builder.AppendLine($"-----BEGIN {label}-----");
            builder.AppendLine(Convert.ToBase64String(der, Base64FormattingOptions.InsertLineBreaks));
            builder.AppendLine($"-----END {label}-----");
            return builder.ToString();
        }
    }
}
