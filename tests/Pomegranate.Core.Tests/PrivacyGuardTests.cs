namespace Pomegranate.Core.Tests;

/// <summary>
/// Spec §2 and §12.5: Pomegranate never connects to the internet. This fails if networking APIs
/// show up anywhere in the app's source.
/// </summary>
public class PrivacyGuardTests
{
    private static readonly string[] Forbidden =
    [
        "System.Net.Http",
        "HttpClient",
        "HttpWebRequest",
        "WebRequest.Create",
        "WebClient",
        "System.Net.Sockets",
        "TcpClient",
        "UdpClient",
        "System.Net.WebSockets",
        "Windows.Web.Http",
    ];

    [Fact]
    public void App_source_contains_no_networking_code()
    {
        var src = Path.Combine(RepoRoot(), "src");
        var offenders = Directory.EnumerateFiles(src, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".cpp", StringComparison.OrdinalIgnoreCase))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                        !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .SelectMany(f => Forbidden
                .Where(token => File.ReadAllText(f).Contains(token, StringComparison.Ordinal))
                .Select(token => $"{Path.GetRelativePath(src, f)}: {token}"))
            .ToArray();

        Assert.Empty(offenders);
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Pomegranate.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Couldn't find the repository root.");
    }
}
