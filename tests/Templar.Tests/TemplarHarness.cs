using Microsoft.Extensions.Options;
using Templar.Abstractions;
using Templar.Caching;
using Templar.Rendering;
using Templar.Services;
using Templar.Stores;

namespace Templar.Tests;

/// <summary>
/// Builds the three services over one store, the way <c>AddTemplar</c> wires them, plus the shared
/// seed the service tests work against.
/// </summary>
internal sealed class TemplarHarness(
    ITemplateQueryService queries,
    ITemplateRenderService render,
    ITemplateCommandService? commands,
    ITemplateCache cache)
{
    public ITemplateQueryService Queries { get; } = queries;

    public ITemplateRenderService Render { get; } = render;

    /// <summary><see langword="null"/> when the store is read-only.</summary>
    public ITemplateCommandService? Commands { get; } = commands;

    public ITemplateCache Cache { get; } = cache;

    public static TemplarHarness Create(
        ITemplateStore store,
        Action<TemplateOptions>? configure = null,
        ITemplateCache? cache = null)
    {
        var options = new TemplateOptions();
        configure?.Invoke(options);
        var wrapped = Options.Create(options);

        cache ??= options.EnableCache ? new MemoryTemplateCache(wrapped) : NullTemplateCache.Instance;

        var queries = new TemplateQueryService(store, cache, wrapped);

        return new TemplarHarness(
            queries,
            new TemplateRenderService(queries, new MustacheTemplateCompiler(), new TemplateRenderer(), wrapped),
            store is ITemplateWriteStore writable ? new TemplateCommandService(writable, cache) : null,
            cache);
    }

    /// <summary>A harness over the shared seed.</summary>
    public static TemplarHarness Create(Action<TemplateOptions>? configure = null)
        => Create(new InMemoryTemplateStore(Seed), configure);

    public static TemplateValues Values() => TemplateValues.Create()
        .Set("username", "huy")
        .Set("EMAIL", "huy@example.com");

    private static TemplateDefinition Welcome(string culture, string subject, string content) => new()
    {
        TemplateKey = "welcome-user",
        Culture = culture,
        Channel = TemplateChannel.Email,
        Subject = subject,
        TextBody = content,
        HtmlBody = $"<p>{content}</p>",
        UpdatedAtUtc = DateTimeOffset.UnixEpoch,
    };

    public static TemplateDefinition[] Seed =>
    [
        Welcome("en", "Welcome to XXX", "Hello {{username}}, welcome to XXX, this is your email {{EMAIL}}"),
        Welcome("vi", "Chào mừng tới XXX", "Xin chào {{username}}, chào mừng tới XXX, đây là email của bạn {{EMAIL}}")
            with { Name = "Email chào mừng", Description = "Gửi sau khi xác nhận email." },
        new()
        {
            TemplateKey = "welcome-user",
            Culture = "en",
            Channel = TemplateChannel.InApp,
            Subject = "Welcome!",
            TextBody = "Hi {{username}} 👋",
            UpdatedAtUtc = DateTimeOffset.UnixEpoch,
        },
        new()
        {
            TemplateKey = "welcome-user",
            Culture = "vi",
            Channel = TemplateChannel.Other,
            Name = "SMS chào mừng",
            Description = "Kênh Other: chỉ có text.",
            TextBody = "XXX: chao {{username}}",
            UpdatedAtUtc = DateTimeOffset.UnixEpoch,
        },
        new()
        {
            TemplateKey = "welcome-user",
            Culture = "fr",
            Channel = TemplateChannel.Email,
            Subject = "Bienvenue",
            TextBody = "Bonjour {{username}}",
            IsActive = false,
            UpdatedAtUtc = DateTimeOffset.UnixEpoch,
        },
    ];

    /// <summary>Counts store reads, so caching can be asserted. Writes pass straight through.</summary>
    internal sealed class CountingStore(InMemoryTemplateStore inner) : ITemplateWriteStore
    {
        public int Reads { get; private set; }

        public Task<IReadOnlyList<TemplateDefinition>> GetTemplateSetAsync(
            string templateKey,
            CancellationToken cancellationToken = default)
        {
            Reads++;
            return inner.GetTemplateSetAsync(templateKey, cancellationToken);
        }

        public Task<IReadOnlyList<string>> ListTemplateKeysAsync(CancellationToken cancellationToken = default)
            => inner.ListTemplateKeysAsync(cancellationToken);

        public Task<IReadOnlyList<TemplateDefinition>> GetAllTemplatesAsync(CancellationToken cancellationToken = default)
        {
            Reads++;
            return inner.GetAllTemplatesAsync(cancellationToken);
        }

        public Task UpsertAsync(TemplateDefinition template, CancellationToken cancellationToken = default)
            => inner.UpsertAsync(template, cancellationToken);

        public Task<bool> DeleteAsync(
            string templateKey,
            string culture,
            TemplateChannel channel,
            CancellationToken cancellationToken = default)
            => inner.DeleteAsync(templateKey, culture, channel, cancellationToken);
    }
}
