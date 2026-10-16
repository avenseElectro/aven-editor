using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Sprocket.App.Ai;

/// <summary>
/// The assistant's API keys (Etapa D): never in <c>settings.json</c> — the whole store is one
/// DPAPI-encrypted blob under <c>%AppData%/Sprocket/ai-keys.bin</c>, so nothing readable ever reaches the
/// disk. Windows-only (<see cref="System.Security.Cryptography.ProtectedData"/>); elsewhere
/// <see cref="IsSupported"/> is false and the preferences UI reports that keys cannot be persisted. All
/// reads are best-effort: a missing or unreadable store just means "no key".
/// </summary>
internal static class SecretStore
{
    public const string Gemini = "gemini";
    public const string Glm = "glm";

    // Ties the ciphertext to this store's purpose; changing it invalidates every stored key (acceptable —
    // the user re-pastes a key once).
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Sprocket.AvenEditor.ai-keys.v1");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    /// <summary>Whether keys can be persisted on this OS (DPAPI exists).</summary>
    public static bool IsSupported => OperatingSystem.IsWindows();

    /// <summary>The store file. Honours a <c>SPROCKET_AI_KEYS_FILE</c> override (tests / portable installs),
    /// mirroring <c>SPROCKET_LAYOUTS_DIR</c>.</summary>
    public static string DefaultFilePath()
    {
        string? overridePath = Environment.GetEnvironmentVariable("SPROCKET_AI_KEYS_FILE");
        if (!string.IsNullOrEmpty(overridePath))
            return overridePath;
        string baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(baseDir))
            baseDir = Path.Combine(AppContext.BaseDirectory, "cache");
        return Path.Combine(baseDir, "Sprocket", "ai-keys.bin");
    }

    /// <summary>The stored key for <paramref name="provider"/>, or <see langword="null"/> when absent.</summary>
    public static string? Get(string provider, string? filePath = null)
    {
        if (!IsSupported)
            return null;
        try
        {
            return LoadMap(filePath).GetValueOrDefault(provider.Trim().ToLowerInvariant());
        }
        catch
        {
            return null; // unreadable — all mean "no key"
        }
    }

    /// <summary>Whether a key is stored for <paramref name="provider"/>.</summary>
    public static bool HasKey(string provider, string? filePath = null) => Get(provider, filePath) is not null;

    /// <summary>Stores (or replaces) the key for <paramref name="provider"/>, keeping the other providers'
    /// keys. Windows-only; throws <see cref="PlatformNotSupportedException"/> elsewhere.</summary>
    public static void Set(string provider, string key, string? filePath = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(provider);
        ArgumentException.ThrowIfNullOrEmpty(key);
        Dictionary<string, string> map = LoadMap(filePath);
        map[provider.Trim().ToLowerInvariant()] = key;
        WriteMap(map, filePath);
    }

    /// <summary>Removes the key for <paramref name="provider"/>; deleting the last key removes the file.</summary>
    public static void Remove(string provider, string? filePath = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(provider);
        Dictionary<string, string> map = LoadMap(filePath);
        if (!map.Remove(provider.Trim().ToLowerInvariant()))
            return;
        try
        {
            if (map.Count == 0)
                File.Delete(filePath ?? DefaultFilePath());
            else
                WriteMap(map, filePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort like every other store: a stale key is a nuisance, not a failure.
        }
    }

    private static Dictionary<string, string> LoadMap(string? filePath)
    {
        if (!IsSupported)
            throw new PlatformNotSupportedException("DPAPI is only available on Windows.");
        try
        {
            string path = filePath ?? DefaultFilePath();
            if (!File.Exists(path))
                return [];
            byte[] plain = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(plain) ?? [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
        catch (CryptographicException)
        {
            // A blob that no longer decrypts (corrupt, or another user's) is dead weight: start over
            // rather than fail — the keys are a paste-away.
            return [];
        }
    }

    private static void WriteMap(Dictionary<string, string> map, string? filePath)
    {
        string path = filePath ?? DefaultFilePath();
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        byte[] cipher = ProtectedData.Protect(
            JsonSerializer.SerializeToUtf8Bytes(map, JsonOptions), Entropy, DataProtectionScope.CurrentUser);
        string temp = path + ".tmp";
        File.WriteAllBytes(temp, cipher);
        File.Move(temp, path, overwrite: true);
    }
}
