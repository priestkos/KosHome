using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Xunit;

namespace KosHome.Host.Tests;

/// <summary>KosHome must never talk to the network itself (Square Enix or anyone else); this reads KosHome.dll's metadata.</summary>
public class NoNetworkTests
{
    private static readonly string[] NetNamespaces =
        ["System.Net.Http", "System.Net.Sockets", "System.Net.WebSockets", "System.Net.Mail", "System.Net.NetworkInformation", "System.Net.Quic"];
    private static readonly string[] NetTypes = ["WebRequest", "HttpWebRequest", "WebClient", "Dns", "HttpListener"];
    private static readonly string[] SquareEnixHosts = ["square-enix", "finalfantasyxiv", "playonline", "ffxiv.com", "sqex", "mogstation"];

    private static (List<string> Types, List<string> Strings) Read()
    {
        // shortcut: scans KosHome.dll only, not the bundled Penumbra/Glamourer/Luna DLLs; extend if a new dependency ships.
        using var pe = new PEReader(File.OpenRead(typeof(AppCatalog).Assembly.Location));
        var md = pe.GetMetadataReader();
        var types = md.TypeReferences.Select(h => md.GetTypeReference(h))
                      .Select(t => $"{md.GetString(t.Namespace)}.{md.GetString(t.Name)}").ToList();
        var strings = new List<string>();
        for (var h = MetadataTokens.UserStringHandle(1); !h.IsNil; h = md.GetNextHandle(h))
            strings.Add(md.GetUserString(h));
        return (types, strings);
    }

    [Fact]
    public void ScannerSeesReferencesAndStrings()
    {
        var (types, strings) = Read();
        Assert.Contains("FFXIVClientStructs.FFXIV.Client.UI.UIModule", types); // control: Quill14's chat send is visible
        Assert.Contains(strings, s => s.Contains("discord.gg", StringComparison.Ordinal));
    }

    [Fact]
    public void ReferencesNoNetworkingApi()
    {
        var bad = Read().Types.Where(t => NetNamespaces.Any(n => t.StartsWith(n + ".", StringComparison.Ordinal))
                                       || NetTypes.Any(n => t == "System.Net." + n)).ToList();
        Assert.Empty(bad);
    }

    [Fact]
    public void NamesNoSquareEnixHost()
    {
        var bad = Read().Strings.Where(s => SquareEnixHosts.Any(h => s.Contains(h, StringComparison.OrdinalIgnoreCase))).ToList();
        Assert.Empty(bad);
    }
}
