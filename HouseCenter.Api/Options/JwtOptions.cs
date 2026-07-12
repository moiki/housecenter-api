namespace HouseCenter.Api.Options;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Secret { get; set; } = string.Empty;
    public string InvitationSecret { get; set; } = string.Empty;
    public string ResetSecret { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public int AccessTokenExpiryMinutes { get; set; } = 60;
    public int RefreshTokenExpiryDays { get; set; } = 7;
    public int InvitationTokenExpiryHours { get; set; } = 72;
    public int ResetTokenExpiryHours { get; set; } = 1;
}
