using System.Reflection;

namespace BookTracker.Mobile.Services;

// Mirrors BookTracker.Web/Services/BuildInfo.cs — surfaces the app's
// display version + the deployed git commit SHA so the home page
// footer makes it obvious which build is running on the device. Cuts
// the "is this even the new build?" round-trip during in-bookshop
// testing.
//
// SHA injection happens at build time via AssemblyInformationalVersion:
// the BookTracker.Mobile.csproj <Target Name="StampGitSha"> runs
// `git rev-parse HEAD` before versioning and stamps SourceRevisionId,
// which the SDK appends as "+<sha>" on InformationalVersion. Builds
// outside a git checkout (CI download artefact, manual publish without
// git) get the version-only string; ShortSha returns null and the
// footer renders just "v0.1".
public static class BuildInfo
{
    private const int ShortShaLength = 7;
    private const string RepoCommitUrlBase = "https://github.com/N3rdage/the-library/commit/";

    public static string Version { get; }
    public static string? ShortSha { get; }
    public static string? CommitUrl { get; }
    public static string DisplayString { get; }

    static BuildInfo()
    {
        var info = typeof(BuildInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        Version = ParseVersion(info) ?? "0.0";

        var sha = ParseSha(info);
        if (!string.IsNullOrEmpty(sha))
        {
            ShortSha = sha.Length >= ShortShaLength ? sha[..ShortShaLength] : sha;
            CommitUrl = RepoCommitUrlBase + sha;
        }

        DisplayString = ShortSha is null ? $"v{Version}" : $"v{Version} · {ShortSha}";
    }

    /// <summary>
    /// Returns the version part of an InformationalVersion string of
    /// the shape "0.1+abcdef0...". Returns the whole string when no
    /// "+sha" suffix is present, or null when the input is empty.
    /// Internal so the static initializer stays declarative while tests
    /// can exercise the parsing rules directly.
    /// </summary>
    internal static string? ParseVersion(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion)) return null;
        var plus = informationalVersion.IndexOf('+');
        return plus < 0 ? informationalVersion : informationalVersion[..plus];
    }

    /// <summary>
    /// Returns the SHA suffix from an InformationalVersion string of
    /// the shape "0.1+abcdef0...". Null when there's no "+sha" suffix
    /// (a build outside a git checkout, or one not passing
    /// SourceRevisionId).
    /// </summary>
    internal static string? ParseSha(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion)) return null;
        var plus = informationalVersion.IndexOf('+');
        if (plus < 0 || plus == informationalVersion.Length - 1) return null;
        return informationalVersion[(plus + 1)..];
    }
}
