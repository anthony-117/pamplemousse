using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Pamplemousse.Api.Auth;

namespace Pamplemousse.Api.Tests;

/// <summary>
/// Firebase-like ID tokens signed with a key made for the test run (research R-8). The test API trusts <see cref="Key"/>
/// for project <see cref="Project"/>, so no test reaches Google.
/// </summary>
public static class TestTokens
{
    public const string Project = "travelgenie-test";
    public const string Uid = "uid-fares";

    public static readonly RsaSecurityKey Key = new(RSA.Create(2048)) { KeyId = "test-key" };

    /// <summary>A key the API doesn't trust, for forged tokens.</summary>
    public static readonly RsaSecurityKey OtherKey = new(RSA.Create(2048)) { KeyId = "test-key" };

    public static string Issuer => FirebaseSignIn.Issuer(Project);

    public static string For(
        string? uid = Uid,
        bool? verified = true,
        string? email = "fares@example.com",
        string? name = "Fares Haddad",
        DateTimeOffset? issuedAt = null,
        DateTimeOffset? authTime = null,
        string audience = Project,
        string? issuer = null,
        SecurityKey? key = null)
    {
        var issued = issuedAt ?? DateTimeOffset.UtcNow;
        var claims = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["auth_time"] = (authTime ?? issued).ToUnixTimeSeconds(),
            ["firebase"] = new Dictionary<string, object>(StringComparer.Ordinal) { ["sign_in_provider"] = "password" },
        };
        Add(claims, "sub", uid);
        Add(claims, "user_id", uid);
        Add(claims, "email", email);
        Add(claims, "name", name);
        if (verified is { } v)
        {
            claims["email_verified"] = v;
        }

        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer ?? Issuer,
            Audience = audience,
            IssuedAt = issued.UtcDateTime,
            NotBefore = issued.UtcDateTime,
            Expires = issued.AddHours(1).UtcDateTime,
            Claims = claims,
            SigningCredentials = new SigningCredentials(key ?? Key, SecurityAlgorithms.RsaSha256),
        });
    }

    public static AuthenticationHeaderValue Bearer(string token) => new("Bearer", token);

    private static void Add(Dictionary<string, object> claims, string name, string? value)
    {
        if (value is not null)
        {
            claims[name] = value;
        }
    }
}
