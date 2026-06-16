using System.Runtime.InteropServices;

namespace Azdo.Core.Configuration;

/// <summary>Thrown when no PAT is stored (≈ <c>config.ErrNotFound</c>).</summary>
public sealed class PatNotFoundException() : Exception("PAT not found");

/// <summary>Stores/retrieves the Azure DevOps Personal Access Token.</summary>
public interface IPatStore
{
    /// <summary>Returns the stored PAT or throws <see cref="PatNotFoundException"/>.</summary>
    string GetPat();
    void SetPat(string token);
    void DeletePat();
}

/// <summary>
/// File-backed PAT store with an <c>AZDO_PAT</c> environment-variable fallback.
/// The token is written to <c>~/.config/azdo-tui/.pat</c> with <c>0600</c>
/// permissions on Unix. Native OS credential stores are future work (see TODO).
/// </summary>
public sealed class PatStore : IPatStore
{
    private readonly string _path;

    public PatStore(string? path = null)
    {
        if (path is not null) { _path = path; return; }
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _path = Path.Combine(home, ".config", "azdo-tui", ".pat");
    }

    public string GetPat()
    {
        if (File.Exists(_path))
        {
            var token = File.ReadAllText(_path).Trim();
            if (token.Length > 0) return token;
        }
        var env = Environment.GetEnvironmentVariable("AZDO_PAT");
        if (!string.IsNullOrEmpty(env)) return env;
        throw new PatNotFoundException();
    }

    public void SetPat(string token)
    {
        if (string.IsNullOrEmpty(token)) throw new ArgumentException("token cannot be empty");
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, token);
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try { File.SetUnixFileMode(_path, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
            catch { /* best effort */ }
        }
    }

    public void DeletePat()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }
}
