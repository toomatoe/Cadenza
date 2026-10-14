using System.Text.Json;
using Cadenza.Core.Social;
using Cadenza.Server.Exceptions;
using Cadenza.Server.Extensions;
using Cadenza.Server.Interfaces;
using Cadenza.Server.Models;
using Microsoft.AspNetCore.Http.Features;

namespace Cadenza.Server.Endpoints;

/// <summary>The shared library: anyone with an account can search, play and add songs.</summary>
public static class MusicEndpoints
{
    // Multipart boundaries and the metadata field on top of the file itself.
    private const long UploadOverheadBytes = 1024 * 1024;
    private const string FileField = "file";
    private const string MetadataField = "metadata";
    private const string ExpectedUpload = "Expected a music file upload.";

    /// <summary>Maps <c>/music</c> on the authenticated API group.</summary>
    public static RouteGroupBuilder MapMusicEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/music", (string? q, ISocialStore store) => store.SearchMusic(q, SocialRules.PageSize));
        api.MapGet("/music/{id}/file", GetFile);
        api.MapPost("/music", UploadAsync).RequireRateLimiting(RateLimitPolicies.Upload);
        api.MapDelete("/music/{id}", Delete);

        return api;
    }

    private static IResult GetFile(string id, ISocialStore store, IMusicLibrary library)
    {
        var track = store.FindMusic(id) ?? throw new SocialRuleException(StatusCodes.Status404NotFound, "That song is no longer in the library.");

        return Results.File(library.PathFor(track.Id, track.Format), MusicFormats.ContentType(track.Format), enableRangeProcessing: true);
    }

    private static async Task<LibraryTrack> UploadAsync(HttpContext context, IMusicLibrary library, TimeProvider clock)
    {
        // The form is read here rather than bound, so the size limit can be raised for this request only.
        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
            limit.MaxRequestBodySize = MusicFormats.MaxUploadBytes + UploadOverheadBytes;

        if (!context.Request.HasFormContentType)
            throw new SocialRuleException(ExpectedUpload);

        var form = await context.Request.ReadFormAsync(context.RequestAborted);
        var file = form.Files.GetFile(FileField) ?? throw new SocialRuleException(ExpectedUpload);
        var metadata = ReadMetadata(form[MetadataField].ToString()) ?? throw new SocialRuleException("Song details are missing.");

        return await library.SaveAsync(context.GetUser(), file, metadata, clock.GetUtcNow(), context.RequestAborted);
    }

    private static IResult Delete(HttpContext context, string id, IMusicLibrary library)
    {
        if (!library.Delete(context.GetUser().Id, id))
            throw new SocialRuleException(StatusCodes.Status403Forbidden, "Only the person who added a song can remove it.");

        return Results.NoContent();
    }

    private static UploadMetadata? ReadMetadata(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<UploadMetadata>(json, SocialRules.Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
