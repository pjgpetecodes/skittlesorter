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
            byte[] groupKey = Convert.FromBase64String(enrollmentGroupKeyBase64);
            using var hmac = new HMACSHA256(groupKey);
            byte[] regBytes = Encoding.ASCII.GetBytes(regIdLower);
            byte[] mac = hmac.ComputeHash(regBytes);
            return Convert.ToBase64String(mac);
        }

        public static string GenerateDpsSas(string idScope, string registrationId, string deviceKeyBase64, int expirySeconds)
        {
            long expiry = DateTimeOffset.UtcNow.AddSeconds(expirySeconds).ToUnixTimeSeconds();
            string resourceUri = $"{idScope}/registrations/{registrationId}";
            string stringToSign = $"{resourceUri}\n{expiry}";
            byte[] keyBytes = Convert.FromBase64String(deviceKeyBase64);
            using var hmac = new HMACSHA256(keyBytes);
            byte[] sigBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign));
            string signature = Convert.ToBase64String(sigBytes);
            string sr = Uri.EscapeDataString(resourceUri);
            string sigEscaped = Uri.EscapeDataString(signature);
            return $"SharedAccessSignature sr={sr}&sig={sigEscaped}&se={expiry}&skn=registration";
        }
    }
}
