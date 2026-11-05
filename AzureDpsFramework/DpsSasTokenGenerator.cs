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
            if (string.IsNullOrWhiteSpace(registrationId))
                throw new ArgumentException("RegistrationId is required", nameof(registrationId));
            if (string.IsNullOrWhiteSpace(enrollmentGroupKeyBase64))
                throw new ArgumentNullException(nameof(enrollmentGroupKeyBase64), "Enrollment group key is required");

            var regIdLower = registrationId.ToLowerInvariant();
            
            byte[] groupKey = Convert.FromBase64String(enrollmentGroupKeyBase64!);
            
            using var hmac = new HMACSHA256(groupKey);
            byte[] regBytes = Encoding.ASCII.GetBytes(regIdLower);
            byte[] mac = hmac.ComputeHash(regBytes);
            var derivedKey = Convert.ToBase64String(mac);
            return derivedKey;
        }

        public static string GenerateDpsSas(string idScope, string registrationId, string keyBase64, int expirySeconds)
        {
            if (string.IsNullOrWhiteSpace(idScope))
                throw new ArgumentException("IdScope is required", nameof(idScope));
            if (string.IsNullOrWhiteSpace(registrationId))
                throw new ArgumentException("RegistrationId is required", nameof(registrationId));
            if (string.IsNullOrWhiteSpace(keyBase64))
                throw new ArgumentNullException(nameof(keyBase64), "Base64 key is required");

            // Step 1: Base64-decode the key
            byte[] keyBytes = Convert.FromBase64String(keyBase64!);
            
            // Step 2: Build resource URI (lowercase for Azure compatibility)
            string resourceUri = $"{idScope}/registrations/{registrationId}".ToLowerInvariant();
            
            // Step 3: URL-encode the resource URI FOR SIGNING ONLY
            string urlEncodedUri = Uri.EscapeDataString(resourceUri);
            
            // Step 4: Build expiry timestamp
            long expiry = DateTimeOffset.UtcNow.AddSeconds(expirySeconds).ToUnixTimeSeconds();
            
            // Step 5: Build message with LITERAL newline (URL-encoded URI + \n + expiry)
            string stringToSign = $"{urlEncodedUri}\n{expiry}";
            
            // Step 6: HMAC-SHA256 sign with enrollment group key
            using var hmac = new HMACSHA256(keyBytes);
            byte[] sigBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign));
            string signatureBase64 = Convert.ToBase64String(sigBytes);
            
            // Step 7: URL-encode the signature
            string urlEncodedSignature = Uri.EscapeDataString(signatureBase64);
            
            // Step 8: Build final SAS token
            // CRITICAL: sr parameter must contain URL-ENCODED resource URI (not raw)
            var sasToken = $"SharedAccessSignature sr={urlEncodedUri}&sig={urlEncodedSignature}&se={expiry}&skn=registration";
            return sasToken;
        }
    }
}
