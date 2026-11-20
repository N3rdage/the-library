using System.ComponentModel.DataAnnotations;

namespace BookTracker.Data.Models;

public enum SeriesType
{
    /// <summary>Numbered series with a known order (e.g. The Ender's Game Saga).</summary>
    Series,

    /// <summary>Loose collection without strict ordering (e.g. Discworld, Hercule Poirot).</summary>
    Collection
}

// Series' fields are written only through Create and UpdateDetails (private
// setters; EF materialises through them regardless of accessibility), so a
// handler cannot bypass the ExpectedCount/Type invariant and the compiler
// enforces it. The internal constructor serves the tests' seed builder
// (InternalsVisibleTo), and BookTracker.Application cannot reach it. Id stays
// public for EF and the tests' in-memory graphs.
public class Series
{
    // EF materialisation, and Create.
    private Series() { }

    /// <summary>Stores exactly the values given: nothing is trimmed or
    /// normalised here (unlike <see cref="UpdateDetails"/>, which also drops a
    /// Collection's count), so a test seed gets exactly what it passes.</summary>
    internal Series(string name, SeriesType type, string? author, int? expectedCount, string? description)
    {
        Name = name;
        Type = type;
        Author = author;
        ExpectedCount = expectedCount;
        Description = description;
    }

    public int Id { get; set; }

    [Required, MaxLength(300)]
    public string Name { get; private set; } = string.Empty;

    [MaxLength(200)]
    public string? Author { get; private set; }

    public SeriesType Type { get; private set; } = SeriesType.Series;

    /// <summary>Expected number of works in a Series. Null for Collections or unknown.</summary>
    public int? ExpectedCount { get; private set; }

    public string? Description { get; private set; }

    // Series membership lives on the Book — the Book is installment N of a
    // publication series (a series of short-story collections is unmanageable at
    // Work grain). Books is the inverse navigation; Work carries no series.
    // Membership is written through Book.AssignToSeries / ClearSeries.
    public IReadOnlyCollection<Book> Books => _books;
    private readonly List<Book> _books = [];

    // --- Aggregate behaviour -------------------------------------------------
    // Series is a thin aggregate: no shared m2m, no ref-count lifecycle (Book
    // owns the Book↔Series link via Book.AssignToSeries/ClearSeries). Its one
    // invariant is the ExpectedCount/Type pairing below — a target count only
    // means anything for an ordered Series, never a loose Collection. Cross-row
    // rules the entity can't see (name uniqueness) live in the handlers.
    // Deletion is FK-driven (Book.SeriesId is ON DELETE SET NULL), so there's no
    // delete method here. See docs/BACKEND-REFACTOR-DESIGN.md.

    /// <summary>Creates a Series with its details applied (and the
    /// ExpectedCount/Type invariant enforced). Name is required.</summary>
    public static Series Create(string name, string? author, SeriesType type, int? expectedCount, string? description)
    {
        var series = new Series();
        series.UpdateDetails(name, author, type, expectedCount, description);
        return series;
    }

    /// <summary>Applies the editable details. A target <paramref name="expectedCount"/>
    /// is only retained for an ordered <see cref="SeriesType.Series"/> — a
    /// Collection has no meaningful count, so it's nulled regardless of input.</summary>
    public void UpdateDetails(string name, string? author, SeriesType type, int? expectedCount, string? description)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainRuleException("Series name is required.");
        Name = name.Trim();
        Author = author.TrimToNull();
        Type = type;
        ExpectedCount = type == SeriesType.Series ? expectedCount : null;
        Description = description.TrimToNull();
    }
}
