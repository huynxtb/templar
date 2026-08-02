using System.Collections.Concurrent;
using Templar.Abstractions;

namespace Templar.Stores;

/// <summary>
/// Thread-safe in-memory store. Useful for unit tests, samples and for seeding defaults, and it
/// gives the sample application something to run against without a database.
/// </summary>
public sealed class InMemoryTemplateStore : ITemplateWriteStore
{
    private readonly ConcurrentDictionary<string, ImmutableSet> _templates =
        new(StringComparer.OrdinalIgnoreCase);

    public InMemoryTemplateStore() { }

    public InMemoryTemplateStore(IEnumerable<TemplateDefinition> templates)
    {
        ArgumentNullException.ThrowIfNull(templates);
        foreach (var template in templates) Upsert(template);
    }

    /// <summary>Synchronous counterpart of <see cref="UpsertAsync"/>.</summary>
    public void Upsert(TemplateDefinition template)
    {
        ArgumentNullException.ThrowIfNull(template);

        _templates.AddOrUpdate(
            template.TemplateKey,
            static (_, added) => new ImmutableSet([added]),
            static (_, existing, added) => existing.With(added),
            template);
    }

    public Task UpsertAsync(TemplateDefinition template, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Upsert(template);
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(
        string templateKey,
        string culture,
        TemplateChannel channel,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateKey);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_templates.TryGetValue(templateKey, out var existing)) return Task.FromResult(false);

        var reduced = existing.Without(culture, channel);
        if (reduced.Items.Count == existing.Items.Count) return Task.FromResult(false);

        if (reduced.Items.Count == 0) _templates.TryRemove(templateKey, out _);
        else _templates[templateKey] = reduced;

        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<TemplateDefinition>> GetTemplateSetAsync(
        string templateKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateKey);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_templates.TryGetValue(templateKey, out var set)
            ? set.Items
            : (IReadOnlyList<TemplateDefinition>)[]);
    }

    public Task<IReadOnlyList<string>> ListTemplateKeysAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var keys = _templates.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToArray();
        return Task.FromResult<IReadOnlyList<string>>(keys);
    }

    public Task<IReadOnlyList<TemplateDefinition>> GetAllTemplatesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var all = _templates
            .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .SelectMany(entry => entry.Value.Items
                .OrderBy(template => template.Culture, StringComparer.OrdinalIgnoreCase)
                .ThenBy(template => template.Channel))
            .ToArray();

        return Task.FromResult<IReadOnlyList<TemplateDefinition>>(all);
    }

    /// <summary>
    /// Copy-on-write list of the variants of one key, so readers never observe a partial update
    /// and never need a lock.
    /// </summary>
    private sealed class ImmutableSet(IReadOnlyList<TemplateDefinition> items)
    {
        public IReadOnlyList<TemplateDefinition> Items { get; } = items;

        public ImmutableSet With(TemplateDefinition template)
        {
            var replaced = new List<TemplateDefinition>(Items.Count + 1);
            replaced.AddRange(Items.Where(existing => !Matches(existing, template.Culture, template.Channel)));
            replaced.Add(template);
            return new ImmutableSet(replaced);
        }

        public ImmutableSet Without(string culture, TemplateChannel channel)
            => new([.. Items.Where(existing => !Matches(existing, culture, channel))]);

        private static bool Matches(TemplateDefinition definition, string culture, TemplateChannel channel)
            => definition.Channel == channel
               && CultureFallback.NameComparer.Equals(definition.Culture, culture);
    }
}
