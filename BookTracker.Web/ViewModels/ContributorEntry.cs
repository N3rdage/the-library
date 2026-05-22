using BookTracker.Data.Models;

namespace BookTracker.Web.ViewModels;

// One row in the "Other contributors" picker — a non-Author contributor
// to a Work (editor, translator, illustrator, etc.). The Author role
// itself is captured by the standard MudAuthorPicker; this entry exists
// to model the rarer role-tagged contributors that ride alongside.
//
// Mutable so MudAutocomplete + MudSelect can two-way-bind through to the
// underlying list — record-with-init would force the picker to rebuild
// the list on every per-field edit.
public class ContributorEntry
{
    public string Name { get; set; } = string.Empty;
    public AuthorRole Role { get; set; } = AuthorRole.Editor;

    public ContributorEntry() { }
    public ContributorEntry(string name, AuthorRole role)
    {
        Name = name;
        Role = role;
    }
}
