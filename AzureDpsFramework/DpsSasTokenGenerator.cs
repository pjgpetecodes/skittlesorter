using System;
using System.Security.Cryptography;
using System.Text;
using System.Web;

namespace AzureDpsFramework
{
    public static class DpsSasTokenGenerator
    {
        public static string DeriveDeviceKey(string registrationId, string enrollmentGroupKeyBase64)
        {
            var regIdLower = registrationId.ToLowerInvariant();
            Console.WriteLine($"[KEY DERIVATION] RegistrationId (normalized): {regIdLower}");
            Console.WriteLine($"[KEY DERIVATION] EnrollmentGroupKey (first 20): {enrollmentGroupKeyBase64?.Substring(0, Math.Min(20, enrollmentGroupKeyBase64.Length))}...");
            
            byte[] groupKey = Convert.FromBase64String(enrollmentGroupKeyBase64);
            Console.WriteLine($"[KEY DERIVATION] Decoded group key length: {groupKey.Length} bytes");
            
            using var hmac = new HMACSHA256(groupKey);
            byte[] regBytes = Encoding.ASCII.GetBytes(regIdLower);
            byte[] mac = hmac.ComputeHash(regBytes);
            var derivedKey = Convert.ToBase64String(mac);
            Console.WriteLine($"[KEY DERIVATION] Derived device key (first 20): {derivedKey.Substring(0, Math.Min(20, derivedKey.Length))}...");
            Console.WriteLine($"[KEY DERIVATION] Derived device key length: {derivedKey.Length} chars\n");
            return derivedKey;
        }

        public static string GenerateDpsSas(string idScope, string registrationId, string keyBase64, int expirySeconds)
        {
            Console.WriteLine($"\n[SAS] Generating SAS token for DPS registration");
            Console.WriteLine($"[SAS] IdScope: {idScope}");
            Console.WriteLine($"[SAS] RegistrationId: {registrationId}");
            Console.WriteLine($"[SAS] Key (first 20 chars): {keyBase64?.Substring(0, Math.Min(20, keyBase64.Length))}...");
            Console.WriteLine($"[SAS] ExpirySeconds: {expirySeconds}");
            
            // Step 1: Base64-decode the key
            byte[] keyBytes = Convert.FromBase64String(keyBase64);
            Console.WriteLine($"[SAS] Decoded key length: {keyBytes.Length} bytes");
            
            // Step 2: Build resource URI (lowercase for Azure compatibility)
            string resourceUri = $"{idScope}/registrations/{registrationId}".ToLowerInvariant();
            Console.WriteLine($"[SAS] Resource URI: {resourceUri}");
            
            // Step 3: URL-encode the resource URI FOR SIGNING ONLY
            string urlEncodedUri = Uri.EscapeDataString(resourceUri);
            Console.WriteLine($"[SAS] URL-encoded URI (for signing): {urlEncodedUri}");
            
            // Step 4: Build expiry timestamp
            long expiry = DateTimeOffset.UtcNow.AddSeconds(expirySeconds).ToUnixTimeSeconds();
            Console.WriteLine($"[SAS] Expiry Timestamp: {expiry}");
            
            // Step 5: Build message with LITERAL newline (URL-encoded URI + \n + expiry)
            string stringToSign = $"{urlEncodedUri}\n{expiry}";
            Console.WriteLine($"[SAS] Message to sign: {stringToSign.Replace("\n", "\\n")}");
            
            // Step 6: HMAC-SHA256 sign with enrollment group key
            using var hmac = new HMACSHA256(keyBytes);
            byte[] sigBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign));
            string signatureBase64 = Convert.ToBase64String(sigBytes);
            Console.WriteLine($"[SAS] Signature (first 30 chars): {signatureBase64.Substring(0, Math.Min(30, signatureBase64.Length))}...");
            
            // Step 7: URL-encode the signature
            string urlEncodedSignature = Uri.EscapeDataString(signatureBase64);
            
            // Step 8: Build final SAS token
            // CRITICAL: sr parameter must contain URL-ENCODED resource URI (not raw)
            var sasToken = $"SharedAccessSignature sr={urlEncodedUri}&sig={urlEncodedSignature}&se={expiry}&skn=registration";
            Console.WriteLine($"[SAS] Generated SAS Token (first 60 chars): {sasToken.Substring(0, Math.Min(60, sasToken.Length))}...");
            Console.WriteLine($"[SAS] Full SAS Token length: {sasToken.Length} chars");
            Console.WriteLine($"[SAS] FULL TOKEN: {sasToken}\n");
            return sasToken;
        }
    }
}
