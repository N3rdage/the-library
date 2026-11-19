using System.Reflection;
using BookTracker.Data;
using BookTracker.Web;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Tests.Architecture;

/// <summary>
/// Keeps the Application seam honest in <c>BookTracker.Web</c>. Web references
/// Data (the composition root and the enums need it), so the compiler cannot
/// stop a ViewModel or component from reaching for the DbContext or an entity;
/// these reflection checks do instead.
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class WebLayeringTests
{
    private static readonly Assembly WebAssembly = typeof(ProgramSetup).Assembly;

    // The five C1 exceptions (decided on backlog #262): the composition root,
    // the warmup probe, and the cover-mirror / edition-format background work
    // that runs outside any circuit and has no dispatcher scope to go through.
    private static readonly HashSet<string> DbContextAllowList =
    [
        "BookTracker.Web.ProgramSetup",
        "BookTracker.Web.Api.WarmupEndpoints",
        "BookTracker.Web.Services.Covers.CoverMirrorBackgroundService",
        "BookTracker.Web.Services.Covers.CoverMirrorQueue",
        "BookTracker.Web.Services.EditionFormatBackfillService",
    ];

    private static readonly string[] PresentationNamespaces =
    [
        "BookTracker.Web.ViewModels",
        "BookTracker.Web.Components",
    ];

    private const BindingFlags AllDeclared =
        BindingFlags.Public | BindingFlags.NonPublic |
        BindingFlags.Instance | BindingFlags.Static |
        BindingFlags.DeclaredOnly;

    [Fact]
    public void OnlyTheAllowListedTypesTouchTheDbContext()
    {
        var offenders = WebAssembly.GetTypes()
            .Where(t => !DbContextAllowList.Contains(OutermostType(t).FullName!))
            .SelectMany(t => Offenders(t, IsDbContextType))
            .ToList();

        Assert.True(offenders.Count == 0,
            "Types outside the DbContext allow-list reference BookTrackerDbContext, " +
            "IDbContextFactory<BookTrackerDbContext> or DbSet<>; dispatch a command or " +
            "query through IDispatcher instead:" + Environment.NewLine +
            string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void ViewModelsAndComponentsDoNotTouchDataEntities()
    {
        var offenders = WebAssembly.GetTypes()
            .Where(t => PresentationNamespaces.Any(ns =>
                t.Namespace == ns || (t.Namespace?.StartsWith(ns + ".", StringComparison.Ordinal) ?? false)))
            .SelectMany(t => Offenders(t, IsDataEntity))
            .ToList();

        Assert.True(offenders.Count == 0,
            "ViewModels or components reference Data entities; project to an Application " +
            "read model instead (Data enums are allowed):" + Environment.NewLine +
            string.Join(Environment.NewLine, offenders));
    }

    // Nested and compiler-generated types (async state machines, lambda
    // closures) belong to the type that declares them.
    private static Type OutermostType(Type type)
    {
        while (type.DeclaringType is not null)
        {
            type = type.DeclaringType;
        }
        return type;
    }

    private static IEnumerable<string> Offenders(Type type, Func<Type, bool> isForbidden)
    {
        var members = new List<(string Member, Type Type)>();

        foreach (var field in type.GetFields(AllDeclared))
        {
            members.Add((field.Name, field.FieldType));
        }
        // Covers [Inject] properties, which is what Razor @inject compiles into.
        foreach (var property in type.GetProperties(AllDeclared))
        {
            members.Add((property.Name, property.PropertyType));
        }
        foreach (var method in type.GetConstructors(AllDeclared).Cast<MethodBase>().Concat(type.GetMethods(AllDeclared)))
        {
            foreach (var parameter in method.GetParameters())
            {
                members.Add(($"{method.Name}({parameter.Name})", parameter.ParameterType));
            }
        }

        foreach (var (member, memberType) in members)
        {
            var hit = Flatten(memberType).FirstOrDefault(isForbidden);
            if (hit is not null)
            {
                yield return $"  {type.FullName}.{member}: {Display(hit)}";
            }
        }
    }

    // Type.FullName spells generic arguments assembly-qualified; this keeps
    // the failure list readable (IDbContextFactory<BookTrackerDbContext>).
    private static string Display(Type type)
    {
        if (!type.IsGenericType)
        {
            return type.Name;
        }
        var name = type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)];
        return $"{name}<{string.Join(", ", type.GetGenericArguments().Select(Display))}>";
    }

    // A forbidden type counts wherever it appears: by ref, as an array element
    // or as a generic argument (List<Book>, Func<BookTrackerDbContext, Task>).
    private static IEnumerable<Type> Flatten(Type type)
    {
        yield return type;
        if (type.HasElementType)
        {
            foreach (var inner in Flatten(type.GetElementType()!))
            {
                yield return inner;
            }
        }
        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
            {
                foreach (var inner in Flatten(argument))
                {
                    yield return inner;
                }
            }
        }
    }

    private static bool IsDbContextType(Type type) =>
        type == typeof(BookTrackerDbContext)
        || type == typeof(IDbContextFactory<BookTrackerDbContext>)
        || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(DbSet<>));

    // Arrays and by-refs report IsClass and their element's namespace, so a
    // BookStatus[] would look like an entity; Flatten checks the element.
    private static bool IsDataEntity(Type type) =>
        !type.HasElementType
        && type.Namespace == "BookTracker.Data.Models"
        && type.IsClass
        && type.Assembly == typeof(BookTrackerDbContext).Assembly;
}
