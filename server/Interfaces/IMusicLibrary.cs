using Cadenza.Core.Models.Social;

namespace Cadenza.Server.Interfaces;

/// <summary>The shared song library's files on disk.</summary>
public interface IMusicLibrary
{
    /// <summary>The folder songs are stored in, one file per song named by ID.</summary>
    string Folder { get; }

    /// <summary>Checks and stores an upload; returns the existing song if the same file was uploaded before.</summary>
    /// <exception cref="Exceptions.SocialRuleException">The file isn't supported music, is too large, or its details are invalid.</exception>
    Task<LibraryTrack> SaveAsync(SocialUser uploader, IFormFile file, UploadMetadata metadata, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>The path of a stored song's file.</summary>
    string PathFor(string id, string format);

    /// <summary>Deletes a song the user uploaded; returns false if it isn't theirs or doesn't exist.</summary>
    bool Delete(string userId, string id);
}
