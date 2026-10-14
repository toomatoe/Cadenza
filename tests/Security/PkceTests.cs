using Cadenza.Core.Services.Spotify;

namespace Cadenza.UnitTests.Security;

[TestClass]
public sealed class PkceTests
{
    private const string State = "abc";
    private const string Callback = "http://127.0.0.1:8888/callback";

    [TestMethod]
    public void ChallengeMatchesTheRfc7636Vector()
    {
        var challenge = Pkce.Challenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk");

        Assert.AreEqual("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", challenge, "RFC 7636 PKCE vector");
    }

    [TestMethod]
    public void RandomVerifierHas43Characters() =>
        Assert.HasCount(43, Pkce.RandomValue(), "PKCE verifier length");

    [TestMethod]
    public void CustomRedirectPortAndPathAreAccepted()
    {
        var redirect = SpotifyAuth.ValidateRedirectUri("http://127.0.0.1:9000/signin");

        Assert.AreEqual(9000, redirect.Port, "Custom redirect port and path");
    }

    [TestMethod]
    [DataRow("http://localhost:8888/callback")]
    [DataRow("https://example.com/callback")]
    [DataRow("http://127.0.0.1/callback?x=1")]
    public void InvalidRedirectUrisAreRejected(string redirect) =>
        Assert.Throws<ArgumentException>(() => SpotifyAuth.ValidateRedirectUri(redirect), "Invalid redirect accepted");

    [TestMethod]
    public void OAuthCodeIsReadFromTheCallback()
    {
        var code = Pkce.ReadCode(new Uri(Callback + "?state=abc&code=hello"), State);

        Assert.AreEqual("hello", code, "OAuth code parsing");
    }

    [TestMethod]
    [DataRow("?state=wrong&code=x")]
    [DataRow("?state=abc&error=access_denied")]
    [DataRow("?state=abc&code=x&code=y")]
    [DataRow("?code=x")]
    public void InvalidOAuthCallbacksAreRejected(string query) =>
        Assert.Throws<InvalidOperationException>(() => Pkce.ReadCode(new Uri(Callback + query), State), "Invalid OAuth callback accepted");
}
