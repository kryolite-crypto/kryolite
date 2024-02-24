using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Kryolite.Shared;

// only partially implemented
// https://api.github.com/repos/kryolite-crypto/kryolite/releases/latest
public partial class GithubRelease
{
    public string tag_name { get; set; } = string.Empty;
}
