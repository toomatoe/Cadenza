using System.Globalization;
using Cadenza.Core.Social;
using Microsoft.Data.Sqlite;
using Cadenza.Server.Exceptions;
using Cadenza.Server.Interfaces;
using Cadenza.Server.Models;
using Microsoft.Extensions.Options;

namespace Cadenza.Server.Services;

/// <summary>
/// Accounts, invites, friendships, blocks, messages and reports in one SQLite file. Each call opens a
/// pooled connection, so the store is safe to share between requests. Rules that protect people (only
/// friends can message, blocks hide both ways) are enforced here, not in the client.
/// </summary>
public sealed class SocialStore : ISocialStore
{
    public const int MaxActiveInvitesPerUser = 5;
    public static readonly TimeSpan InviteLifetime = TimeSpan.FromDays(7);

    private const string Schema = """
        PRAGMA journal_mode = WAL;
        CREATE TABLE IF NOT EXISTS users (
            id TEXT PRIMARY KEY,
            username TEXT NOT NULL UNIQUE,
            display_name TEXT NOT NULL,
            token_hash TEXT NOT NULL UNIQUE,
            created_at INTEGER NOT NULL);
        CREATE TABLE IF NOT EXISTS invites (
            code TEXT PRIMARY KEY,
            created_by TEXT REFERENCES users(id) ON DELETE CASCADE,
            expires_at INTEGER NOT NULL,
            used_by TEXT REFERENCES users(id) ON DELETE SET NULL);
        CREATE TABLE IF NOT EXISTS friendships (
            requester TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
            addressee TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
            accepted INTEGER NOT NULL DEFAULT 0,
            created_at INTEGER NOT NULL,
            PRIMARY KEY (requester, addressee));
        CREATE TABLE IF NOT EXISTS blocks (
            blocker TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
            blocked TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
            PRIMARY KEY (blocker, blocked));
        CREATE TABLE IF NOT EXISTS messages (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            from_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
            to_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
            kind TEXT NOT NULL,
            text TEXT NOT NULL,
            track_id TEXT,
            sent_at INTEGER NOT NULL);
        CREATE INDEX IF NOT EXISTS messages_pair ON messages (from_id, to_id, id);
        CREATE TABLE IF NOT EXISTS reports (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            reporter TEXT REFERENCES users(id) ON DELETE SET NULL,
            message_id INTEGER NOT NULL,
            from_id TEXT NOT NULL,
            text TEXT NOT NULL,
            reason TEXT NOT NULL,
            created_at INTEGER NOT NULL);
        CREATE TABLE IF NOT EXISTS music (
            id TEXT PRIMARY KEY,
            uploader TEXT REFERENCES users(id) ON DELETE SET NULL,
            uploader_name TEXT NOT NULL,
            title TEXT NOT NULL,
            artist TEXT NOT NULL,
            album TEXT NOT NULL,
            duration_ms INTEGER NOT NULL,
            format TEXT NOT NULL,
            sha256 TEXT NOT NULL UNIQUE,
            created_at INTEGER NOT NULL);
        """;

    private const string MusicColumns = "SELECT id, title, artist, album, duration_ms, format, uploader_name FROM music";

    private readonly string _connectionString;

