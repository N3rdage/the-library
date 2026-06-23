using BookTracker.Data.Models;

namespace BookTracker.Application.Works;

/// <summary>A non-Author contributor (editor, translator, …) on a Work, by name +
/// role. The handler resolves the name to an Author via AuthorResolver.</summary>
public sealed record ContributorInput(string Name, AuthorRole Role);

/// <summary>One row of the "attach multiple works" compendium flow: either an
/// existing Work to attach (<see cref="AttachedWorkId"/> set) or a new Work to
/// create from its fields. Dates/orders arrive already parsed (the VM owns the
/// free-text parsing). Application-side contract so no Web VM type crosses the
/// boundary (convention C3).</summary>
public sealed record WorkRow(
    int? AttachedWorkId,
    string? Title,
    string? Subtitle,
    DateOnly? FirstPublished,
    DatePrecision Precision,
    IReadOnlyList<string> Authors,
    IReadOnlyList<ContributorInput> Contributors,
    IReadOnlyList<int> GenreIds);
