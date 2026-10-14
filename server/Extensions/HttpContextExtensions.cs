using Cadenza.Core.Models.Social;

namespace Cadenza.Server.Extensions;

/// <summary>Access to the signed-in user that <see cref="Endpoints.AuthenticationFilter"/> stores on the request.</summary>
public static class HttpContextExtensions
{
    private const string UserItem = "cadenza-user";

    /// <summary>The signed-in user. Only valid inside endpoints behind <see cref="Endpoints.AuthenticationFilter"/>.</summary>
    public static SocialUser GetUser(this HttpContext context) => (SocialUser)context.Items[UserItem]!;

    /// <summary>Stores the signed-in user for the rest of the request.</summary>
    public static void SetUser(this HttpContext context, SocialUser user) => context.Items[UserItem] = user;
}
