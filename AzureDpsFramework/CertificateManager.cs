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

        /// <summary>
        /// Generates a self-signed X.509 certificate for testing/development purposes.
        /// Useful for X.509 attestation scenarios where you need a bootstrap certificate.
        /// </summary>
        /// <param name="commonName">The Common Name (CN) for the certificate subject.</param>
        /// <param name="validityDays">Number of days the certificate is valid (default: 365).</param>
        /// <param name="algorithm">Algorithm to use: "RSA" or "ECDSA" (default: RSA).</param>
        /// <param name="rsaKeySize">RSA key size in bits (default: 2048).</param>
        /// <returns>Tuple of (certificatePem, privateKeyPem) as PEM strings.</returns>
        public static (string certificatePem, string privateKeyPem) GenerateSelfSignedCertificate(
            string commonName, 
            int validityDays = 365, 
            string algorithm = "RSA", 
            int rsaKeySize = 2048)
        {
            if (algorithm.Equals("RSA", StringComparison.OrdinalIgnoreCase))
            {
                using var rsa = RSA.Create(rsaKeySize);
                var dn = new X500DistinguishedName($"CN={commonName}");
                var req = new CertificateRequest(dn, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                
                // Add extensions
                req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
                req.CertificateExtensions.Add(new X509KeyUsageExtension(
                    X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
                req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
                    new OidCollection { new Oid("1.3.6.1.5.5.7.3.2") }, // Client Authentication
                    true));
                
                // Create self-signed certificate
                var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(validityDays));
                
                // Export certificate to PEM
                byte[] certDer = cert.Export(X509ContentType.Cert);
                string certPem = WritePem("CERTIFICATE", certDer);
                
                // Export private key to PEM (PKCS#8)
                byte[] keyPkcs8 = rsa.ExportPkcs8PrivateKey();
                string keyPem = WritePem("PRIVATE KEY", keyPkcs8);
                
                return (certPem, keyPem);
            }
            else
            {
                using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
                var dn = new X500DistinguishedName($"CN={commonName}");
                var req = new CertificateRequest(dn, ecdsa, HashAlgorithmName.SHA256);
                
                // Add extensions
                req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
                req.CertificateExtensions.Add(new X509KeyUsageExtension(
                    X509KeyUsageFlags.DigitalSignature, true));
                req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
                    new OidCollection { new Oid("1.3.6.1.5.5.7.3.2") }, // Client Authentication
                    true));
                
                // Create self-signed certificate
                var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(validityDays));
                
                // Export certificate to PEM
                byte[] certDer = cert.Export(X509ContentType.Cert);
                string certPem = WritePem("CERTIFICATE", certDer);
                
                // Export private key to PEM (PKCS#8)
                byte[] keyPkcs8 = ecdsa.ExportPkcs8PrivateKey();
                string keyPem = WritePem("PRIVATE KEY", keyPkcs8);
                
                return (certPem, keyPem);
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
            // Read the certificate chain and private key
            var chainText = File.ReadAllText(certPemPath);
            var keyText = File.ReadAllText(keyPemPath);
            
            // Extract only the FIRST certificate (device cert)
            var firstCertStart = chainText.IndexOf("-----BEGIN CERTIFICATE-----");
            var firstCertEnd = chainText.IndexOf("-----END CERTIFICATE-----");
            if (firstCertStart < 0 || firstCertEnd < 0)
                throw new InvalidOperationException("No valid certificate found in chain file");
            
            var deviceCertPem = chainText.Substring(firstCertStart, firstCertEnd - firstCertStart + "-----END CERTIFICATE-----".Length);
            
            // Combine the device cert and private key into a single PEM
            var combinedPem = deviceCertPem + "\n" + keyText;
            
            // Load the certificate with private key (ephemeral)
            var certEphemeral = X509Certificate2.CreateFromPem(combinedPem, combinedPem);
            
            // Export to PFX/PKCS12 format and re-import to make the private key persistent
            // This is required for TLS authentication with Azure IoT Hub
            var exportedPfx = certEphemeral.Export(X509ContentType.Pfx);
            var certPersistent = X509CertificateLoader.LoadPkcs12(
                exportedPfx,
                password: null,
                keyStorageFlags: X509KeyStorageFlags.Exportable);

            return certPersistent;
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
