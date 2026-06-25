using System.Security.Cryptography;
using System.Text;

namespace EnerkomChatbot.Indexer;

/// <summary>SHA-256 hash textu (hex) pro detekci změny zdroje a obsahu chunku.</summary>
internal static class Hashing
{
    public static string Sha256(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexStringLower(bytes);
    }
}
