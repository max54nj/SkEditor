using System;

namespace SkEditor.Utilities.InternalAPI;

/// <summary>
///     Utility class for parsing and comparing npm-style version ranges.
/// </summary>
public static class VersionParser
{
    /// <summary>
    ///     Check if a version satisfies a version range.
    /// </summary>
    /// <param name="version">The version to check.</param>
    /// <param name="range">The version range (npm-style: ^1.2.3, ~1.2.3, >=1.0.0, etc.)</param>
    /// <returns>True if the version satisfies the range, false otherwise.</returns>
    public static bool Satisfies(string version, string? range)
    {
        if (string.IsNullOrWhiteSpace(range) || range == "*")
        {
            return true;
        }

        if (!Version.TryParse(version, out Version? parsedVersion))
        {
            return false;
        }

        range = range.Trim();

        // Handle exact version
        if (!range.Contains('>') && !range.Contains('<') && !range.Contains('~') && !range.Contains('^') &&
            !range.Contains('x') && !range.Contains('X') && !range.Contains('*'))
        {
            return Version.TryParse(range, out Version? exactVersion) && parsedVersion.Equals(exactVersion);
        }

        // Handle caret range (^1.2.3 allows changes that do not modify the left-most non-zero digit)
        if (range.StartsWith("^"))
        {
            string versionStr = range.Substring(1);
            if (!Version.TryParse(versionStr, out Version? minVersion))
            {
                return false;
            }

            if (parsedVersion < minVersion)
            {
                return false;
            }

            // If major version is 0, only allow patch updates
            if (minVersion.Major == 0)
            {
                if (minVersion.Minor == 0)
                {
                    return parsedVersion.Major == 0 && parsedVersion.Minor == 0 &&
                           parsedVersion.Build == minVersion.Build;
                }

                return parsedVersion.Major == 0 && parsedVersion.Minor == minVersion.Minor;
            }

            // Otherwise, allow any version with same major
            return parsedVersion.Major == minVersion.Major;
        }

        // Handle tilde range (~1.2.3 allows patch-level changes)
        if (range.StartsWith("~"))
        {
            string versionStr = range.Substring(1);
            if (!Version.TryParse(versionStr, out Version? minVersion))
            {
                return false;
            }

            return parsedVersion >= minVersion &&
                   parsedVersion.Major == minVersion.Major &&
                   parsedVersion.Minor == minVersion.Minor;
        }

        // Handle x-ranges (1.2.x, 1.x, *)
        if (range.Contains('x') || range.Contains('X'))
        {
            string pattern = range.Replace("x", "X");
            string[] parts = pattern.Split('.');

            if (parts.Length >= 1 && parts[0] != "X")
            {
                if (!int.TryParse(parts[0], out int major) || parsedVersion.Major != major)
                {
                    return false;
                }
            }

            if (parts.Length >= 2 && parts[1] != "X")
            {
                if (!int.TryParse(parts[1], out int minor) || parsedVersion.Minor != minor)
                {
                    return false;
                }
            }

            if (parts.Length >= 3 && parts[2] != "X")
            {
                if (!int.TryParse(parts[2], out int build) || parsedVersion.Build != build)
                {
                    return false;
                }
            }

            return true;
        }

        // Handle comparison operators
        if (range.Contains(">=") || range.Contains("<=") || range.Contains(">") || range.Contains("<"))
        {
            // Handle compound ranges like ">=1.0.0 <2.0.0"
            string[] rangeParts = range.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string part in rangeParts)
            {
                if (!EvaluateComparison(parsedVersion, part))
                {
                    return false;
                }
            }

            return true;
        }

        return false;
    }

    private static bool EvaluateComparison(Version version, string comparison)
    {
        comparison = comparison.Trim();

        if (comparison.StartsWith(">="))
        {
            string versionStr = comparison.Substring(2);
            return Version.TryParse(versionStr, out Version? compareVersion) && version >= compareVersion;
        }

        if (comparison.StartsWith("<="))
        {
            string versionStr = comparison.Substring(2);
            return Version.TryParse(versionStr, out Version? compareVersion) && version <= compareVersion;
        }

        if (comparison.StartsWith(">"))
        {
            string versionStr = comparison.Substring(1);
            return Version.TryParse(versionStr, out Version? compareVersion) && version > compareVersion;
        }

        if (comparison.StartsWith("<"))
        {
            string versionStr = comparison.Substring(1);
            return Version.TryParse(versionStr, out Version? compareVersion) && version < compareVersion;
        }

        if (comparison.StartsWith("="))
        {
            string versionStr = comparison.Substring(1);
            return Version.TryParse(versionStr, out Version? compareVersion) && version.Equals(compareVersion);
        }

        // If no operator, try exact match
        return Version.TryParse(comparison, out Version? exactVersion) && version.Equals(exactVersion);
    }
}
