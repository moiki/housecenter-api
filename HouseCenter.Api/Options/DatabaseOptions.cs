namespace HouseCenter.Api.Options;

public class DatabaseOptions
{
    public const string SectionName = "Database";

    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Apply pending EF migrations on startup. Useful for tests and single-replica deploys.</summary>
    public bool ApplyMigrationsAtStartup { get; set; }

    /// <summary>TESTS ONLY: drop and recreate the database on startup.</summary>
    public bool RecreateOnStartup { get; set; }
}
