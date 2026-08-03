using System;
using System.Security.Cryptography;
using System.Text;

namespace CodeJanitor.Helpers
{
    /// <summary>
    /// Provides per-user DPAPI protection for persisted secrets.
    /// </summary>
    internal static class SecretProtectionHelper
    {
        private const string ProtectedPrefix = "enc:";

        internal static string ProtectForCurrentUser(string plainText)
        {
            if (string.IsNullOrWhiteSpace(plainText))
            {
                return string.Empty;
            }

            var data = Encoding.UTF8.GetBytes(plainText);
            var protectedData = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
            return ProtectedPrefix + Convert.ToBase64String(protectedData);
        }

        internal static string UnprotectForCurrentUser(string protectedValue)
        {
            if (string.IsNullOrWhiteSpace(protectedValue))
            {
                return string.Empty;
            }

            if (!protectedValue.StartsWith(ProtectedPrefix, StringComparison.Ordinal))
            {
                // Backward compatibility for previously saved plain-text values.
                return protectedValue;
            }

            try
            {
                var payload = protectedValue.Substring(ProtectedPrefix.Length);
                var protectedBytes = Convert.FromBase64String(payload);
                var plainBytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plainBytes);
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }
    }
}