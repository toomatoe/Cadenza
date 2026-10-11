using System.Net;
using System.Text;
using Cadenza.Core.Security;
using Cadenza.Core.Services.Spotify;
using Cadenza.Tests.Infrastructure;
using static Cadenza.Tests.Fixtures.TestData;

namespace Cadenza.Tests.Suites;

internal static class PkceChecks
{
    public static void Run(CheckRunner checks)
    {
        checks.Check(Pkce.Challenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk") == "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", "RFC 7636 PKCE vector");
        checks.Check(Pkce.RandomValue().Length == 43, "PKCE verifier length");
        checks.Check(SpotifyAuth.ValidateRedirectUri("http://127.0.0.1:9000/signin").Port == 9000, "Custom redirect port and path");
        foreach (var redirect in new[] { "http://localhost:8888/callback", "https://example.com/callback", "http://127.0.0.1/callback?x=1" })
        {
            checks.Throws<ArgumentException>(() => SpotifyAuth.ValidateRedirectUri(redirect), "Invalid redirect accepted");
        }
        checks.Check(Pkce.ReadCode(new Uri("http://127.0.0.1:8888/callback?state=abc&code=hello"), "abc") == "hello", "OAuth code parsing");
        foreach (var uri in new[] { "?state=wrong&code=x", "?state=abc&error=access_denied", "?state=abc&code=x&code=y", "?code=x" })
        {
            checks.Throws<InvalidOperationException>(() => Pkce.ReadCode(new Uri("http://127.0.0.1:8888/callback" + uri), "abc"), "Invalid OAuth callback accepted");
        }
    }
}
