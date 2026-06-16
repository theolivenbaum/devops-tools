using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Azdo.Core.Util;

/// <summary>Opens URLs in the user's default browser across platforms (≈ <c>browser.Open</c>).</summary>
public static class Browser
{
    public static void Open(string rawUrl)
    {
        if (string.IsNullOrEmpty(rawUrl))
            throw new ArgumentException("browser: empty URL");

        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri))
            throw new ArgumentException($"browser: invalid URL: {rawUrl}");
        if (uri.Scheme != "http" && uri.Scheme != "https")
            throw new ArgumentException($"browser: only http/https URLs are supported, got \"{uri.Scheme}\"");
        if (string.IsNullOrEmpty(uri.Host))
            throw new ArgumentException($"browser: URL missing host: \"{rawUrl}\"");

        var (name, args) = PlatformCommand(rawUrl);
        Process.Start(new ProcessStartInfo
        {
            FileName = name,
            Arguments = string.Join(' ', args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a)),
            UseShellExecute = false,
        });
    }

    private static (string, string[]) PlatformCommand(string url)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return ("open", new[] { url });
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return ("rundll32", new[] { "url.dll,FileProtocolHandler", url });
        return ("xdg-open", new[] { url });
    }
}