    public SocialStore(IOptions<CadenzaServerOptions> options)
    {
        var databasePath = options.Value.Database;
        SQLitePCL.Batteries.Init();
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = true }.ToString();
        using var connection = Open();
        Execute(connection, Schema);
    }

    /// <summary>
    /// When nobody has joined yet, makes sure an invite exists for the owner: the configured code, or a
    /// new one. Returns that code, or null once accounts exist.
    /// </summary>
    public string? EnsureBootstrapInvite(string? configuredCode, DateTimeOffset now)
    {
        using var connection = Open();
        if (Scalar<long>(connection, "SELECT COUNT(*) FROM users") > 0)
            return null;

        var existing = Scalar<string?>(connection, "SELECT code FROM invites WHERE created_by IS NULL AND used_by IS NULL LIMIT 1");
        var code = string.IsNullOrWhiteSpace(configuredCode) ? existing ?? TokenHasher.NewInviteCode() : TokenHasher.NormalizeInvite(configuredCode);
        Execute(connection, "INSERT OR IGNORE INTO invites (code, created_by, expires_at) VALUES ($code, NULL, $expires)",
            ("$code", code), ("$expires", long.MaxValue));

        return code;
    }

    public AccountCreated CreateAccount(CreateAccountRequest request, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!SocialRules.IsValidUsername(request.Username))
            throw new SocialRuleException("Usernames are 3–20 lowercase letters, digits or underscores.");

        if (!SocialRules.IsValidDisplayName(request.DisplayName))
            throw new SocialRuleException($"Display names are 1–{SocialRules.MaxDisplayNameLength} characters.");

        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        var code = TokenHasher.NormalizeInvite(request.InviteCode ?? "");
        var valid = Scalar<long>(connection, "SELECT COUNT(*) FROM invites WHERE code = $code AND used_by IS NULL AND expires_at > $now",
            ("$code", code), ("$now", now.ToUnixTimeSeconds())) > 0;
        if (!valid)
            throw new SocialRuleException(StatusCodes.Status403Forbidden, "That invite code isn't valid or has been used.");

        if (Scalar<long>(connection, "SELECT COUNT(*) FROM users WHERE username = $username", ("$username", request.Username)) > 0)
            throw new SocialRuleException(StatusCodes.Status409Conflict, "That username is taken.");

        var user = new SocialUser(Guid.NewGuid().ToString("N"), request.Username, request.DisplayName.Trim());
        var token = TokenHasher.NewToken();
        Execute(connection, "INSERT INTO users (id, username, display_name, token_hash, created_at) VALUES ($id, $username, $name, $hash, $now)",
            ("$id", user.Id), ("$username", user.Username), ("$name", user.DisplayName), ("$hash", TokenHasher.Hash(token)), ("$now", now.ToUnixTimeSeconds()));
        Execute(connection, "UPDATE invites SET used_by = $id WHERE code = $code", ("$id", user.Id), ("$code", code));

        // Whoever invited you becomes your first friend.
        var inviter = Scalar<string?>(connection, "SELECT created_by FROM invites WHERE code = $code", ("$code", code));
        if (inviter is not null)
        {
            Execute(connection, "INSERT INTO friendships (requester, addressee, accepted, created_at) VALUES ($a, $b, 1, $now)",
                ("$a", inviter), ("$b", user.Id), ("$now", now.ToUnixTimeSeconds()));
        }

        transaction.Commit();

        return new AccountCreated(user, token);
    }

    public SocialUser? Authenticate(string token)
    {
        using var connection = Open();

        return Users(connection, "SELECT id, username, display_name FROM users WHERE token_hash = $hash", ("$hash", TokenHasher.Hash(token)))
            .FirstOrDefault();
    }

    public SocialUser? FindUser(string userId)
    {
        using var connection = Open();

        return Users(connection, "SELECT id, username, display_name FROM users WHERE id = $id", ("$id", userId)).FirstOrDefault();
    }

    public InviteCreated CreateInvite(string userId, DateTimeOffset now)
    {
        using var connection = Open();
        var active = Scalar<long>(connection, "SELECT COUNT(*) FROM invites WHERE created_by = $id AND used_by IS NULL AND expires_at > $now",
            ("$id", userId), ("$now", now.ToUnixTimeSeconds()));
        if (active >= MaxActiveInvitesPerUser)
            throw new SocialRuleException(StatusCodes.Status429TooManyRequests, $"You already have {MaxActiveInvitesPerUser} unused invites.");

        var invite = new InviteCreated(TokenHasher.NewInviteCode(), now + InviteLifetime);
        Execute(connection, "INSERT INTO invites (code, created_by, expires_at) VALUES ($code, $id, $expires)",
            ("$code", invite.Code), ("$id", userId), ("$expires", invite.ExpiresAt.ToUnixTimeSeconds()));

        return invite;
    }

    public FriendList GetFriends(string userId)
    {
        using var connection = Open();
        const string Columns = "SELECT u.id, u.username, u.display_name FROM users u JOIN friendships f ON ";

        return new FriendList(
            Users(connection, Columns + "((f.requester = $me AND f.addressee = u.id) OR (f.addressee = $me AND f.requester = u.id)) WHERE f.accepted = 1 ORDER BY u.display_name", ("$me", userId)),
            Users(connection, Columns + "f.requester = u.id WHERE f.addressee = $me AND f.accepted = 0 ORDER BY f.created_at", ("$me", userId)),
            Users(connection, Columns + "f.addressee = u.id WHERE f.requester = $me AND f.accepted = 0 ORDER BY f.created_at", ("$me", userId)));
    }

    /// <summary>
    /// Sends a friend request, or accepts one if the other person already asked. Someone who blocked
    /// you looks the same as a username that doesn't exist.
    /// </summary>
    /// <returns>The other person, and whether you are now friends.</returns>
    public (SocialUser Other, bool Accepted) RequestFriend(string userId, string username, DateTimeOffset now)
    {
        using var connection = Open();
        var other = Users(connection, "SELECT id, username, display_name FROM users WHERE username = $username", ("$username", (username ?? "").Trim().ToLowerInvariant()))
            .FirstOrDefault();
        if (other is null || IsBlocked(connection, other.Id, userId))
            throw new SocialRuleException(StatusCodes.Status404NotFound, "No one has that username.");

        if (other.Id == userId)
            throw new SocialRuleException("That's you.");

        if (IsBlocked(connection, userId, other.Id))
            throw new SocialRuleException(StatusCodes.Status403Forbidden, "Unblock them first.");

        if (Scalar<long>(connection, "SELECT COUNT(*) FROM friendships WHERE requester = $other AND addressee = $me", ("$other", other.Id), ("$me", userId)) > 0)
        {
            Execute(connection, "UPDATE friendships SET accepted = 1 WHERE requester = $other AND addressee = $me", ("$other", other.Id), ("$me", userId));
            return (other, true);
        }

        Execute(connection, "INSERT OR IGNORE INTO friendships (requester, addressee, accepted, created_at) VALUES ($me, $other, 0, $now)",
            ("$me", userId), ("$other", other.Id), ("$now", now.ToUnixTimeSeconds()));

        return (other, AreFriends(connection, userId, other.Id));
    }

    public SocialUser Accept(string userId, string otherId)
    {
        using var connection = Open();
        var changed = Execute(connection, "UPDATE friendships SET accepted = 1 WHERE requester = $other AND addressee = $me AND accepted = 0",
            ("$other", otherId), ("$me", userId));
        if (changed == 0)
            throw new SocialRuleException(StatusCodes.Status404NotFound, "That request no longer exists.");

        return FindUserOrThrow(connection, otherId);
    }

    /// <summary>Unfriends, declines or cancels, whichever applies.</summary>
    public void Remove(string userId, string otherId)
    {
        using var connection = Open();
        RemoveFriendship(connection, userId, otherId);
    }

    public void Block(string userId, string otherId)
    {
        using var connection = Open();
        FindUserOrThrow(connection, otherId);
        RemoveFriendship(connection, userId, otherId);
        Execute(connection, "INSERT OR IGNORE INTO blocks (blocker, blocked) VALUES ($me, $other)", ("$me", userId), ("$other", otherId));
    }

    public void Unblock(string userId, string otherId)
    {
        using var connection = Open();
        Execute(connection, "DELETE FROM blocks WHERE blocker = $me AND blocked = $other", ("$me", userId), ("$other", otherId));
    }

    public ChatMessage SendMessage(string userId, string toId, SendMessageRequest request, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (SocialRules.ValidateMessage(request) is { } problem)
            throw new SocialRuleException(problem);

        using var connection = Open();
        if (!AreFriends(connection, userId, toId) || IsBlocked(connection, toId, userId) || IsBlocked(connection, userId, toId))
            throw new SocialRuleException(StatusCodes.Status403Forbidden, "You can only message friends.");

        var text = (request.Text ?? "").Trim();
        var id = Scalar<long>(connection,
            "INSERT INTO messages (from_id, to_id, kind, text, track_id, sent_at) VALUES ($from, $to, $kind, $text, $track, $now) RETURNING id",
            ("$from", userId), ("$to", toId), ("$kind", request.Kind.ToString()), ("$text", text), ("$track", request.TrackId), ("$now", now.ToUnixTimeMilliseconds()));

        return new ChatMessage(id, userId, toId, request.Kind, text, request.TrackId, DateTimeOffset.FromUnixTimeMilliseconds(now.ToUnixTimeMilliseconds()));
    }

    /// <summary>The latest messages with a friend, oldest first; pass <paramref name="beforeId"/> to page back.</summary>
    public IReadOnlyList<ChatMessage> Messages(string userId, string friendId, long? beforeId, int limit)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, from_id, to_id, kind, text, track_id, sent_at FROM messages
            WHERE ((from_id = $me AND to_id = $friend) OR (from_id = $friend AND to_id = $me)) AND id < $before
            ORDER BY id DESC LIMIT $limit
            """;
        Bind(command, ("$me", userId), ("$friend", friendId), ("$before", beforeId ?? long.MaxValue), ("$limit", Math.Clamp(limit, 1, SocialRules.PageSize)));
        var messages = new List<ChatMessage>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            messages.Add(new ChatMessage(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                Enum.Parse<ChatMessageKind>(reader.GetString(3)),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(6))));
        }

        messages.Reverse();

        return messages;
    }

    /// <summary>Stores a report with a copy of the message, so it survives the sender deleting their account.</summary>
    public void Report(string userId, ReportRequest request, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);
        var reason = (request.Reason ?? "").Trim();
        if (reason.Length > SocialRules.MaxReportLength)
            throw new SocialRuleException($"Reports are limited to {SocialRules.MaxReportLength} characters.");

        using var connection = Open();
        var inserted = Execute(connection, """
            INSERT INTO reports (reporter, message_id, from_id, text, reason, created_at)
            SELECT $me, id, from_id, text, $reason, $now FROM messages WHERE id = $message AND to_id = $me
            """, ("$me", userId), ("$message", request.MessageId), ("$reason", reason), ("$now", now.ToUnixTimeSeconds()));
        if (inserted == 0)
            throw new SocialRuleException(StatusCodes.Status404NotFound, "You can only report messages sent to you.");
    }

    public SocialUser UpdateProfile(string userId, UpdateProfileRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!SocialRules.IsValidDisplayName(request.DisplayName))
            throw new SocialRuleException($"Display names are 1–{SocialRules.MaxDisplayNameLength} characters.");

        using var connection = Open();
        Execute(connection, "UPDATE users SET display_name = $name WHERE id = $id", ("$name", request.DisplayName.Trim()), ("$id", userId));

        return FindUserOrThrow(connection, userId);
    }

    /// <summary>The library song with this file hash, if someone already uploaded it.</summary>
    public LibraryTrack? FindMusicByHash(string sha256)
    {
        using var connection = Open();

        return MusicRows(connection, MusicColumns + " WHERE sha256 = $hash", ("$hash", sha256)).FirstOrDefault();
    }

    public LibraryTrack? FindMusic(string id)
    {
        using var connection = Open();

        return MusicRows(connection, MusicColumns + " WHERE id = $id", ("$id", id)).FirstOrDefault();
    }

    public LibraryTrack AddMusic(SocialUser uploader, string id, UploadMetadata metadata, string format, string sha256, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(uploader);
        ArgumentNullException.ThrowIfNull(metadata);
        using var connection = Open();
        Execute(connection, """
            INSERT INTO music (id, uploader, uploader_name, title, artist, album, duration_ms, format, sha256, created_at)
            VALUES ($id, $uploader, $name, $title, $artist, $album, $duration, $format, $hash, $now)
            """,
            ("$id", id), ("$uploader", uploader.Id), ("$name", uploader.Username), ("$title", metadata.Title.Trim()),
            ("$artist", metadata.Artist.Trim()), ("$album", (metadata.Album ?? "").Trim()), ("$duration", metadata.DurationMs),
            ("$format", format), ("$hash", sha256), ("$now", now.ToUnixTimeSeconds()));

        return new LibraryTrack(id, metadata.Title.Trim(), metadata.Artist.Trim(), (metadata.Album ?? "").Trim(), metadata.DurationMs, format, uploader.Username);
    }

    /// <summary>
    /// Every word of the query must appear in the title, artist or album (case-insensitive). An empty
    /// query lists the newest uploads.
    /// </summary>
    public IReadOnlyList<LibraryTrack> SearchMusic(string? query, int limit)
    {
        var words = (query ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(8).ToArray();
        var conditions = words.Select((_, i) => $"(title || ' ' || artist || ' ' || album) LIKE $w{i} ESCAPE '\\'");
        var sql = MusicColumns + (words.Length > 0 ? " WHERE " + string.Join(" AND ", conditions) : "") + " ORDER BY created_at DESC LIMIT $limit";
        var parameters = words
            .Select((word, i) => ($"$w{i}", (object?)("%" + word.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%")))
            .Append(("$limit", Math.Clamp(limit, 1, SocialRules.PageSize)))
            .ToArray();
        using var connection = Open();

        return MusicRows(connection, sql, parameters);
    }

    /// <summary>Removes a song if <paramref name="userId"/> uploaded it; returns whether it did.</summary>
    public bool RemoveMusic(string userId, string id)
    {
        using var connection = Open();

        return Execute(connection, "DELETE FROM music WHERE id = $id AND uploader = $me", ("$id", id), ("$me", userId)) > 0;
    }

    private static List<LibraryTrack> MusicRows(SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        Bind(command, parameters);
        var rows = new List<LibraryTrack>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new LibraryTrack(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetInt32(4), reader.GetString(5), reader.GetString(6)));
        }

        return rows;
    }

    /// <summary>Deletes the account with its friendships, blocks, invites and messages (reports keep their copy, and songs stay in the shared library).</summary>
    public void DeleteAccount(string userId)
    {
        using var connection = Open();
        Execute(connection, "DELETE FROM users WHERE id = $id", ("$id", userId));
    }

    public bool AreFriends(string userId, string otherId)
    {
        using var connection = Open();

        return AreFriends(connection, userId, otherId);
    }

    private static bool AreFriends(SqliteConnection connection, string a, string b) =>
        Scalar<long>(connection, "SELECT COUNT(*) FROM friendships WHERE accepted = 1 AND ((requester = $a AND addressee = $b) OR (requester = $b AND addressee = $a))",
            ("$a", a), ("$b", b)) > 0;

    private static bool IsBlocked(SqliteConnection connection, string blocker, string blocked) =>
        Scalar<long>(connection, "SELECT COUNT(*) FROM blocks WHERE blocker = $a AND blocked = $b", ("$a", blocker), ("$b", blocked)) > 0;

    private static void RemoveFriendship(SqliteConnection connection, string a, string b) =>
        Execute(connection, "DELETE FROM friendships WHERE (requester = $a AND addressee = $b) OR (requester = $b AND addressee = $a)", ("$a", a), ("$b", b));

    private static SocialUser FindUserOrThrow(SqliteConnection connection, string userId) =>
        Users(connection, "SELECT id, username, display_name FROM users WHERE id = $id", ("$id", userId)).FirstOrDefault()
        ?? throw new SocialRuleException(StatusCodes.Status404NotFound, "That account no longer exists.");

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        Execute(connection, "PRAGMA foreign_keys = ON;");

        return connection;
    }

    private static List<SocialUser> Users(SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        Bind(command, parameters);
        var users = new List<SocialUser>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            users.Add(new SocialUser(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }

        return users;
    }

    private static int Execute(SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        Bind(command, parameters);

        return command.ExecuteNonQuery();
    }

    private static T Scalar<T>(SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        Bind(command, parameters);
        var value = command.ExecuteScalar();

        return value is null or DBNull ? default! : (T)Convert.ChangeType(value, Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T), CultureInfo.InvariantCulture);
    }

    private static void Bind(SqliteCommand command, params (string Name, object? Value)[] parameters)
    {
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }
    }
}
