using System.Security.Cryptography;
using System.Text;

namespace DropWheel.Ipc;

/// <summary>Имена pipe/mutex, свои у каждого пользователя Windows.</summary>
public static class PipeNames
{
    /// <summary>
    /// «DropWheel-1a2b3c4d5e6f7a8b»: к базовому имени добавляется короткий хэш домена и имени
    /// пользователя. В имени только латиница, цифры и дефис — годится и для pipe, и для mutex.
    /// </summary>
    public static string ForCurrentUser(string baseName)
    {
        var user = $"{Environment.UserDomainName}\\{Environment.UserName}".ToLowerInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(user));
        return $"{Sanitize(baseName)}-{Convert.ToHexString(hash, 0, 8).ToLowerInvariant()}";
    }

    private static string Sanitize(string name)
    {
        var chars = name.Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_').ToArray();
        return chars.Length == 0 ? "DropWheel" : new string(chars);
    }
}
