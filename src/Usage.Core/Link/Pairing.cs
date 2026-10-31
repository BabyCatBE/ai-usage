using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Usage.Core.Link;

/// <summary>
/// Pairing secret and end-to-end encryption. The relay only ever sees the room id and opaque ciphertext.
/// The secret is 128 random bits shown as a QR / link / grouped code; everything else is derived with HKDF.
/// </summary>
public static class Pairing
{
    const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public sealed record Keys(string RoomId, byte[] Key);

    public static byte[] NewSecret() => RandomNumberGenerator.GetBytes(16);

    public static Keys Derive(byte[] secret) => new(
        Convert.ToHexString(HKDF.DeriveKey(HashAlgorithmName.SHA256, secret, 16, info: "usage/v1/room"u8.ToArray())).ToLowerInvariant(),
        HKDF.DeriveKey(HashAlgorithmName.SHA256, secret, 32, info: "usage/v1/key"u8.ToArray()));

    public static string ToCode(byte[] secret)
    {
        var sb = new StringBuilder();
        int buffer = 0, bits = 0;
        foreach (var b in secret)
        {
            buffer = (buffer << 8) | b; bits += 8;
            while (bits >= 5) { sb.Append(Alphabet[(buffer >> (bits - 5)) & 31]); bits -= 5; }
        }
        if (bits > 0) sb.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        return sb.ToString();
    }

    /// <summary>Accepts the raw code in any case, with spaces or dashes.</summary>
    public static byte[]? FromCode(string code)
    {
        var clean = new string(code.ToUpperInvariant().Where(c => Alphabet.Contains(c)).ToArray());
        if (clean.Length != 26) return null;
        var bytes = new List<byte>(); int buffer = 0, bits = 0;
        foreach (var c in clean)
        {
            buffer = (buffer << 5) | Alphabet.IndexOf(c); bits += 5;
            if (bits >= 8) { bytes.Add((byte)((buffer >> (bits - 8)) & 0xFF)); bits -= 8; }
        }
        return bytes.Count == 16 ? [.. bytes] : null;
    }

    /// <summary>Human-friendly grouping: ABCDE-FGHIJ-KLMNO-PQRST-UVWXYZ.</summary>
    public static string Group(string code) =>
        string.Join('-', Enumerable.Range(0, 5).Select(i => code.Substring(i * 5, 5)).Append(code[25..]));

    public static string BuildLink(byte[] secret, string relayUrl, string deviceName) =>
        "usage://pair?k=" + ToCode(secret) + "&r=" + Uri.EscapeDataString(relayUrl) + "&n=" + Uri.EscapeDataString(deviceName);

    // --- Encryption: AES-256-GCM, random 96-bit nonce, direction bound through the AAD. -----------

    public static string Seal(Keys keys, char role, JsonObject message)
    {
        var plain = Encoding.UTF8.GetBytes(message.ToJsonString());
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(keys.Key, 16);
        aes.Encrypt(nonce, plain, cipher, tag, Aad(keys, role));
        return new JsonObject
        {
            ["v"] = 1,
            ["n"] = Convert.ToBase64String(nonce),
            ["c"] = Convert.ToBase64String([.. cipher, .. tag]),
        }.ToJsonString();
    }

    /// <summary>Returns null for anything that is not a valid, authentic, fresh message from <paramref name="fromRole"/>.</summary>
    public static JsonObject? Open(Keys keys, char fromRole, string frame, DateTimeOffset now)
    {
        try
        {
            if (JsonNode.Parse(frame) is not JsonObject env || (int?)env["v"] != 1) return null;
            var nonce = Convert.FromBase64String(env["n"]!.GetValue<string>());
            var all = Convert.FromBase64String(env["c"]!.GetValue<string>());
            if (nonce.Length != 12 || all.Length < 16) return null;
            var plain = new byte[all.Length - 16];
            using var aes = new AesGcm(keys.Key, 16);
            aes.Decrypt(nonce, all.AsSpan(0, plain.Length), all.AsSpan(plain.Length), plain, Aad(keys, fromRole));
            if (JsonNode.Parse(plain) is not JsonObject msg) return null;
            var ts = (long?)msg["ts"];
            if (ts is null || Math.Abs((now - DateTimeOffset.FromUnixTimeMilliseconds(ts.Value)).TotalMinutes) > 10) return null; // replay window
            return msg;
        }
        catch { return null; }
    }

    static byte[] Aad(Keys keys, char role) => Encoding.ASCII.GetBytes(keys.RoomId + ":" + role);
}
