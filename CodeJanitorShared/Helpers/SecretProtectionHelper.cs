using System;
using System.Security.Cryptography;
using System.Text;

namespace CodeJanitor.Helpers;

/// <summary>
/// Provides per-user DPAPI protection for persisted secrets.
/// </summary>

internal static class SecretProtectionHelper
{
    private const string ProtectedPrefix = "enc:";

    /// <summary>
    /// Encodes the input as UTF-8, encrypts it with DPAPI using the current user&apos;s scope and no entropy, then returns a base64 string prefixed with ProtectedPrefix, or an empty string if the input is null or whitespace.
    /// </summary>
    /// <param name="plainText">The plain text.</param>
    /// <returns>A string value produced by this method.</returns>

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

    /// <summary>
    /// Attempts to decrypt a prefixed, Base64-encoded protected string for the current user, returning it as UTF-8 text, with empty string for null/whitespace input, an unchanged value for legacy plain-text input, and a silent empty string on any decryption failure (no side effects).
    /// </summary>
    /// <param name="protectedValue">The protected value.</param>
    /// <returns>A string value produced by this method.</returns>

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
