using System.Diagnostics.CodeAnalysis;

namespace Templar;

/// <summary>
/// Default comparer used to match placeholder names against supplied values. It ignores case
/// and the separators <c>_</c>, <c>-</c>, <c>.</c> and space, so a single value named
/// <c>username</c> satisfies <c>{{username}}</c>, <c>{{UserName}}</c> and <c>{{USER_NAME}}</c>.
/// </summary>
/// <remarks>
/// Pass <see cref="StringComparer.Ordinal"/> (or any other comparer) to
/// <see cref="TemplateValues.Create(IEqualityComparer{string}?)"/> when exact matching is required.
/// </remarks>
public sealed class TemplateVariableNameComparer : IEqualityComparer<string>
{
    /// <summary>Shared instance.</summary>
    public static readonly TemplateVariableNameComparer Instance = new();

    private TemplateVariableNameComparer() { }

    private static bool IsSeparator(char c) => c is '_' or '-' or '.' or ' ';

    public bool Equals(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x is null || y is null) return false;

        int i = 0, j = 0;
        while (true)
        {
            while (i < x.Length && IsSeparator(x[i])) i++;
            while (j < y.Length && IsSeparator(y[j])) j++;

            if (i == x.Length || j == y.Length) return i == x.Length && j == y.Length;
            if (char.ToUpperInvariant(x[i]) != char.ToUpperInvariant(y[j])) return false;

            i++;
            j++;
        }
    }

    public int GetHashCode([DisallowNull] string obj)
    {
        var hash = new HashCode();
        foreach (var c in obj)
        {
            if (IsSeparator(c)) continue;
            hash.Add(char.ToUpperInvariant(c));
        }

        return hash.ToHashCode();
    }
}
