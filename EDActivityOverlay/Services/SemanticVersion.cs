using System.Numerics;
using System.Text.RegularExpressions;

namespace EDActivityOverlay.Services;

/// <summary>SemVer 2.0 precedence; build metadata never affects ordering.</summary>
internal sealed class SemanticVersion : IComparable<SemanticVersion>
{
    private static readonly Regex Pattern = new(
        @"^v?(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly BigInteger major;
    private readonly BigInteger minor;
    private readonly BigInteger patch;
    private readonly string[] prerelease;

    private SemanticVersion(string value, Match match)
    {
        Value = value;
        major = BigInteger.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        minor = BigInteger.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
        patch = BigInteger.Parse(match.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
        prerelease = match.Groups[4].Success ? match.Groups[4].Value.Split('.') : [];
    }

    public string Value { get; }
    public string DisplayValue => Value.TrimStart('v').Split('+')[0];
    public bool IsPrerelease => prerelease.Length != 0;

    public static bool TryParse(string? value, out SemanticVersion? version)
    {
        version = null;
        if (string.IsNullOrEmpty(value)) return false;
        Match match = Pattern.Match(value);
        if (!match.Success) return false;
        if (match.Groups[4].Success && match.Groups[4].Value.Split('.').Any(
            identifier => identifier.Length > 1 && identifier[0] == '0' && identifier.All(char.IsAsciiDigit)))
            return false;
        version = new SemanticVersion(value, match);
        return true;
    }

    public int CompareTo(SemanticVersion? other)
    {
        if (other is null) return 1;
        int result = major.CompareTo(other.major);
        if (result != 0) return result;
        result = minor.CompareTo(other.minor);
        if (result != 0) return result;
        result = patch.CompareTo(other.patch);
        if (result != 0) return result;
        if (!IsPrerelease || !other.IsPrerelease)
            return IsPrerelease.CompareTo(other.IsPrerelease) * -1;
        for (int i = 0; i < Math.Min(prerelease.Length, other.prerelease.Length); i++)
        {
            string left = prerelease[i], right = other.prerelease[i];
            bool leftNumeric = left.All(char.IsAsciiDigit), rightNumeric = right.All(char.IsAsciiDigit);
            if (leftNumeric != rightNumeric) return leftNumeric ? -1 : 1;
            result = leftNumeric
                ? left.Length == right.Length ? string.CompareOrdinal(left, right) : left.Length.CompareTo(right.Length)
                : string.CompareOrdinal(left, right);
            if (result != 0) return result;
        }
        return prerelease.Length.CompareTo(other.prerelease.Length);
    }

    public override string ToString() => Value;
}
