using System.Globalization;

namespace Templar;

/// <summary>
/// Builds the ordered list of cultures to try for a request: the requested culture, its parents,
/// then the configured default and its parents.
/// </summary>
/// <example>
/// Requesting <c>vi-VN</c> with a default of <c>en-US</c> yields
/// <c>vi-VN</c>, <c>vi</c>, <c>en-US</c>, <c>en</c>.
/// </example>
public static class CultureFallback
{
    /// <summary>Comparer used to match culture names, which are case-insensitive by definition.</summary>
    public static IEqualityComparer<string> NameComparer => StringComparer.OrdinalIgnoreCase;

    /// <summary>Produces the candidate chain, without duplicates.</summary>
    public static IReadOnlyList<string> GetCandidates(string culture, string defaultCulture, bool enableFallback)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(culture);

        var candidates = new List<string>(4);
        var seen = new HashSet<string>(NameComparer);

        void Add(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            if (seen.Add(name)) candidates.Add(name);
        }

        Add(culture.Trim());
        if (!enableFallback) return candidates;

        AddParents(culture, Add);

        if (!string.IsNullOrWhiteSpace(defaultCulture))
        {
            Add(defaultCulture.Trim());
            AddParents(defaultCulture, Add);
        }

        return candidates;
    }

    private static void AddParents(string culture, Action<string?> add)
    {
        var info = TryGetCulture(culture);
        if (info is null) return;

        // "vi-VN" -> "vi"; the walk stops at the invariant culture, whose name is empty.
        for (var parent = info.Parent; !string.IsNullOrEmpty(parent.Name); parent = parent.Parent)
            add(parent.Name);
    }

    /// <summary>
    /// Resolves a <see cref="CultureInfo"/> used to format values, falling back to
    /// <see cref="CultureInfo.InvariantCulture"/> for names the platform does not know.
    /// </summary>
    public static CultureInfo GetFormattingCulture(string culture)
        => TryGetCulture(culture) ?? CultureInfo.InvariantCulture;

    private static CultureInfo? TryGetCulture(string culture)
    {
        if (string.IsNullOrWhiteSpace(culture)) return null;

        try
        {
            // predefinedOnly: false keeps custom tags such as "en-XX" usable as storage keys.
            return CultureInfo.GetCultureInfo(culture.Trim(), predefinedOnly: false);
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }
}
