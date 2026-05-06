using System.Reflection;

namespace BookTracker.Web.Services;

// Surfaces the deployed git commit SHA in the UI footer so it's clear which
// build is live in prod / staging without checking the Azure portal — useful
// in particular for confirming slot swaps actually moved the bits.
//
// SHA injection happens at build time via the SDK's standard plumbing:
// `dotnet build /p:SourceRevisionId=<sha>` causes the SDK to append `+<sha>`
// to AssemblyInformationalVersionAttribute. Local `dotnet run` without the
// property leaves the suffix empty — ShortSha returns null and the footer
// renders nothing rather than a placeholder.
public static class BuildInfo
{
    private const int ShortShaLength = 7;
    private const string RepoCommitUrlBase = "https://github.com/N3rdage/the-library/commit/";

    public static string? ShortSha { get; }
    public static string? CommitUrl { get; }

    static BuildInfo()
    {
        var sha = ParseSha(Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
        if (string.IsNullOrEmpty(sha)) return;
        ShortSha = sha.Length >= ShortShaLength ? sha[..ShortShaLength] : sha;
        CommitUrl = RepoCommitUrlBase + sha;
    }

    /// <summary>
    /// Parses the SHA suffix from an InformationalVersion string of the shape
    /// "1.0.0+abcdef0...". Returns null when the input is empty or lacks a
    /// "+sha" suffix (the local-dev case). Internal so the static initializer
    /// can stay declarative while tests exercise the parsing rules directly.
    /// </summary>
    internal static string? ParseSha(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion)) return null;
        var plus = informationalVersion.IndexOf('+');
        if (plus < 0 || plus == informationalVersion.Length - 1) return null;
        return informationalVersion[(plus + 1)..];
    }
}
