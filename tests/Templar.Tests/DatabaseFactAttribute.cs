using Xunit;

namespace Templar.Tests;

/// <summary>
/// A test that needs a live database. It is skipped unless the named environment variable holds a
/// connection string, so <c>dotnet test</c> stays green on a machine without any servers running.
/// </summary>
/// <example>
/// <code>
/// TEMPLAR_POSTGRES="Host=localhost;Port=5432;Database=notifications;Username=postgres;Password=secret" \
///   dotnet test
/// </code>
/// </example>
public sealed class DatabaseFactAttribute : FactAttribute
{
    public DatabaseFactAttribute(string variable)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable)))
            Skip = $"Set {variable} to a connection string to run this test.";
    }

    /// <summary>Reads the connection string the test was gated on.</summary>
    public static string ConnectionString(string variable)
        => Environment.GetEnvironmentVariable(variable)
           ?? throw new InvalidOperationException($"{variable} is not set.");
}
