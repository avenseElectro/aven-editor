using System.IO;
using System.Text;
using Sprocket.App.Ai;
using Xunit;

namespace Sprocket.App.Tests;

/// <summary>
/// The assistant's key store (Etapa D): DPAPI round-trips per provider, the on-disk blob never carries the
/// key in the clear, and a missing/corrupt store reads as "no key" instead of throwing. The crypto itself
/// is Windows-only, so the tests skip elsewhere.
/// </summary>
public sealed class SecretStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"ai-keys-{Guid.NewGuid():N}.bin");

    public void Dispose()
    {
        try { File.Delete(_path); } catch (IOException) { }
    }

    [Fact]
    public void Set_Get_Remove_Round_Trips_Per_Provider()
    {
        if (!OperatingSystem.IsWindows())
            return;

        Assert.False(SecretStore.HasKey(SecretStore.Gemini, _path));
        SecretStore.Set(SecretStore.Gemini, "key-gemini-1", _path);
        SecretStore.Set(SecretStore.Glm, "key-glm-1", _path);

        Assert.Equal("key-gemini-1", SecretStore.Get(SecretStore.Gemini, _path));
        Assert.Equal("key-glm-1", SecretStore.Get(SecretStore.Glm, _path));

        SecretStore.Set(SecretStore.Gemini, "key-gemini-2", _path); // replace keeps the sibling
        Assert.Equal("key-gemini-2", SecretStore.Get(SecretStore.Gemini, _path));
        Assert.Equal("key-glm-1", SecretStore.Get(SecretStore.Glm, _path));

        SecretStore.Remove(SecretStore.Gemini, _path);
        Assert.False(SecretStore.HasKey(SecretStore.Gemini, _path));
        Assert.Equal("key-glm-1", SecretStore.Get(SecretStore.Glm, _path));

        SecretStore.Remove(SecretStore.Glm, _path); // last key → the file itself goes away
        Assert.False(File.Exists(_path));
        SecretStore.Remove(SecretStore.Glm, _path); // removing from nothing is a no-op
    }

    [Fact]
    public void The_File_On_Disk_Never_Contains_The_Key_In_The_Clear()
    {
        if (!OperatingSystem.IsWindows())
            return;

        SecretStore.Set(SecretStore.Gemini, "sk-very-secret-0123456789abcdef", _path);

        string disk = Encoding.UTF8.GetString(File.ReadAllBytes(_path));
        Assert.DoesNotContain("sk-very-secret", disk);
        Assert.DoesNotContain("gemini", disk); // not even the provider names are readable
    }

    [Fact]
    public void Missing_Or_Corrupt_File_Reads_As_No_Key()
    {
        if (!OperatingSystem.IsWindows())
            return;

        Assert.Null(SecretStore.Get(SecretStore.Gemini, _path));

        File.WriteAllBytes(_path, [1, 2, 3, 4, 5, 6, 7, 8]);
        Assert.Null(SecretStore.Get(SecretStore.Gemini, _path));
        Assert.False(SecretStore.HasKey(SecretStore.Glm, _path));

        // A corrupt store is replaced, not fatal: Set still lands and reads back.
        SecretStore.Set(SecretStore.Glm, "key-after-corruption", _path);
        Assert.Equal("key-after-corruption", SecretStore.Get(SecretStore.Glm, _path));
    }
}
