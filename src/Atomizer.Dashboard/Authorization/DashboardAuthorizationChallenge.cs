using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Atomizer.Dashboard.Authorization;

internal static class DashboardAuthorizationChallenge
{
    private static readonly object ChallengesKey = new();

    public static void RegisterBasic(HttpContext context, string realm)
    {
        ArgumentNullException.ThrowIfNull(context);

        Register(context, $"Basic realm=\"{EscapeHeaderValue(realm)}\", charset=\"UTF-8\"");
    }

    public static void Apply(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (
            !context.Items.TryGetValue(ChallengesKey, out var value)
            || value is not List<string> challenges
            || challenges.Count == 0
        )
        {
            return;
        }

        context.Response.Headers.WWWAuthenticate = new StringValues(challenges.ToArray());
    }

    private static void Register(HttpContext context, string challenge)
    {
        var challenges = GetOrCreateChallenges(context);
        if (!challenges.Contains(challenge, StringComparer.Ordinal))
            challenges.Add(challenge);
    }

    private static List<string> GetOrCreateChallenges(HttpContext context)
    {
        if (context.Items.TryGetValue(ChallengesKey, out var value) && value is List<string> challenges)
            return challenges;

        challenges = [];
        context.Items[ChallengesKey] = challenges;
        return challenges;
    }

    private static string EscapeHeaderValue(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
