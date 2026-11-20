using System.Collections;
using System.Reflection;
using BookTracker.Data.Models;

namespace BookTracker.Tests.Architecture;

/// <summary>
/// Keeps convention C7 enforced: an entity's collections are read-only over
/// private backing fields, so a caller outside the aggregate cannot add to,
/// remove from or replace them and every write goes through an entity method.
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class EntityCollectionTests
{
    private static readonly Type[] ReadOnlyCollectionTypes =
    [
        typeof(IReadOnlyCollection<>),
        typeof(IEnumerable<>),
    ];

    [Fact]
    public void EntityCollectionsAreReadOnlyWithNoPublicSetter()
    {
        var offenders = typeof(Book).Assembly.GetTypes()
            .Where(t => t.Namespace == typeof(Book).Namespace)
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(p => p.PropertyType != typeof(string) && typeof(IEnumerable).IsAssignableFrom(p.PropertyType))
            .Where(p => p.SetMethod is { IsPublic: true } || !IsReadOnlyCollectionType(p.PropertyType))
            .Select(p => $"{p.DeclaringType!.Name}.{p.Name} ({p.PropertyType.Name})")
            .ToList();

        Assert.True(offenders.Count == 0,
            "Entity collections must be IReadOnlyCollection<T> (or IEnumerable<T>) over a " +
            "private backing field, with no public setter; write through an entity method:" +
            Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    private static bool IsReadOnlyCollectionType(Type type) =>
        type.IsGenericType && ReadOnlyCollectionTypes.Contains(type.GetGenericTypeDefinition());
}
