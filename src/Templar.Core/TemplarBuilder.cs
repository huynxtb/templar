using Microsoft.Extensions.DependencyInjection;

namespace Templar;

/// <summary>
/// Returned by <c>AddTemplar</c> so a database provider can be attached:
/// <c>services.AddTemplar().UseMySql(connectionString)</c>.
/// </summary>
public sealed class TemplarBuilder
{
    internal TemplarBuilder(IServiceCollection services)
        => Services = services;

    /// <summary>The service collection being configured.</summary>
    public IServiceCollection Services { get; }
}
