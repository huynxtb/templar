using Microsoft.Extensions.DependencyInjection;
using Templar.Scriban;

namespace Templar;

/// <summary>Tunes the Scriban rendering engine that <c>AddTemplar</c> already registered.</summary>
public static class ScribanTemplarBuilderExtensions
{
    /// <summary>
    /// Configures the Scriban engine. Scriban is the default, so this is only needed to change one of
    /// its settings — the loop limit, Liquid syntax, or a <c>TemplateLoader</c> for <c>{{ include }}</c>.
    /// Calling <c>AddTemplar()</c> alone already gives you <c>{{ if }}</c>, <c>{{ for }}</c> and pipes.
    /// </summary>
    /// <remarks>
    /// Bodies written for Templar 1.0 mostly carry over — <c>{{ username }}</c> still means the same
    /// thing — with two exceptions worth knowing before pointing this at a live table:
    /// <c>{{DATE:dd/MM/yyyy}}</c> becomes <c>{{ DATE | format 'dd/MM/yyyy' }}</c> (rejected at compile
    /// time until it is, see <see cref="ScribanOptions.RejectLegacyFormatSyntax"/>), and text that
    /// merely looks like a placeholder is now a syntax error rather than literal text.
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddTemplar()
    ///         .UsePostgreSql(connectionString)
    ///         .UseScriban(options => options.LoopLimit = 5000);
    /// </code>
    /// </example>
    public static TemplarBuilder UseScriban(
        this TemplarBuilder builder,
        Action<ScribanOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (configure is not null) builder.Services.Configure(configure);

        return builder;
    }
}
