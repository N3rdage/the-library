using System.Text;
using System.Text.RegularExpressions;

namespace BookTracker.Application.Duplicates;

internal static class DuplicateNormalization
{
    private static readonly Regex PunctuationAndSymbols = new(@"[\p{P}\p{S}]", RegexOptions.Compiled);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex LeadingArticle = new(@"^(the|a|an)\s+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Normalise an author name so "J.R.R. Tolkien", "JRR Tolkien", and
    // "J R R Tolkien" all collapse to "jrr tolkien". Single-letter runs
    // (typically initials) are joined into one token.
    public static string Author(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var lower = name.ToLowerInvariant();
        var depunct = PunctuationAndSymbols.Replace(lower, " ");
        var tokens = depunct.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var output = new List<string>();
        var run = new StringBuilder();
        foreach (var tok in tokens)
        {
            if (tok.Length == 1)
            {
                run.Append(tok);
            }
            else
            {
                if (run.Length > 0)
                {
                    output.Add(run.ToString());
                    run.Clear();
                }
                output.Add(tok);
            }
        }
        if (run.Length > 0) output.Add(run.ToString());

        return string.Join(' ', output);
    }

    // Secondary author key: "<surname>|<first-name-initial>". Returns empty
    // if the normalised name has fewer than two tokens (can't identify a
    // first-name initial). Used by the looser author-dup detection pass so
    // "Doug Preston" / "Douglas Preston" / "D Preston" surface as candidates.
    public static string AuthorSurnameInitialKey(string? name)
    {
        var normalized = Author(name);
        if (string.IsNullOrWhiteSpace(normalized)) return "";
        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 2) return "";
        var surname = tokens[^1];
        var firstInitial = tokens[0][0];
        return $"{surname}|{firstInitial}";
    }

    // Normalise a title for comparison. Lowercase, strip punctuation, collapse
    // whitespace, drop a leading article ("The ", "A ", "An ").
    public static string Title(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "";
        var lower = title.ToLowerInvariant();
        var depunct = PunctuationAndSymbols.Replace(lower, " ");
        var collapsed = Whitespace.Replace(depunct, " ").Trim();
        return LeadingArticle.Replace(collapsed, "");
    }
}
