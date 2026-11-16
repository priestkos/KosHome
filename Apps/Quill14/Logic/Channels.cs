using System.Text;
using System.Text.RegularExpressions;

namespace KosHome.Apps.Quill14;

/// <summary>Saved as numbers: values are pinned, append only.</summary>
public enum Channel
{
    Say = 0,
    Party = 1,
    Tell = 2,
    Reply = 3,
}

public static partial class Channels
{
    /// <summary>Whole chat line (prefix + space + body) stays under the game's 500-byte cap with Wordsmith's margin.</summary>
    public const int MaxLineBytes = 470;

    public static string Label(Channel c) => c switch
    {
        Channel.Say => "Say",
        Channel.Party => "Party",
        Channel.Tell => "Tell",
        _ => "Reply",
    };

    /// <summary>
    /// The command prefix for a post, or null when it cannot be built. Tell uses the typed target, Reply the
    /// locked last-tell sender; both go out as /tell so a new tell mid-post cannot redirect the rest.
    /// The target check lives here so no call path can skip it.
    /// </summary>
    public static string? Prefix(Channel c, string? tellTarget, string? lastTellSender) => c switch
    {
        Channel.Say => "/s",
        Channel.Party => "/p",
        Channel.Tell => IsValidTarget(tellTarget) ? $"/tell {tellTarget!.Trim()}" : null,
        Channel.Reply => IsValidTarget(lastTellSender) ? $"/tell {lastTellSender!.Trim()}" : null,
        _ => null,
    };

    /// <summary>Forename Surname@World — nothing else, so a target can never smuggle a second command.</summary>
    public static bool IsValidTarget(string? target) => target is not null && TargetRegex().IsMatch(target.Trim());

    /// <summary>One line the game will take as chat, not as a command of its own.</summary>
    public static bool IsSendable(string prefix, string body) =>
        body.Length > 0 && !body.StartsWith('/') && !body.Any(char.IsControl) &&
        Encoding.UTF8.GetByteCount(prefix) + 1 + Encoding.UTF8.GetByteCount(body) <= MaxLineBytes;

    [GeneratedRegex(@"^[A-Za-z][A-Za-z'\-]{1,14} [A-Za-z][A-Za-z'\-]{1,14}@[A-Za-z]{3,16}$")]
    private static partial Regex TargetRegex();
}
