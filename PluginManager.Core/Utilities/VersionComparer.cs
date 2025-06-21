using System.Text.RegularExpressions;

namespace PluginManager.Core.Utilities;

public static class VersionComparer
{
    /// <summary>
    /// Compares two version strings and determines if the latest version is newer than the current version.
    /// Handles common version formats like "v1.2.3", "1.2.3", "1.2.3-beta", etc.
    /// </summary>
    /// <param name="currentVersion">Current version string</param>
    /// <param name="latestVersion">Latest version string to compare against</param>
    /// <returns>True if latestVersion is newer than currentVersion</returns>
    public static bool IsNewerVersion(string? currentVersion, string? latestVersion)
    {
        if (string.IsNullOrWhiteSpace(currentVersion) || string.IsNullOrWhiteSpace(latestVersion))
            return false;

        // Clean version strings (remove 'v' prefix and other non-version characters)
        var cleanCurrent = CleanVersionString(currentVersion);
        var cleanLatest = CleanVersionString(latestVersion);

        // If both are identical after cleaning, no update needed
        if (string.Equals(cleanCurrent, cleanLatest, StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            // Try to parse as System.Version first (most reliable)
            if (TryParseAsSystemVersion(cleanCurrent, out var currentVer) && 
                TryParseAsSystemVersion(cleanLatest, out var latestVer))
            {
                return latestVer > currentVer;
            }

            // Fallback to semantic version comparison
            return CompareSemanticVersions(cleanCurrent, cleanLatest) > 0;
        }
        catch
        {
            // Final fallback to string comparison
            return string.Compare(cleanLatest, cleanCurrent, StringComparison.OrdinalIgnoreCase) > 0;
        }
    }

    private static string CleanVersionString(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return "0.0.0";

        // Remove 'v' prefix and any leading non-numeric characters
        var cleaned = Regex.Replace(version.Trim(), @"^[^\d]*", "");
        
        // Remove build metadata (+something) but keep prerelease info (-something)
        var plusIndex = cleaned.IndexOf('+');
        if (plusIndex >= 0)
        {
            cleaned = cleaned.Substring(0, plusIndex);
        }

        // Ensure we have at least a basic version format
        if (!Regex.IsMatch(cleaned, @"^\d+(\.\d+)*"))
        {
            return "0.0.0";
        }

        return cleaned;
    }

    private static bool TryParseAsSystemVersion(string versionString, out Version version)
    {
        version = new Version();
        
        try
        {
            // Remove prerelease suffix for System.Version parsing
            var versionPart = versionString.Split('-')[0];
            
            // Ensure we have at least major.minor format for System.Version
            var parts = versionPart.Split('.');
            if (parts.Length == 1)
            {
                versionPart += ".0";
            }
            
            version = new Version(versionPart);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static int CompareSemanticVersions(string version1, string version2)
    {
        var v1Parts = ParseSemanticVersion(version1);
        var v2Parts = ParseSemanticVersion(version2);

        // Compare major, minor, patch
        for (int i = 0; i < 3; i++)
        {
            var comparison = v1Parts.NumericParts[i].CompareTo(v2Parts.NumericParts[i]);
            if (comparison != 0)
                return comparison;
        }

        // Handle prerelease versions (1.0.0-alpha < 1.0.0)
        if (string.IsNullOrEmpty(v1Parts.Prerelease) && !string.IsNullOrEmpty(v2Parts.Prerelease))
            return 1; // 1.0.0 > 1.0.0-alpha
        
        if (!string.IsNullOrEmpty(v1Parts.Prerelease) && string.IsNullOrEmpty(v2Parts.Prerelease))
            return -1; // 1.0.0-alpha < 1.0.0

        if (!string.IsNullOrEmpty(v1Parts.Prerelease) && !string.IsNullOrEmpty(v2Parts.Prerelease))
            return string.Compare(v1Parts.Prerelease, v2Parts.Prerelease, StringComparison.OrdinalIgnoreCase);

        return 0;
    }

    private static (int[] NumericParts, string Prerelease) ParseSemanticVersion(string version)
    {
        var parts = version.Split('-');
        var versionPart = parts[0];
        var prerelease = parts.Length > 1 ? parts[1] : "";

        var numericParts = versionPart.Split('.')
            .Take(3)
            .Select(p => int.TryParse(p, out var num) ? num : 0)
            .ToArray();

        // Ensure we have exactly 3 parts (major, minor, patch)
        if (numericParts.Length < 3)
        {
            var fullParts = new int[3];
            Array.Copy(numericParts, fullParts, numericParts.Length);
            numericParts = fullParts;
        }

        return (numericParts, prerelease);
    }
}