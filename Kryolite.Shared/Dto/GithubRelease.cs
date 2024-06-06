namespace Kryolite.Shared;

// only partially implemented
// https://api.github.com/repos/kryolite-crypto/kryolite/releases/latest
public partial class GithubRelease
{
    public string tag_name { get; set; } = string.Empty;
}
