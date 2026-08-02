using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using Templar.Abstractions;

namespace Templar.Mongo;

/// <summary>Options for the MongoDB store.</summary>
public sealed class MongoTemplateStoreOptions
{
    /// <summary>MongoDB connection string, for example <c>mongodb://localhost:27017</c>.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Database holding the templates.</summary>
    public string DatabaseName { get; set; } = "notifications";

    /// <summary>Collection holding the templates. Defaults to <c>notification_templates</c>.</summary>
    public string CollectionName { get; set; } = "notification_templates";

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
            throw new InvalidOperationException($"{nameof(MongoTemplateStoreOptions)}.{nameof(ConnectionString)} must be set.");
        if (string.IsNullOrWhiteSpace(DatabaseName))
            throw new InvalidOperationException($"{nameof(MongoTemplateStoreOptions)}.{nameof(DatabaseName)} must be set.");
        if (string.IsNullOrWhiteSpace(CollectionName))
            throw new InvalidOperationException($"{nameof(MongoTemplateStoreOptions)}.{nameof(CollectionName)} must be set.");
    }
}

/// <summary>
/// Template store backed by MongoDB.
/// </summary>
/// <remarks>
/// Unlike the SQL providers, template keys are matched exactly: MongoDB comparisons are
/// byte-wise unless a collation is configured. Culture matching still happens in
/// the query service and stays case-insensitive.
/// </remarks>
/// <param name="collection">
/// Collection holding the template documents, for callers that manage the Mongo client themselves.
/// </param>
/// <param name="logger">Optional logger; defaults to <see cref="NullLogger"/>.</param>
public sealed class MongoTemplateStore(
    IMongoCollection<MongoTemplateDocument> collection,
    ILogger<MongoTemplateStore>? logger = null) : ITemplateWriteStore, ITemplateSchemaInitializer
{
    private readonly IMongoCollection<MongoTemplateDocument> _collection =
        collection ?? throw new ArgumentNullException(nameof(collection));

    private readonly ILogger _logger = logger ?? (ILogger)NullLogger.Instance;

    /// <summary>Connects with its own client, which is the usual way to construct the store.</summary>
    public MongoTemplateStore(MongoTemplateStoreOptions options, ILogger<MongoTemplateStore>? logger = null)
        : this(Connect(options), logger) { }

    private static IMongoCollection<MongoTemplateDocument> Connect(MongoTemplateStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        return new MongoClient(options.ConnectionString)
            .GetDatabase(options.DatabaseName)
            .GetCollection<MongoTemplateDocument>(options.CollectionName);
    }

    public async Task<IReadOnlyList<TemplateDefinition>> GetTemplateSetAsync(
        string templateKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateKey);

        var filter = Builders<MongoTemplateDocument>.Filter.Eq(d => d.Id.TemplateKey, templateKey);
        var documents = await _collection.Find(filter).ToListAsync(cancellationToken).ConfigureAwait(false);

        return [.. documents.Select(d => d.ToDefinition())];
    }

    public async Task<IReadOnlyList<string>> ListTemplateKeysAsync(CancellationToken cancellationToken = default)
    {
        var keys = await _collection
            .Distinct(new StringFieldDefinition<MongoTemplateDocument, string>("_id.templateKey"),
                Builders<MongoTemplateDocument>.Filter.Empty,
                cancellationToken: cancellationToken)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        keys.Sort(StringComparer.OrdinalIgnoreCase);
        return keys;
    }

    public async Task<IReadOnlyList<TemplateDefinition>> GetAllTemplatesAsync(CancellationToken cancellationToken = default)
    {
        var sort = Builders<MongoTemplateDocument>.Sort
            .Ascending("_id.templateKey")
            .Ascending("_id.culture")
            .Ascending("_id.channel");

        var documents = await _collection
            .Find(Builders<MongoTemplateDocument>.Filter.Empty)
            .Sort(sort)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. documents.Select(d => d.ToDefinition())];
    }

    public async Task UpsertAsync(TemplateDefinition template, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentException.ThrowIfNullOrWhiteSpace(template.TemplateKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(template.Culture);

        var document = MongoTemplateDocument.FromDefinition(template);
        var filter = Builders<MongoTemplateDocument>.Filter.Eq(d => d.Id, document.Id);

        await _collection
            .ReplaceOneAsync(filter, document, new ReplaceOptions { IsUpsert = true }, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(
        string templateKey,
        string culture,
        TemplateChannel channel,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(culture);

        var id = new MongoTemplateId
        {
            TemplateKey = templateKey,
            Culture = culture,
            Channel = channel.ToString(),
        };

        var result = await _collection
            .DeleteOneAsync(Builders<MongoTemplateDocument>.Filter.Eq(d => d.Id, id), cancellationToken)
            .ConfigureAwait(false);

        return result.DeletedCount > 0;
    }

    /// <summary>
    /// Creates the index that backs <see cref="GetTemplateSetAsync"/>. The collection itself is
    /// created implicitly by the first write, and <c>_id</c> already enforces uniqueness.
    /// </summary>
    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        var index = new CreateIndexModel<MongoTemplateDocument>(
            Builders<MongoTemplateDocument>.IndexKeys.Ascending("_id.templateKey"),
            new CreateIndexOptions { Name = "ix_template_key" });

        await _collection.Indexes.CreateOneAsync(index, cancellationToken: cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Template collection {Collection} is ready.", _collection.CollectionNamespace);
    }
}
