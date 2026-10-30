using System.Globalization;
using System.Text;
using MediaMan.Core.Domain;
using MediaMan.Data;
using Microsoft.EntityFrameworkCore;

namespace MediaMan.Auth;

/// <summary>
/// The install's password policy (SPEC §7.11), chosen in setup and changed by the Superadmin. With
/// <paramref name="RequirePasswords"/> off, accounts sign in by picking their account and can have no password;
/// <paramref name="StrongPasswords"/> applies to every password that is set either way.
/// </summary>
public sealed record PasswordPolicy(bool RequirePasswords, bool StrongPasswords)
{
    public const int MinimumLength = 8;
    public const int StrongMinimumLength = 12;

    /// <summary>How many of the four character kinds (uppercase, lowercase, digit, symbol) a strong password mixes.</summary>
    public const int StrongKindsNeeded = 3;

    internal const string RequiredKey = "auth.passwords.required";
    internal const string StrongKey = "auth.passwords.strong";

    /// <summary>A new install's policy, and an existing one's until the Superadmin changes it.</summary>
    public static PasswordPolicy Default { get; } = new(RequirePasswords: true, StrongPasswords: false);

    /// <summary>Why <paramref name="password"/> can't be set under this policy, as a sentence for the user; null if it can.</summary>
    public string? ProblemWith(string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        // Counted in characters as people see them, so an emoji or an accented letter counts once.
        var length = new StringInfo(password).LengthInTextElements;
        var minimum = StrongPasswords ? StrongMinimumLength : MinimumLength;
        if (length < minimum)
        {
            return StrongPasswords
                ? $"Use a password of at least {StrongMinimumLength} characters, mixing at least {StrongKindsNeeded} of: uppercase letters, lowercase letters, digits and symbols."
                : $"Use a password of at least {MinimumLength} characters.";
        }

        if (password.Length > PasswordHasher.MaximumPasswordLength)
        {
            return $"Use a password of at most {PasswordHasher.MaximumPasswordLength} characters.";
        }

        if (StrongPasswords && KindsIn(password) < StrongKindsNeeded)
        {
            return $"Mix at least {StrongKindsNeeded} of: uppercase letters, lowercase letters, digits and symbols.";
        }

        return null;
    }

    private static int KindsIn(string password)
    {
        bool upper = false, lower = false, digit = false, symbol = false;
        foreach (var rune in password.EnumerateRunes())
        {
            if (Rune.IsUpper(rune))
            {
                upper = true;
            }
            else if (Rune.IsLower(rune))
            {
                lower = true;
            }
            else if (Rune.IsDigit(rune))
            {
                digit = true;
            }
            else if (!Rune.IsLetter(rune))
            {
                symbol = true; // punctuation, spaces, emoji: anything that isn't a letter or a digit
            }
        }

        return (upper ? 1 : 0) + (lower ? 1 : 0) + (digit ? 1 : 0) + (symbol ? 1 : 0);
    }

    /// <summary>The stored policy, or <see cref="Default"/> for whatever was never set.</summary>
    public static async Task<PasswordPolicy> LoadAsync(MediaManDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);

        var stored = await db.Settings.AsNoTracking()
            .Where(s => s.Key == RequiredKey || s.Key == StrongKey)
            .ToDictionaryAsync(s => s.Key, s => s.Value, StringComparer.Ordinal, cancellationToken);
        return new PasswordPolicy(
            stored.TryGetValue(RequiredKey, out var required) ? required == "true" : Default.RequirePasswords,
            stored.TryGetValue(StrongKey, out var strong) ? strong == "true" : Default.StrongPasswords);
    }

    /// <summary>Records <paramref name="policy"/>. Saves through the shared context, so it joins any open transaction.</summary>
    public static async Task SaveAsync(MediaManDbContext db, PasswordPolicy policy, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(policy);

        await SetAsync(db, RequiredKey, policy.RequirePasswords, cancellationToken);
        await SetAsync(db, StrongKey, policy.StrongPasswords, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task SetAsync(MediaManDbContext db, string key, bool value, CancellationToken cancellationToken)
    {
        var text = value ? "true" : "false";
        var setting = await db.Settings.SingleOrDefaultAsync(s => s.Key == key, cancellationToken);
        if (setting is null)
        {
            db.Settings.Add(new Setting { Key = key, Value = text });
        }
        else
        {
            setting.Value = text;
        }
    }
}
