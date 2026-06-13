namespace HouseCenter.Api.Options;

public class SessionReminderOptions
{
    public const string SectionName = "SessionReminders";

    /// <summary>How far ahead a Scheduled session triggers an "upcoming" reminder.</summary>
    public int UpcomingWindowHours { get; set; } = 24;

    /// <summary>How often the background scanner runs.</summary>
    public int ScanIntervalMinutes { get; set; } = 15;

    /// <summary>Master switch — disabled in integration tests so the scanner never races.</summary>
    public bool Enabled { get; set; } = true;
}
