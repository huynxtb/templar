using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Templar;

/// <summary>
/// The values substituted into a template's placeholders.
/// </summary>
/// <remarks>
/// Names are matched with <see cref="TemplateVariableNameComparer"/> by default, which ignores
/// case and separators. Values are formatted with the target template's culture, so a
/// <see cref="DateTime"/> renders as <c>31/07/2026</c> for <c>vi</c> and <c>7/31/2026</c> for
/// <c>en-US</c>. Wrap a value in <see cref="TemplateRaw"/> to bypass HTML encoding.
/// </remarks>
public sealed class TemplateValues : IEnumerable<KeyValuePair<string, object?>>
{
    private readonly Dictionary<string, object?> _values;

    private TemplateValues(Dictionary<string, object?> values) => _values = values;

    /// <summary>An empty, immutable value set.</summary>
    public static TemplateValues Empty { get; } = new(new Dictionary<string, object?>(0, TemplateVariableNameComparer.Instance));

    /// <summary>Creates an empty set, optionally with a custom name comparer.</summary>
    public static TemplateValues Create(IEqualityComparer<string>? comparer = null)
        => new(new Dictionary<string, object?>(comparer ?? TemplateVariableNameComparer.Instance));

    /// <summary>Creates a set from existing pairs.</summary>
    public static TemplateValues From(
        IEnumerable<KeyValuePair<string, object?>> values,
        IEqualityComparer<string>? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(values);

        var result = Create(comparer);
        foreach (var (name, value) in values) result.Set(name, value);
        return result;
    }

    /// <summary>
    /// Creates a set from the public readable properties of <paramref name="source"/>, which is
    /// typically an anonymous object such as <c>new { username = "huy", EMAIL = "a@b.com" }</c>.
    /// </summary>
    public static TemplateValues FromObject(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] object source,
        IEqualityComparer<string>? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source is TemplateValues already) return already;
        if (source is IEnumerable<KeyValuePair<string, object?>> pairs) return From(pairs, comparer);

        var result = Create(comparer);
        foreach (var property in source.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead || property.GetIndexParameters().Length != 0) continue;
            result.Set(property.Name, property.GetValue(source));
        }

        return result;
    }

    /// <summary>Adds or replaces a value and returns the same instance so calls can be chained.</summary>
    public TemplateValues Set(string name, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (ReferenceEquals(this, Empty))
            throw new InvalidOperationException($"{nameof(TemplateValues)}.{nameof(Empty)} is immutable; use {nameof(Create)}() instead.");

        _values[name] = value;
        return this;
    }

    /// <summary>Looks up a placeholder value.</summary>
    public bool TryGetValue(string name, out object? value) => _values.TryGetValue(name, out value);

    /// <summary>True when a value with this name exists (even if it is <see langword="null"/>).</summary>
    public bool Contains(string name) => _values.ContainsKey(name);

    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => _values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
