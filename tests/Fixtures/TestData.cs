using System.Net;
using System.Text;
using Cadenza.Core.Models;

namespace Cadenza.UnitTests.Fixtures;

internal static class TestData
{
    public const string ClientId = "0123456789abcdef0123456789abcdef";

    public static Track Song(string id, string artist) =>
        new(id, id, artist, artist, "Album", 65000, "https://open.spotify.com/track/" + id, false);

    public static Track Song(int id, string artist) => Song(id.ToString(System.Globalization.CultureInfo.InvariantCulture), artist);

    public static HttpResponseMessage JsonResponse(string value) =>
        new(HttpStatusCode.OK) { Content = new StringContent(value, Encoding.UTF8, "application/json") };
}
