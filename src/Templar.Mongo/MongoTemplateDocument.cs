using MongoDB.Bson.Serialization.Attributes;

namespace Templar.Mongo;

/// <summary>
/// Storage shape of a template document. The natural key (template key + culture + channel) is
/// used as <c>_id</c>, which makes an upsert a single indexed write and rules out duplicates
/// without a second unique index.
/// </summary>
public sealed class MongoTemplateDocument
{
    [BsonId]
    public MongoTemplateId Id { get; set; } = new();

    [BsonElement("name")]
    [BsonIgnoreIfNull]
    public string? Name { get; set; }

    [BsonElement("description")]
    [BsonIgnoreIfNull]
    public string? Description { get; set; }

    [BsonElement("subject")]
    [BsonIgnoreIfNull]
    public string? Subject { get; set; }

    [BsonElement("textBody")]
    [BsonIgnoreIfNull]
    public string? TextBody { get; set; }

    [BsonElement("htmlBody")]
    [BsonIgnoreIfNull]
    public string? HtmlBody { get; set; }

    [BsonElement("isActive")]
    public bool IsActive { get; set; } = true;

    [BsonElement("updatedAt")]
    public DateTime UpdatedAtUtc { get; set; }

    public static MongoTemplateDocument FromDefinition(TemplateDefinition template)
    {
        ArgumentNullException.ThrowIfNull(template);

        return new MongoTemplateDocument
        {
            Id = new MongoTemplateId
            {
                TemplateKey = template.TemplateKey,
                Culture = template.Culture,
                Channel = template.Channel.ToString(),
            },
            Name = template.Name,
            Description = template.Description,
            Subject = template.Subject,
            TextBody = template.TextBody,
            HtmlBody = template.HtmlBody,
            IsActive = template.IsActive,
            UpdatedAtUtc = template.UpdatedAtUtc.UtcDateTime,
        };
    }

    public TemplateDefinition ToDefinition()
    {
        if (!Enum.TryParse<TemplateChannel>(Id.Channel, ignoreCase: true, out var channel))
        {
            throw new InvalidOperationException(
                $"Template '{Id.TemplateKey}' ({Id.Culture}) has an unknown channel '{Id.Channel}'. "
                + $"Expected one of: {string.Join(", ", Enum.GetNames<TemplateChannel>())}.");
        }

        return new TemplateDefinition
        {
            TemplateKey = Id.TemplateKey,
            Culture = Id.Culture,
            Channel = channel,
            Name = Name,
            Description = Description,
            Subject = Subject,
            TextBody = TextBody,
            HtmlBody = HtmlBody,
            IsActive = IsActive,
            UpdatedAtUtc = new DateTimeOffset(DateTime.SpecifyKind(UpdatedAtUtc, DateTimeKind.Utc)),
        };
    }
}

/// <summary>Composite <c>_id</c> of a template document.</summary>
public sealed class MongoTemplateId
{
    [BsonElement("templateKey")]
    public string TemplateKey { get; set; } = string.Empty;

    [BsonElement("culture")]
    public string Culture { get; set; } = string.Empty;

    /// <summary>Channel name, stored as text so documents stay readable in the shell.</summary>
    [BsonElement("channel")]
    public string Channel { get; set; } = string.Empty;

    public override string ToString() => $"{TemplateKey}/{Culture}/{Channel}";
}
