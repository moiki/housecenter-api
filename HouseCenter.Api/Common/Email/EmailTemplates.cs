namespace HouseCenter.Api.Common.Email;

public static class EmailTemplates
{
    public static string Invitation(string inviteUrl, string roleName) => $"""
        <html><body style="font-family: sans-serif; color: #333;">
          <h2>You have been invited to HouseCenter</h2>
          <p>You were invited to join HouseCenter as <strong>{roleName}</strong>.</p>
          <p>This invitation expires in 72 hours.</p>
          <p><a href="{inviteUrl}" style="background:#2563eb;color:#fff;padding:10px 20px;border-radius:6px;text-decoration:none;">Accept invitation</a></p>
          <p style="color:#777;font-size:12px;">If the button does not work, copy this link: {inviteUrl}</p>
        </body></html>
        """;

    public static string Notification(string title, string body) => $"""
        <html><body style="font-family: sans-serif; color: #333;">
          <h2>{title}</h2>
          <p>{body}</p>
          <p style="color:#777;font-size:12px;">You are receiving this because you have activity in HouseCenter.</p>
        </body></html>
        """;

    public static string PasswordReset(string resetUrl) => $"""
        <html><body style="font-family: sans-serif; color: #333;">
          <h2>Reset your HouseCenter password</h2>
          <p>We received a request to reset your password. This link expires in 1 hour.</p>
          <p><a href="{resetUrl}" style="background:#2563eb;color:#fff;padding:10px 20px;border-radius:6px;text-decoration:none;">Reset password</a></p>
          <p style="color:#777;font-size:12px;">If you did not request this, you can safely ignore this email.</p>
        </body></html>
        """;
}
