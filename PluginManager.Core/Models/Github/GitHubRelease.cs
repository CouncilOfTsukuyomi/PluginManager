namespace PluginManager.Core.Models.GitHub;

public class GitHubRelease
{
    public string TagName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string HtmlUrl { get; set; } = string.Empty;
    public DateTime PublishedAt { get; set; }
    public bool Prerelease { get; set; }
    public List<GitHubAsset>? Assets { get; set; }
}