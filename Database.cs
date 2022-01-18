using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Net.Http.Headers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using DSharpPlus.Entities;
using Google.Apis.YouTube.v3.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Logging;
using NodaTime;
using Npgsql;
using NpgsqlTypes;
using Serilog;
using Z.EntityFramework.Plus;

namespace DiscordMusicRecs;

[SuppressMessage("ReSharper", "MergeIntoPattern")]
[SuppressMessage("ReSharper", "ReplaceWithStringIsNullOrEmpty")]
internal class Database
{
    // Obtain connection string information from the portal
    //
    private static readonly string Host = Program.Config.PostgresConfig.Host; //IP or Url of Postgres Server
    private static readonly string User = Program.Config.PostgresConfig.User; //Username for use on postgres database, probably postgres
    private static readonly string Port = Program.Config.PostgresConfig.Port; //TCP port that Postgres server is listening on, probably 5432
    private static readonly string Password = Program.Config.PostgresConfig.Password; //Password use when setting up postgres, not root password of server
    private static readonly string DBname = Program.Config.PostgresConfig.DbName; //Name of the database used
    public static readonly string MainTableName = Program.Config.PostgresConfig.MainTableName; //Name of main table, for server channel watches
    public static readonly string LogTableName = Program.Config.PostgresConfig.LogTableName; //Name of main table, for server channel watches
    public static readonly string YouTubeVideoDataTableName = Program.Config.PostgresConfig.YouTubeVideoDataTableName; //Name of main table, for server channel watches

    private readonly NpgsqlConnection Connection = null!;

    // Build connection string using parameters from portal
    private static string BaseConnectionString => $"Server={Host};Username={User};Database={DBname};Port={Port};Password={Password};SSLMode=Prefer;Pooling=true;Command Timeout=5;Include Error Detail=true;";

    public class SubmittedVideoData
    {
        [Required]
        public string VideoId { get; set; } = null!;
        [Required]
        public string PlaylistItemId { get; set; } = null!;
        public string? WeeklyPlaylistItemId { get; set; }
        public string? MonthlyPlaylistItemId { get; set; }
        public string? YearlyPlaylistItemId { get; set; }
        public ulong? ChannelId { get; set; }
        [Required]
        public ulong UserId { get; set; }
        [Required]
        public NodaTime.ZonedDateTime TimeSubmitted { get; set; }
        [Required]
        [Key]
        public ulong MessageId { get; set; }
        [Required]
        public short Upvotes { get; set; }
        [Required]
        public short Downvotes { get; set; }
        //Relation to playlist
        public string PlaylistId { get; set; } = null!;
        public PlaylistData Playlist { get; set; } = null!;
    }

    public readonly struct SubmittedVideoDataAndContext
    {
        public readonly SubmittedVideoData SubmittedVideoData;
        public readonly DiscordDatabaseContext Context;

        public SubmittedVideoDataAndContext(ref SubmittedVideoData submittedVideoData, ref DiscordDatabaseContext context)
        {
            SubmittedVideoData = submittedVideoData;
            Context = context;
        }
    }
    public class YouTubeVideo
    {
        public string? ETag { get; set; }
        public string Id { get;set; }

        #region snippet
        public NodaTime.ZonedDateTime? PublishedAt { get; set; }
        public string? ChannelId { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? Thumbnail { get; set; }
        public string? ChannelTitle { get; set; }
        public string[]? Tags { get; set; }
        public string? CategoryId { get; set; }
        public string? LiveBroadcastContent { get; set; }
        public string? DefaultLanguage { get; set; }
        public string? LocalizedTitle { get; set; }
        public string? LocalizedDescription { get; set; }
        public string? DefaultAudioLanguage { get; set; }
        #endregion

        #region contentDetails
        public NodaTime.Duration? Duration { get; set; }
        public string? Dimension { get; set; }
        //Alternative to using Dimension since options are 2D or 3D
        public bool Is3D { get; set; } = false;
        //To Limit to official uploads only
        public bool? LicensedContent { get; set; }
        //must be used in conjunction with IsRestricted
        public string[]? RestrictedRegions { get; set; }
        //True if RestrictedRegions is a blocked list (blacklist), false if RestrictedRegions is an allowed list (whitelist) or if RestrictedRegions is null
        public bool IsRestricted { get; set; } = false;
        //TODO: Implement Content Rating Check
        //360 or rectangular
        public string? Projection { get; set; }
        #endregion

        #region status
        //TODO: Implement rest of status
        public string? UploadStatus { get; set; }
        public string? PrivacyStatus { get; set; }
        public bool? PublicStatsViewable { get; set; }
        public bool? MadeForKids { get; set; }
        #endregion

        #region statistics
        public ulong? ViewCount { get; set; }
        public ulong? LikeCount { get; set; }
        public ulong? DislikeCount { get; set; }
        public ulong? FavoriteCount { get; set; }
        public ulong? CommentCount { get; set; }
        #endregion

        #region topicDetails

        public const string WikipediaUrl = "https://en.wikipedia.org";
        public const string WikiBase = $"{WikipediaUrl}/wiki/";
        //contains only extensions on top of WikiBase
        public string[]? TopicCategories { get; set; }
        #endregion

        #region construction
        public YouTubeVideo(){}
        public YouTubeVideo(string id, string? eTag = null, DateTime? publishedAt = default, string? channelId = null,
            string? title = null, string? description = null, string? thumbnail = null, string? channelTitle = null,
            string[]? tags = null, string? categoryId = null, string? liveBroadcastContent = null,
            string? defaultLanguage = null, string? localizedTitle = null, string? localizedDescription = null,
            string? defaultAudioLanguage = null, NodaTime.Duration? duration = default, string? dimension = null,
            bool is3D = default, bool? licensedContent = default, string[]? restrictedRegions = null,
            bool isRestricted = default, string? projection = null, string? uploadStatus = null,
            string? privacyStatus = null, bool? publicStatsViewable = default, bool? madeForKids = default,
            ulong? viewCount = default, ulong? likeCount = default, ulong? dislikeCount = default,
            ulong? favoriteCount = default, ulong? commentCount = default, string[]? topicCategories = null)
        {
            ETag = eTag;
            Id = id;
            PublishedAt = publishedAt is not null ? NodaTime.ZonedDateTime.FromDateTimeOffset(publishedAt.Value).WithZone(DateTimeZone.Utc) : null;
            ChannelId = channelId;
            Title = title;
            Description = description;
            Thumbnail = thumbnail;
            ChannelTitle = channelTitle;
            Tags = tags;
            CategoryId = categoryId;
            LiveBroadcastContent = liveBroadcastContent;
            DefaultLanguage = defaultLanguage;
            LocalizedTitle = localizedTitle;
            LocalizedDescription = localizedDescription;
            DefaultAudioLanguage = defaultAudioLanguage;
            Duration = duration;
            Dimension = dimension;
            Is3D = is3D;
            LicensedContent = licensedContent;
            RestrictedRegions = restrictedRegions;
            IsRestricted = isRestricted;
            Projection = projection;
            UploadStatus = uploadStatus;
            PrivacyStatus = privacyStatus;
            PublicStatsViewable = publicStatsViewable;
            MadeForKids = madeForKids;
            ViewCount = viewCount;
            LikeCount = likeCount;
            DislikeCount = dislikeCount;
            FavoriteCount = favoriteCount;
            CommentCount = commentCount;
            TopicCategories = topicCategories;
        }
        public YouTubeVideo(string id, string? eTag = null, DateTime? publishedAt = default, string? channelId = null,
            string? title = null, string? description = null, string? thumbnail = null, string? channelTitle = null,
            string[]? tags = null, string? categoryId = null, string? liveBroadcastContent = null,
            string? defaultLanguage = null, string? localizedTitle = null, string? localizedDescription = null,
            string? defaultAudioLanguage = null, string? duration = null, string? dimension = null,
            bool is3D = default, bool? licensedContent = default, string[]? restrictedRegions = null,
            bool isRestricted = default, string? projection = null, string? uploadStatus = null,
            string? privacyStatus = null, bool? publicStatsViewable = default, bool? madeForKids = default,
            ulong? viewCount = default, ulong? likeCount = default, ulong? dislikeCount = default,
            ulong? favoriteCount = default, ulong? commentCount = default, string[]? topicCategories = null)
        {
            ETag = eTag;
            Id = id;
            PublishedAt = publishedAt is not null ? NodaTime.ZonedDateTime.FromDateTimeOffset(publishedAt.Value).WithZone(DateTimeZone.Utc) : null;
            ChannelId = channelId;
            Title = title;
            Description = description;
            Thumbnail = thumbnail;
            ChannelTitle = channelTitle;
            Tags = tags;
            CategoryId = categoryId;
            LiveBroadcastContent = liveBroadcastContent;
            DefaultLanguage = defaultLanguage;
            LocalizedTitle = localizedTitle;
            LocalizedDescription = localizedDescription;
            DefaultAudioLanguage = defaultAudioLanguage;
            Duration = duration == null ? null : NodaTime.Duration.FromTimeSpan(XmlConvert.ToTimeSpan(duration));
            Dimension = dimension;
            Is3D = is3D;
            LicensedContent = licensedContent;
            RestrictedRegions = restrictedRegions;
            IsRestricted = isRestricted;
            Projection = projection;
            UploadStatus = uploadStatus;
            PrivacyStatus = privacyStatus;
            PublicStatsViewable = publicStatsViewable;
            MadeForKids = madeForKids;
            ViewCount = viewCount;
            LikeCount = likeCount;
            DislikeCount = dislikeCount;
            FavoriteCount = favoriteCount;
            CommentCount = commentCount;
            TopicCategories = topicCategories;
        }

        public YouTubeVideo(string videoId, Video video)
        {
            ETag = video.Snippet?.ETag;
            Id = videoId;
            PublishedAt = video.Snippet?.PublishedAt is not null ? NodaTime.ZonedDateTime.FromDateTimeOffset(video.Snippet.PublishedAt.Value).WithZone(DateTimeZone.Utc) : null;
            ChannelId = video.Snippet?.ChannelId;
            Title = video.Snippet?.Title;
            Description = video.Snippet?.Description;
            Thumbnail = video.Snippet?.Thumbnails.Default__.Url;
            ChannelTitle = video.Snippet?.ChannelTitle;
            Tags = video.Snippet?.Tags.ToArray();
            CategoryId = video.Snippet?.CategoryId;
            LiveBroadcastContent = video.Snippet?.LiveBroadcastContent;
            DefaultLanguage = video.Snippet?.DefaultLanguage;
            LocalizedTitle = video.Snippet?.Localized.Title;
            LocalizedDescription = video.Snippet?.Localized.Description;
            DefaultAudioLanguage = video.Snippet?.DefaultAudioLanguage;
            Duration = video.ContentDetails?.Duration == null ? null : NodaTime.Duration.FromTimeSpan(XmlConvert.ToTimeSpan(video.ContentDetails.Duration));
            Dimension = video.ContentDetails?.Dimension;
            Is3D = Dimension?.ToLower() == "3d";
            LicensedContent = video.ContentDetails?.LicensedContent;
            RestrictedRegions = video.ContentDetails?.RegionRestriction.Allowed is { Count: > 0 } ? video.ContentDetails?.RegionRestriction.Allowed.ToArray() : video.ContentDetails?.RegionRestriction.Blocked.ToArray();
            IsRestricted = video.ContentDetails?.RegionRestriction.Blocked is { Count: > 0 };
            Projection = video.ContentDetails?.Projection;
            UploadStatus = video.Status?.UploadStatus;
            PrivacyStatus = video.Status?.PrivacyStatus;
            PublicStatsViewable = video.Status?.PublicStatsViewable;
            MadeForKids = video.Status?.MadeForKids;
            ViewCount = video.Statistics?.ViewCount;
            LikeCount = video.Statistics?.LikeCount;
            DislikeCount = video.Statistics?.DislikeCount;
            FavoriteCount = video.Statistics?.FavoriteCount;
            CommentCount = video.Statistics?.CommentCount;
            TopicCategories = video.TopicDetails?.TopicCategories.Select(categoryUrl => categoryUrl.Replace(WikiBase, "")).ToArray();
        }
        #endregion
        
    }
    public class PlaylistData
    {
        public ulong ServerId { get; set; }
        public ulong? ChannelId { get; set; }
        public bool IsConnectedToChannel { get; set; }
        public string WeeklyPlaylistID { get; set; } = null!;
        public NodaTime.ZonedDateTime WeeklyTimeCreated { get; set; }
        public string MonthlyPlaylistID { get; set; } = null!;
        public NodaTime.ZonedDateTime MonthlyTimeCreated { get; set; }
        public string YearlyPlaylistID { get; set; } = null!;
        public NodaTime.ZonedDateTime YearlyTimeCreated { get; set; }
        public NodaTime.ZonedDateTime TimeCreated { get; set; }
        [Required]
        [Key]
        public string PlaylistId { get; set; } = null!;
        //Relation
        public List<SubmittedVideoData> Videos { get; set; } = null!;
    }

    public class BannedVideoData
    {
        public string VideoId { get; set; }
        public string? PlaylistId { get; set; }
        public ulong ServerId { get; set; }
        public ulong UserId { get; set; }
        public bool BannedFromServer { get; set; }
        public int Id { get; set; }
    }
    public class BannedUserData
    {
        public ulong UserId { get; set; }
        public string? PlaylistId { get; set; }
        public ulong ServerId { get; set; }
        public bool BannedFromServer { get; set; }
        public int Id { get; set; }
    }
    public readonly struct PlaylistDatasAndContext
    {
        public readonly IEnumerable<PlaylistData> PlaylistDatas;
        public readonly DiscordDatabaseContext Context;

        public PlaylistDatasAndContext(ref List<PlaylistData> playlistDatas, ref DiscordDatabaseContext context)
        {
            PlaylistDatas = playlistDatas;
            Context = context;
        }
    }
    public class DiscordDatabaseContext : DbContext
    {
        //TODO: Enable AutoHistory https://github.com/Arch/AutoHistory
        //TODO: Consider NeinLinq https://nein.tech/nein-linq/
        //TODO:
        public DbSet<SubmittedVideoData> VideosSubmitted { get; set; } = null!;
        public DbSet<PlaylistData> PlaylistsAdded { get; set; } = null!;
        public DbSet<BannedUserData> BannedUsers { get; set; } = null!;
        public DbSet<BannedVideoData> BannedVideos { get; set; } = null!;
        public DbSet<YouTubeVideo> YouTubeVideoData { get; set; } = null!;

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder
                .UseNpgsql(
                    BaseConnectionString,
                    options => options
                        .UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)
                        .UseAdminDatabase("postgres")
                        .EnableRetryOnFailure(5)
                        .UseNodaTime())
                .UseSnakeCaseNamingConvention()
                .LogTo(Log.Verbose, LogLevel.Information)
                .EnableSensitiveDataLogging();
        #region Required
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(PlaylistDataEntityTypeConfiguration).Assembly);
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(SubmittedVideoDataEntityTypeConfiguration).Assembly);
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(BannedUserData).Assembly);
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(BannedVideoData).Assembly);
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(YouTubeVideo).Assembly);
        }
        #endregion
    }
    public class SubmittedVideoDataEntityTypeConfiguration : IEntityTypeConfiguration<SubmittedVideoData>
    {
        public void Configure(EntityTypeBuilder<SubmittedVideoData> builder)
        {
            builder
                .Property(vd => vd.VideoId)
                .IsRequired();
            builder
                .Property(vd => vd.UserId)
                .IsRequired();
            builder
                .Property(vd => vd.TimeSubmitted)
                .IsRequired()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
            builder
                .Property(vd => vd.MessageId)
                .IsRequired();
            builder
                .HasIndex(vd => vd.MessageId)
                .IsUnique()
                .IncludeProperties(vd =>  new { timeSubmitted = vd.TimeSubmitted, upvotes = vd.Upvotes, downvotes = vd.Downvotes});
            builder
                .Property(vd => vd.Upvotes)
                .IsRequired()
                .HasDefaultValue(0);
            // builder
            // 	.Property(vd => vd.Downvotes)
            // 	.IsRequired()
            // 	.HasDefaultValue(0);
            // builder
            // 	.HasOne(video => video.PlaylistId)
            // 	.WithMany();
            //Relations
            builder.HasOne(svd => svd.Playlist)
                .WithMany(pd => pd.Videos)
                .HasForeignKey(svd => svd.PlaylistId)
                .HasPrincipalKey(pd => pd.PlaylistId);
            //TODO: Figure out this relation
            // builder.HasOne(vsd => vsd.Video)
            //     .WithMany(vsd => vsd.VideosSubmitted)
            //     .HasForeignKey(vsd => vsd.VideoId)
            //     .HasPrincipalKey(ytvd => ytvd.Id);
            builder
                .ToTable(LogTableName);
        }
    }
    public class YouTubeVideoDataEntityTypeConfiguration : IEntityTypeConfiguration<YouTubeVideo>
    {
        public void Configure(EntityTypeBuilder<YouTubeVideo> builder)
        {
            builder.Property(ytvd => ytvd.Id)
                .IsRequired();
            //TODO: Figure out this relation
            // builder.HasMany(ytvd => ytvd.VideosSubmitted)
            //     .WithOne(vsd=>vsd.Video)
            //     .OnDelete(DeleteBehavior.SetNull)
            //     .HasForeignKey(vsd=>vsd.VideoId)
            //     .HasPrincipalKey(ytvd=>ytvd.Id);
            builder.ToTable(YouTubeVideoDataTableName);
        }
    }
    public class PlaylistDataEntityTypeConfiguration : IEntityTypeConfiguration<PlaylistData>
    {
        public void Configure(EntityTypeBuilder<PlaylistData> builder)
        {
            builder
                .Property(pd => pd.ServerId)
                .IsRequired();
            builder
                .Property(pd => pd.ChannelId)
                .IsRequired();
            builder
                .HasIndex(pd => pd.ChannelId)
                .IsUnique()
                .IncludeProperties(pd => pd.PlaylistId);
            builder
                .Property(pd => pd.PlaylistId)
                .IsRequired();
            builder
                .Property(pd => pd.IsConnectedToChannel)
                .HasDefaultValue(false);
            builder
                .Property(pd => pd.TimeCreated)
                .IsRequired()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
            // builder
            // 	.HasMany<SubmittedVideoData>()
            // 	.WithOne(video => video.Playlist)
            // 	.IsRequired();
            builder
                .ToTable(MainTableName);
        }
    }
    public class BannedVideosEntityTypeConfiguration : IEntityTypeConfiguration<BannedVideoData>
    {
        public void Configure(EntityTypeBuilder<BannedVideoData> builder)
        {
            builder.Property(bv => bv.VideoId)
                .IsRequired();
            builder.Property(bv => bv.PlaylistId);
            builder.Property(bv => bv.ServerId)
                .IsRequired();
            builder.Property(bv => bv.UserId);
            builder.Property(bv => bv.BannedFromServer)
                .IsRequired()
                .HasDefaultValue(false);
            builder.Property(bv => bv.Id)
                .IsRequired()
                .UseIdentityAlwaysColumn();
        }
    }
    public class BannedUsersEntityTypeConfiguration : IEntityTypeConfiguration<BannedUserData>
    {
        public void Configure(EntityTypeBuilder<BannedUserData> builder)
        {
            builder.Property(bu => bu.PlaylistId);
            builder.Property(bu => bu.ServerId)
                .IsRequired();
            builder.Property(bu => bu.UserId)
                .IsRequired();
            builder.Property(bu => bu.BannedFromServer)
                .IsRequired()
                .HasDefaultValue(false);
            builder.Property(bu => bu.Id)
                .IsRequired()
                .UseIdentityAlwaysColumn();
        }
    }
    private Database()
    {
        // database = new DiscordDatabaseContext();
    }

    public static Database Instance { get; } = new();

    [Obsolete("Uses old database format", true)]
    private NpgsqlConnection GetConnection()
    {
        NpgsqlConnection conn;
    startConstructor:
        try
        {

            conn = new NpgsqlConnection(BaseConnectionString);
            conn.StateChange += async (sender, args) =>
            {
                if (args.CurrentState is ConnectionState.Closed or ConnectionState.Broken)
                {
                    // ReSharper disable once AccessToModifiedClosure
                    await conn.OpenAsync().ConfigureAwait(false);
                }
            };


            Console.Out.WriteLine("Opening connection");
            conn.Open();
        }
        catch (PostgresException e)
        {
            if (e.MessageText.Contains($"database \"{DBname}\" does not exist"))
            {
                string fallbackConnection =
                    $"Server={Host};Username={User};Database=postgres;Port={Port};Password={Password};SSLMode=Prefer;Pooling=true;Command Timeout=5";
                conn = new NpgsqlConnection(fallbackConnection);
                conn.Open();
                using NpgsqlCommand makeDbCommand = new()
                {
                    Connection = conn,
                    CommandText = $"CREATE DATABASE \"{DBname}\""
                };
                makeDbCommand.ExecuteNonQuery();
                conn.Dispose();
                goto startConstructor;
            }
            else
            {
                throw;
            }
        }
        return conn;
    }

    internal async Task<int> SaveDatabase()
    {
        await using DiscordDatabaseContext database = new();
        return await database.SaveChangesAsync().ConfigureAwait(false);
    }

    public async Task MakeServerTables()
    {
        await using DiscordDatabaseContext database = new();
        await database.Database.MigrateAsync().ConfigureAwait(false);
    }

    [Obsolete("Uses old database format", true)]
    public async Task<bool> MakePlaylistTable(string playlistId)
    {
        await ExecuteNonQuery(
            $"CREATE TABLE IF NOT EXISTS \"{playlistId}\" (id SERIAL PRIMARY KEY, video_id TEXT NOT NULL, playlist_item_id TEXT NOT NULL, channel_id NUMERIC(20,0), user_id NUMERIC(20,0) NOT NULL, time_submitted TIMESTAMP DEFAULT CURRENT_TIMESTAMP, message_id NUMERIC(20,0), upvotes SMALLINT DEFAULT 0, downvotes SMALLINT DEFAULT 0);").ConfigureAwait(false);
        return true;
    }
    
    public async Task<List<string>> GetAllPlaylistIds(ulong? serverId = null)
    {
        await using DiscordDatabaseContext database = new();
        return await database.PlaylistsAdded.Select(pa => pa.PlaylistId).ToListAsync().ConfigureAwait(false);
    }

    public async Task<int> AddVideoToPlaylistTable(string playlistId, string videoId, string playlistItemId,
        string weeklyPlaylistId, string monthlyPlaylistId, string yearlyPlaylistId, ulong? channelId, ulong userId,
        DateTimeOffset timeSubmitted, ulong messageId)
    {
        await using DiscordDatabaseContext database = new();
        await database.AddAsync(new SubmittedVideoData
        {
            VideoId = videoId,
            PlaylistItemId = playlistItemId,
            WeeklyPlaylistItemId = weeklyPlaylistId,
            MonthlyPlaylistItemId = monthlyPlaylistId,
            YearlyPlaylistItemId = yearlyPlaylistId,
            ChannelId = channelId,
            UserId = userId,
            TimeSubmitted = ZonedDateTime.FromDateTimeOffset(timeSubmitted).WithZone(DateTimeZone.Utc),
            MessageId = messageId,
            PlaylistId = playlistId
        }).ConfigureAwait(false);
        int nRows = await database.SaveChangesAsync().ConfigureAwait(false);
        return nRows;
    }

    //TODO: Fix filtering for specific "optional" categories or make them required
    public async Task<int> UpdateVotes(string? videoId = null, ulong? messageId = null, ulong? channelId = null, string? playlistId = null,
        short? upvotes = null, short? downvotes = null)
    {
        await using DiscordDatabaseContext database = new();
        SubmittedVideoData submittedVideoItem = await database.VideosSubmitted.Where(vs => vs.MessageId == messageId && vs.PlaylistId == playlistId).FirstAsync().ConfigureAwait(false);
        if (upvotes is not null)
            submittedVideoItem.Upvotes = upvotes.Value;
        if(downvotes is not null)
            submittedVideoItem.Downvotes = downvotes.Value;
        int nRows = await database.SaveChangesAsync().ConfigureAwait(false);
        return nRows;
        //database.VideosSubmitted.Where(vs => vs.MessageId == messageId && )
        if (messageId is null && videoId is null)
        {
            throw new ArgumentException(
                "Must specify at least one of messageId or videoId, both cannot be null but are currently");
        }

        if (playlistId is null)
        {
            throw new ArgumentNullException(nameof(playlistId));
        }
        if (channelId is null && playlistId is null)
        {
            throw new ArgumentException(
                "Must specify at least one of channelId or playlistId, both cannot be null but are currently");
        }

        if (upvotes is null && downvotes is null)
        {
            throw new ArgumentException(
                "Must specify at least one of upvotes or downvotes, both cannot be null but are currently");
        }

        if (string.IsNullOrWhiteSpace(playlistId))
        {
            throw new ArgumentNullException(nameof(playlistId));
        }

        NpgsqlCommand command = new()
        {
            Connection = Connection
        };
        await using ConfiguredAsyncDisposable _ = command.ConfigureAwait(false);
        string setString = "";
        if (upvotes is not null)
        {
            if (!string.IsNullOrWhiteSpace(setString))
            {
                setString += ", ";
            }
            setString += "upvotes=@upvotes";
            command.Parameters.AddWithValue("upvotes", NpgsqlDbType.Smallint, upvotes);
        }
        if (downvotes is not null)
        {
            if (!string.IsNullOrWhiteSpace(setString))
            {
                setString += ", ";
            }
            setString += "downvotes=@downvotes";
            command.Parameters.AddWithValue("downvotes", NpgsqlDbType.Smallint, downvotes);
        }
        string whereString = "";
        if (videoId is not null)
        {
            if (!string.IsNullOrWhiteSpace(whereString))
            {
                whereString += " AND ";
            }
            whereString += "video_id=@videoId";
            command.Parameters.AddWithValue("videoId", NpgsqlDbType.Text, videoId);
        }
        if (messageId is not null)
        {
            if (!string.IsNullOrWhiteSpace(whereString))
            {
                whereString += " AND ";
            }
            whereString += "message_id=@messageId";
            command.Parameters.AddWithValue("messageId", NpgsqlDbType.Numeric, (BigInteger)messageId);
        }
        string updateString = $"UPDATE \"{playlistId}\" SET {setString} WHERE {whereString} ;";
        command.CommandText = updateString;
        return nRows;
    }

    public async Task<List<string>> GetBannedVideos(ulong serverId, string? playlistId = null)
    {
        DiscordDatabaseContext database = new();
        if (playlistId is null)
        {
            return await database.BannedVideos.Where(bv => bv.ServerId == serverId && bv.BannedFromServer).Select(bv=>bv.VideoId).ToListAsync().ConfigureAwait(false);
        }
        else
        {
            return await database.BannedVideos.Where(bv => (bv.ServerId == serverId && bv.BannedFromServer) || (bv.PlaylistId == playlistId)).Select(bv => bv.VideoId).ToListAsync().ConfigureAwait(false);
        }
    }

    public static async Task<bool> IsVideoAllowed(ulong serverId, string playlistId, ulong userId, DiscordDatabaseContext? database = null)
    {
        database ??= new DiscordDatabaseContext();
        //Must not be a banned video for the playlist or the whole server
        return
            !(await database.BannedVideos.AnyAsync(bv=> 
                (bv.PlaylistId == playlistId) || (bv.ServerId == serverId && bv.BannedFromServer)).ConfigureAwait(false));
    }

    public static async Task<bool> IsUserAllowed(ulong serverId, string playlistId, ulong userId, DiscordDatabaseContext? database = null)
    {
        database ??= new DiscordDatabaseContext();
        //Has to not be a banned user for this server's playlist or the server as a whole
        return !(await database.BannedUsers.AnyAsync(bu =>
            bu.UserId == userId && bu.ServerId == serverId && (bu.PlaylistId == playlistId || bu.BannedFromServer)).ConfigureAwait(false));
    }
    
    public async Task<bool> CheckPlaylistChannelExists(ulong? serverId = null, ulong? channelId = null,
        string? playlistId = null)
    {
        if (serverId is null && channelId is null && playlistId is null)
        {
            throw new ArgumentException("One of serverId, channelId, or playlistId must not be null");
        }
        await using DiscordDatabaseContext database = new();
        IQueryable<PlaylistData>? whereQuery = null;
        if (serverId is not null)
        {
            whereQuery = whereQuery is null
                ? database.PlaylistsAdded.Where(vs => vs.ServerId == serverId)
                : whereQuery.Where(vs => vs.ServerId == serverId);
        }

        if (playlistId is not null)
        {
            whereQuery = whereQuery is null
                ? database.PlaylistsAdded.Where(vs => vs.PlaylistId == playlistId)
                : whereQuery.Where(vs => vs.PlaylistId == playlistId);
        }

        // ReSharper disable once InvertIf
        if (channelId is not null)
        {
            whereQuery = whereQuery is null
                ? database.PlaylistsAdded.Where(vs => vs.ChannelId == channelId)
                : whereQuery.Where(vs => vs.ChannelId == channelId);
        }

        return await whereQuery!.AnyAsync().ConfigureAwait(false);

    }

    public async Task<List<ulong?>> GetChannelsConnectedForServer(ulong serverId)
    {
        await using DiscordDatabaseContext database = new();
        return await database.PlaylistsAdded.Where(pa => pa.ServerId == serverId).Select(pa => pa.ChannelId).ToListAsync().ConfigureAwait(false);
    }

    public async Task<PlaylistData?> GetPlaylistRowData(ulong? serverId = null, ulong? channelId = null,
        string? playlistId = null)
    {
        await using DiscordDatabaseContext database = new();
        DbSet<PlaylistData> rowBase = database.PlaylistsAdded;
        IQueryable<PlaylistData>? whereQuery = null;
        if (serverId is null && channelId is null && playlistId is null)
        {
            throw new Exception("At least one of id, serverId, channelId, or playlistId");
        }
        if (channelId is not null)
        {
            whereQuery = (whereQuery is not null
                ? whereQuery.Where(pa => pa.ChannelId == channelId)
                : rowBase.Where(pa => pa.ChannelId == channelId));
        }
        if (serverId is not null)
        {
            whereQuery = (whereQuery is not null
                ? whereQuery.Where(pa => pa.ServerId == serverId)
                : rowBase.Where(pa => pa.ServerId == serverId));
        }
        if (playlistId is not null)
        {
            whereQuery = whereQuery is not null
                ? whereQuery.Where(pa => pa.PlaylistId == playlistId)
                : rowBase.Where(pa => pa.PlaylistId == playlistId);
        }

        try
        {
            Debug.Assert(whereQuery != null, nameof(whereQuery) + " != null");
            PlaylistData? rowData = await whereQuery.FirstOrDefaultAsync().ConfigureAwait(false);
            return rowData;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (Exception)
        {
            Debugger.Break();
            Console.WriteLine("I incorrectly caught a query with no elements");
            return null;
        }
    }
    public async Task<List<PlaylistData>> GetRowsDataSafe(ulong? serverId = null, ulong? channelId = null, string? playlistId = null)
    {
        if (serverId is null && channelId is null && playlistId is null)
        {
            throw new Exception("At least one of id, serverId, channelId, or playlistId");
        }

        return await GetRowsData(serverId, channelId, playlistId).ConfigureAwait(false);
    }
    internal async Task<List<PlaylistData>> GetRowsData(ulong? serverId = null, ulong? channelId = null, string? playlistId = null)
    {
        await using DiscordDatabaseContext database = new();
        DbSet<PlaylistData> rowBase = database.PlaylistsAdded;
        IQueryable<PlaylistData>? whereQuery = null;
        if (channelId is not null)
        {
            whereQuery = whereQuery?
                .Where(pa => pa.ChannelId == channelId) 
                ?? rowBase.Where(pa => pa.ChannelId == channelId);
        }
        if (serverId is not null)
        {
            whereQuery = whereQuery?
                .Where(pa => pa.ServerId == serverId)
                ?? rowBase.Where(pa => pa.ServerId == serverId);
        }

        if (playlistId is not null)
        {
            whereQuery = whereQuery?
                .Where(pa => pa.PlaylistId == playlistId)
                ?? rowBase.Where(pa => pa.PlaylistId == playlistId);
        }
        //If whereQuery is null fallback to just returning all with rowBase
        List<PlaylistData> rowData = await (whereQuery ?? rowBase).ToListAsync().ConfigureAwait(false);
        return rowData;
    }

    public async Task<int> DeleteVideoSubmitted(string playlistId, ulong? messageId = null,
        ulong? userId = null, string? videoId = null)
    {
        await using DiscordDatabaseContext database = new();
        IQueryable<SubmittedVideoData> whereQuery = database.VideosSubmitted.Where(vs => vs.PlaylistId == playlistId);
        if (messageId is null && userId is null && videoId is null)
        {
            throw new Exception("At least one of messageId, userId, or videoId must not be null");
        }
        if (messageId is not null)
        {
            whereQuery = whereQuery.Where(pa => pa.MessageId == messageId);
        }
        if (userId is not null)
        {
            whereQuery = whereQuery.Where(pa => pa.UserId == userId);
        }
        if (videoId is not null)
        {
            whereQuery = whereQuery.Where(pa => pa.VideoId == videoId);
        }

        try
        {
            List<SubmittedVideoData> rowData = await whereQuery.ToListAsync().ConfigureAwait(false);
            database.RemoveRange(rowData);
            int nRows = await database.SaveChangesAsync().ConfigureAwait(false);
            return nRows;
        }
        catch (Exception e)
        {
            Debugger.Break();
            Log.Error(e.ToString());
            return 0;
        }
    }

    public async Task<int> DeletePlaylistData(PlaylistData rowData)
    {
        await using DiscordDatabaseContext database = new();
        database.Remove(rowData);
        return await database.SaveChangesAsync().ConfigureAwait(false);
    }

    public async Task<List<SubmittedVideoData>> GetRankedPlaylistItems(string playlistId)
    {
        await using DiscordDatabaseContext database = new();
        IOrderedQueryable<SubmittedVideoData> whereQuery = database.VideosSubmitted.Where(vs => vs.PlaylistId == playlistId).OrderByDescending(vs=>vs.Upvotes);
        return await whereQuery.ToListAsync().ConfigureAwait(false);
    }

    //Remove All Videos from given playlist from the videos submitted table
    public async Task<int> DeleteVideosSubmittedFromPlaylist(string playlistID)
    {
        await using DiscordDatabaseContext database = new();
        return await database.VideosSubmitted.Where(vs => vs.PlaylistId == playlistID).DeleteAsync().ConfigureAwait(false);
    }

    internal async Task<List<SubmittedVideoData>> GetPlaylistItemsCustomWhereFunc(Expression<Func<SubmittedVideoData, bool>> whereFunc)
    {
        await using DiscordDatabaseContext database = new();
        return await database.VideosSubmitted.Where(whereFunc).ToListAsync().ConfigureAwait(false);
    }
    #region PublicPopulates

    public static async Task<int> PopulateTimeBasedPlaylists()
    {
        int count = 0;
        DateTime runTime = DateTime.Now;
        count += await PopulateWeeklyPlaylist(runTime).ConfigureAwait(false);
        count += await PopulateMonthlyPlaylist(runTime).ConfigureAwait(false);
        count += await PopulateYearlyPlaylist(runTime).ConfigureAwait(false);
        return count;
    }

    public static async Task<int> PopulateWeeklyPlaylist(DateTime? passedDateTime = null, bool exactlyOneWeek = true)
    {
        DateTime runTime = (passedDateTime ?? DateTime.Now).ToUniversalTime();
        await using DiscordDatabaseContext database = new();
        DateTime startOfWeek = exactlyOneWeek ? runTime.OneWeekAgo() : runTime.StartOfWeek(StartOfWeek);
        LocalDateTime startOfWeekLocal = ZonedDateTime.FromDateTimeOffset(startOfWeek).WithZone(DateTimeZone.Utc).LocalDateTime;
        //Add if not in a Weekly Playlist already and if the video was submitted after the start of the week
        List<PlaylistData> pld = await database.PlaylistsAdded.Include(playlist => playlist.Videos.Where(
            video => (video.WeeklyPlaylistItemId == null || video.WeeklyPlaylistItemId.Length == 0) && //If not in weekly playlist and
                     startOfWeekLocal <= video.TimeSubmitted.LocalDateTime)) //startOfWeek is before TimeSubmitted
            .ToListAsync().ConfigureAwait(false);
                     // startOfWeek <= video.TimeSubmitted)).ToListAsync().ConfigureAwait(false);
        // List<PlaylistData> pld = await database.PlaylistsAdded.Select(playlist => new PlaylistData
        // {
        // 	PlaylistId = playlist.PlaylistId,
        // 	WeeklyPlaylistID = playlist.WeeklyPlaylistID,
        // 	Videos = playlist.Videos.Where(
        // 			video => (video.WeeklyPlaylistItemId == null || 
        // 			          video.WeeklyPlaylistItemId.Length == 0) &&
        // 			         startOfWeek <= video.TimeSubmitted)
        // 		.Select(video => new SubmittedVideoData
        // 		{
        // 			WeeklyPlaylistItemId = video.WeeklyPlaylistItemId,
        // 			VideoId = video.VideoId
        // 		}).ToList()
        // }).ToListAsync().ConfigureAwait(false);
        int count = 0;
        int nRows = 0;
        foreach (PlaylistData playlistData in pld)
        {
            if (string.IsNullOrEmpty(playlistData.WeeklyPlaylistID))
            {
                DiscordGuildPreview? server = await Program.Discord.GetGuildPreviewAsync(playlistData.ServerId).ConfigureAwait(false);
                string playlistTitle = server?.Name is not null ? $"{server.Name}'s {YoutubeAPIs.defaultPlaylistName}" : YoutubeAPIs.defaultPlaylistName;
                string playlistDescription = server?.Name is not null ? $"{YoutubeAPIs.defaultPlaylistDescription} for {server.Name} server" : YoutubeAPIs.defaultPlaylistDescription;
                playlistData.WeeklyPlaylistID = await YoutubeAPIs.Instance.MakeWeeklyPlaylist(playlistTitle, playlistDescription).ConfigureAwait(false);
                playlistData.WeeklyTimeCreated = SystemClock.Instance.GetCurrentInstant().InUtc();
            }
            foreach (SubmittedVideoData videoData in playlistData.Videos)
            {
                //TODO: Allow Making new playlist on error and handle that
                PlaylistItem playlistItem = await YoutubeAPIs.Instance.AddToPlaylist(videoData.VideoId, playlistId: playlistData.WeeklyPlaylistID, makeNewPlaylistOnError: false).ConfigureAwait(false);
                videoData.WeeklyPlaylistItemId = playlistItem.Id;
                ++count;
            }
        }
        nRows += await database.SaveChangesAsync().ConfigureAwait(false);
        Log.Verbose($"Count: {count}, Rows Updated: {nRows}");
        return count;
    }

    public static async Task<int> PopulateMonthlyPlaylist(DateTime? passedDateTime = null)
    {
        DateTime runTime = (passedDateTime ?? DateTime.Now).ToUniversalTime();
        await using DiscordDatabaseContext database = new();
        DateTime startOfMonth = runTime.StartOfMonth();
        //Add if not in a Monthly Playlist already and if the video was submitted after the start of the week
        List<PlaylistData> pld = await database.PlaylistsAdded.Include(playlist => playlist.Videos.Where(
            video => (video.MonthlyPlaylistItemId == null || video.MonthlyPlaylistItemId.Length == 0) && // Not Currently in monthly playlist and
                     video.TimeSubmitted.Month == runTime.Month && video.TimeSubmitted.Year == runTime.Year)) //Is in the current month
            .ToListAsync().ConfigureAwait(false);
        // List<PlaylistData> pld = await database.PlaylistsAdded.Select(playlist => new PlaylistData
        // {
        // 	PlaylistId = playlist.PlaylistId,
        // 	MonthlyPlaylistID = playlist.MonthlyPlaylistID,
        // 	Videos = playlist.Videos.Where(
        // 			video => (video.MonthlyPlaylistItemId == null || 
        // 			          video.MonthlyPlaylistItemId.Length == 0) &&
        // 			         startOfMonth <= video.TimeSubmitted)
        // 		.Select(video => new SubmittedVideoData
        // 		{
        // 			MonthlyPlaylistItemId = video.MonthlyPlaylistItemId,
        // 			VideoId = video.VideoId
        // 		}).ToList()
        // }).ToListAsync().ConfigureAwait(false);
        int count = 0;
        int nRows = 0;
        foreach (PlaylistData playlistData in pld)
        {
            if (string.IsNullOrEmpty(playlistData.MonthlyPlaylistID))
            {
                DiscordGuildPreview? server = await Program.Discord.GetGuildPreviewAsync(playlistData.ServerId).ConfigureAwait(false);
                string playlistTitle = server?.Name is not null ? $"{server.Name}'s {YoutubeAPIs.defaultPlaylistName}" : YoutubeAPIs.defaultPlaylistName;
                string playlistDescription = server?.Name is not null ? $"{YoutubeAPIs.defaultPlaylistDescription} for {server.Name} server" : YoutubeAPIs.defaultPlaylistDescription;
                playlistData.MonthlyPlaylistID = await YoutubeAPIs.Instance.MakeMonthlyPlaylist(playlistTitle, playlistDescription).ConfigureAwait(false);
                playlistData.MonthlyTimeCreated = SystemClock.Instance.GetCurrentInstant().InUtc();
            }
            foreach (SubmittedVideoData videoData in playlistData.Videos)
            {
                //TODO: Allow Making new playlist on error and handle that
                PlaylistItem playlistItem = await YoutubeAPIs.Instance.AddToPlaylist(videoData.VideoId, playlistId: playlistData.MonthlyPlaylistID, makeNewPlaylistOnError: false).ConfigureAwait(false);
                videoData.MonthlyPlaylistItemId = playlistItem.Id;
                ++count;
            }
        }
        nRows += await database.SaveChangesAsync().ConfigureAwait(false);
        Log.Verbose($"Count: {count}, Rows Updated: {nRows}");
        return count;
    }

    public static async Task<int> PopulateYearlyPlaylist(DateTime? passedDateTime = null)
    {
        DateTime runTime = (passedDateTime ?? DateTime.Now).ToUniversalTime();
        await using DiscordDatabaseContext database = new();
        DateTime startOfYear = runTime.StartOfYear();
        //Add if not in a Yearly Playlist already and if the video was submitted after the start of the week
        List<PlaylistData> pld = await database.PlaylistsAdded.Include(playlist => playlist.Videos.Where(
            video => (video.YearlyPlaylistItemId == null || video.YearlyPlaylistItemId.Length == 0) && // Not Currently in yearly playlist and
                     video.TimeSubmitted.Year == runTime.Year)) //Is in the current year
            .ToListAsync().ConfigureAwait(false);
        // List<PlaylistData> pld = await database.PlaylistsAdded.Select(playlist => new PlaylistData
        // {
        // 	PlaylistId = playlist.PlaylistId,
        // 	YearlyPlaylistID = playlist.YearlyPlaylistID,
        // 	Videos = playlist.Videos.Where(
        // 			video => (video.YearlyPlaylistItemId == null || 
        // 			          video.YearlyPlaylistItemId.Length == 0) &&
        // 			         startOfYear <= video.TimeSubmitted)
        // 		.Select(video => new SubmittedVideoData
        // 		{
        // 			YearlyPlaylistItemId = video.YearlyPlaylistItemId,
        // 			VideoId = video.VideoId
        // 		}).ToList()
        // }).ToListAsync().ConfigureAwait(false);
        int count = 0;
        int nRows = 0;
        foreach (PlaylistData playlistData in pld)
        {
            if (string.IsNullOrEmpty(playlistData.YearlyPlaylistID))
            {
                DiscordGuildPreview? server = await Program.Discord.GetGuildPreviewAsync(playlistData.ServerId).ConfigureAwait(false);
                string playlistTitle = server?.Name is not null ? $"{server.Name}'s {YoutubeAPIs.defaultPlaylistName}" : YoutubeAPIs.defaultPlaylistName;
                string playlistDescription = server?.Name is not null ? $"{YoutubeAPIs.defaultPlaylistDescription} for {server.Name} server" : YoutubeAPIs.defaultPlaylistDescription;
                playlistData.YearlyPlaylistID = await YoutubeAPIs.Instance.MakeYearlyPlaylist(playlistTitle, playlistDescription).ConfigureAwait(false);
                playlistData.YearlyTimeCreated = SystemClock.Instance.GetCurrentInstant().InUtc();
            }
            foreach (SubmittedVideoData videoData in playlistData.Videos)
            {
                //TODO: Allow Making new playlist on error and handle that
                PlaylistItem playlistItem = await YoutubeAPIs.Instance.AddToPlaylist(videoData.VideoId, playlistId: playlistData.YearlyPlaylistID, makeNewPlaylistOnError: false).ConfigureAwait(false);
                videoData.YearlyPlaylistItemId = playlistItem.Id;
                ++count;
            }
        }
        nRows += await database.SaveChangesAsync().ConfigureAwait(false);
        Log.Verbose($"Count: {count}, Rows Updated: {nRows}");
        return count;
    }

    #endregion
    #region TimeBasedFuncs
    //Weekly Add
    private static Expression<Func<SubmittedVideoData, bool>> WeeklySubmissionsToAddFunc(DateTime? passedDateTime = null, bool use7days = true)
    {
        DateTime dt = (passedDateTime ?? DateTime.Now).ToUniversalTime();
        DateTime startOfWeek = use7days ? dt.OneWeekAgo() : dt.StartOfWeek(StartOfWeek);
        LocalDateTime startOfWeekLocal = LocalDateTime.FromDateTime(startOfWeek);
        //Add if not in a Weekly Playlist already and if the video was submitted after the start of the week
        return video => (video.WeeklyPlaylistItemId == null || video.WeeklyPlaylistItemId.Length == 0) && startOfWeekLocal <= video.TimeSubmitted.LocalDateTime;
    }
    public const DayOfWeek StartOfWeek = DayOfWeek.Sunday;

    //Monthly Add
    private static Expression<Func<SubmittedVideoData, bool>> MonthlySubmissionsToAddFunc(DateTime? passedDateTime = null)
    {
        DateTime dt = (passedDateTime ?? DateTime.Now).ToUniversalTime();
        //Add if not in a Monthly Playlist already and if the video was submitted after the start of the Month
        return video => (video.MonthlyPlaylistItemId == null || video.MonthlyPlaylistItemId.Length == 0) && dt.Month == video.TimeSubmitted.Month && dt.Year == video.TimeSubmitted.Year;
    }

    //Yearly Add
    private static Expression<Func<SubmittedVideoData, bool>> YearlySubmissionsToAddFunc(DateTime? passedDateTime = null)
    {
        DateTime dt = (passedDateTime ?? DateTime.Now).ToUniversalTime();
        //Add if not in a Yearly Playlist already and if the video was submitted after the start of the Year
        return video => (video.YearlyPlaylistItemId == null || video.YearlyPlaylistItemId.Length == 0) && dt.Year == video.TimeSubmitted.Year;
    }

    //Weekly Remove
    private static Expression<Func<SubmittedVideoData, bool>> WeeklySubmissionsToRemoveFunc(DateTime? passedDateTime = null, bool use7days = true)
    {
        DateTime dt = (passedDateTime ?? DateTime.Now).ToUniversalTime();
        DateTime startOfWeek = use7days ? dt.OneWeekAgo() : dt.StartOfWeek(StartOfWeek);
        LocalDateTime startOfWeekLocal = LocalDateTime.FromDateTime(startOfWeek);
        //Remove if in a valid Weekly Playlist already but the video was submitted before the start of the week
        return video => (video.WeeklyPlaylistItemId != null && video.WeeklyPlaylistItemId.Length != 0) && video.TimeSubmitted.LocalDateTime < startOfWeekLocal;
    }
    internal async Task<List<string?>> GetPlaylistItemsToRemoveWeekly(DateTime? passedDateTime = null)
    {
        await using DiscordDatabaseContext database = new();
        return await database.VideosSubmitted.Where(WeeklySubmissionsToRemoveFunc(passedDateTime)).Select(video => video.WeeklyPlaylistItemId).ToListAsync().ConfigureAwait(false);
    }
    internal async Task<int> NullOneWeeklyPlaylistItem(string playlistItemId)
    {
        await using DiscordDatabaseContext database = new();
        return await database.VideosSubmitted
            .Where(video => video.WeeklyPlaylistItemId == playlistItemId)
            .UpdateFromQueryAsync(video => new SubmittedVideoData { WeeklyPlaylistItemId = null }).ConfigureAwait(false);
    }

    internal async Task<int> NullAllWeeklyPlaylistItem(DateTime? passedDateTime = null)
    {
        await using DiscordDatabaseContext database = new();
        return await database.VideosSubmitted
            .Where(WeeklySubmissionsToRemoveFunc(passedDateTime))
            .UpdateFromQueryAsync(video => new SubmittedVideoData { WeeklyPlaylistItemId = null }).ConfigureAwait(false);
    }

    //Monthly Remove
    private static Expression<Func<SubmittedVideoData, bool>> MonthlySubmissionsToRemoveFunc(DateTime? passedDateTime = null)
    {
        DateTime dt = (passedDateTime ?? DateTime.Now).ToUniversalTime();
        DateTime startOfMonth = dt.StartOfMonth();
        //Remove if in a valid Monthly Playlist already but the video was submitted before the start of the Month
        return video => (video.MonthlyPlaylistItemId != null && video.MonthlyPlaylistItemId.Length != 0) && video.TimeSubmitted.Month != dt.Month || video.TimeSubmitted.Year != dt.Year;
    }

    internal async Task<List<string?>> GetPlaylistItemsToRemoveMonthly(DateTime? passedDateTime = null)
    {
        await using DiscordDatabaseContext database = new();
        return (await database.VideosSubmitted
            .Where(MonthlySubmissionsToRemoveFunc(passedDateTime))
            .Select(video => video.MonthlyPlaylistItemId).ToListAsync().ConfigureAwait(false))!;
    }

    internal async Task<int> NullOneMonthlyPlaylistItem(string playlistItemId)
    {
        await using DiscordDatabaseContext database = new();
        return await database.VideosSubmitted
            .Where(video => video.MonthlyPlaylistItemId == playlistItemId)
            .UpdateFromQueryAsync(video => new SubmittedVideoData { MonthlyPlaylistItemId = null }).ConfigureAwait(false);
    }

    internal async Task<int> NullAllMonthlyPlaylistItem(DateTime? passedDateTime = null)
    {
        await using DiscordDatabaseContext database = new();
        return await database.VideosSubmitted
            .Where(MonthlySubmissionsToRemoveFunc(passedDateTime))
            .UpdateFromQueryAsync(video => new SubmittedVideoData { MonthlyPlaylistItemId = null }).ConfigureAwait(false);
    }


    //Yearly Remove
    private static Expression<Func<SubmittedVideoData, bool>> YearlySubmissionsToRemoveFunc(DateTime? passedDateTime = null, bool use7days = false)
    {
        DateTime dt = (passedDateTime ?? DateTime.Now).ToUniversalTime();
        //Remove if in a valid Yearly Playlist already but the video was submitted before the start of the Year
        return video => (video.YearlyPlaylistItemId != null && video.YearlyPlaylistItemId.Length != 0) && video.TimeSubmitted.Year != dt.Year;
    }
    internal async Task<List<string?>> GetPlaylistItemsToRemoveYearly(DateTime? passedDateTime = null)
    {
        await using DiscordDatabaseContext database = new();
        return (await database.VideosSubmitted
            .Where(YearlySubmissionsToRemoveFunc(passedDateTime))
            .Select(video => video.YearlyPlaylistItemId).ToListAsync().ConfigureAwait(false))!;
    }
    internal async Task<int> NullOneYearlyPlaylistItem(string playlistItemId)
    {
        await using DiscordDatabaseContext database = new();
        return await database.VideosSubmitted
            .Where(video => video.YearlyPlaylistItemId == playlistItemId)
            .UpdateFromQueryAsync(video => new SubmittedVideoData { YearlyPlaylistItemId = null }).ConfigureAwait(false);
    }

    internal async Task<int> NullAllYearlyPlaylistItem(DateTime? passedDateTime = null)
    {
        await using DiscordDatabaseContext database = new();
        return await database.VideosSubmitted
            .Where(YearlySubmissionsToRemoveFunc(passedDateTime))
            .UpdateFromQueryAsync(video => new SubmittedVideoData { YearlyPlaylistItemId = null }).ConfigureAwait(false);
    }

#endregion

    public async Task<SubmittedVideoData?> GetPlaylistItem(string playlistId, ulong? messageId = null, ulong? userId = null, string? videoId = null)
    {
        //When grabbing only one item, must be more specific than just the playlist it's in
        if (messageId is null && userId is null && videoId is null)
        {
        	throw new Exception("At least one of messageId, userId, or videoId must not be null");
        }
        await using DiscordDatabaseContext database = new();
        IQueryable<SubmittedVideoData>? whereQuery = null;
        if (messageId is not null)
        {
            whereQuery = whereQuery is null ? database.VideosSubmitted.Where(vs=>vs.MessageId == messageId) : whereQuery.Where(vs => vs.MessageId == messageId);
        }
        if (userId is not null)
        {
            whereQuery = whereQuery is null ? database.VideosSubmitted.Where(vs => vs.UserId == userId) : whereQuery.Where(vs => vs.UserId == userId);
        }
        if (videoId is not null)
        {
            whereQuery = whereQuery is null ? database.VideosSubmitted.Where(vs => vs.VideoId == videoId) : whereQuery.Where(vs => vs.VideoId == videoId);
        }

        SubmittedVideoData? video = whereQuery is null
            ? await database.VideosSubmitted.AsNoTracking().FirstOrDefaultAsync().ConfigureAwait(false)
            : await whereQuery.AsNoTracking().FirstOrDefaultAsync().ConfigureAwait(false);
        return video;
    }

    public async Task<List<SubmittedVideoData>> GetPlaylistItems(string playlistId, ulong? messageId = null, ulong? userId = null, string? videoId = null)
    {
        // if (messageId is null && userId is null && videoId is null)
        // {
        // 	throw new Exception("At least one of messageId, userId, or videoId must not be null");
        // }
        Expression<Func<SubmittedVideoData, bool>>? whereExpression = vs => vs.PlaylistId == playlistId;
        IQueryable<SubmittedVideoData>? whereQuery = null;
        await using DiscordDatabaseContext database = new();
        if (messageId is not null)
        {
            whereQuery = whereQuery is null
                ? database.VideosSubmitted.Where(vs => vs.MessageId == messageId)
                : whereQuery.Where(vs => vs.MessageId == messageId);
        }
        if (userId is not null)
        {
            whereQuery = whereQuery is null
                ? database.VideosSubmitted.Where(vs => vs.UserId == userId)
                : whereQuery.Where(vs => vs.UserId == userId);
        }
        if (videoId is not null)
        {
            whereQuery = whereQuery is null
                ? database.VideosSubmitted.Where(vs => vs.VideoId == videoId)
                : whereQuery.Where(vs => vs.VideoId == videoId);
        }

        var vid = whereQuery is null
            ? await database.VideosSubmitted.ToListAsync().ConfigureAwait(false)
            : await whereQuery.ToListAsync().ConfigureAwait(false);
        return vid;
    }

    internal async Task<List<SubmittedVideoData>> GetPlaylistItemsUnsafe(DiscordDatabaseContext database, IQueryable<SubmittedVideoData>? whereQuery = null)
    {
        return whereQuery is null
            ? await database.VideosSubmitted.ToListAsync().ConfigureAwait(false)
            : await whereQuery.ToListAsync().ConfigureAwait(false);
    }

    //Security Measure for user comamnd, without verifying server via context you could theoretically delete any playlist as long as you can mention the channel
    public async Task<bool> DeleteRowWithServer(ulong serverId, ulong channelId)
    {
        await using DiscordDatabaseContext database = new();
        int nRows = await database.PlaylistsAdded.Where(pa => pa.ServerId == serverId && pa.ChannelId == channelId).DeleteAsync().ConfigureAwait(false);
        return nRows > 0;
    }
    
    //Insecure version for internal use only, should not be executable by a user. As long as channelIds are unique (and they have to be since the database table column is marked unique) this is reliable
    //Deletes all rows with given channel Id from the watch list table
    public async Task<bool> DeleteRow(string tableName, ulong channelId)
    {
        await using DiscordDatabaseContext database = new();
        return await database.PlaylistsAdded.Where(pa => pa.ChannelId == channelId).DeleteAsync().ConfigureAwait(false) > 0;
    }
    public async Task InsertRow(string tableName, ulong serverId, string playlistId, string weeklyPlaylistId, string monthlyPlaylistId, string yearlyPlaylistId, ulong? channelId = null, DateTime? timeCreated = null)
    {
        PlaylistData playlistData = new()
        {
            ChannelId = channelId, 
            IsConnectedToChannel = channelId is not null, 
            PlaylistId = playlistId,
            WeeklyPlaylistID = weeklyPlaylistId,
            MonthlyPlaylistID = monthlyPlaylistId,
            YearlyPlaylistID = yearlyPlaylistId,
            ServerId = serverId
        };
        if (timeCreated is not null)
        {
            playlistData.TimeCreated =
            playlistData.WeeklyTimeCreated = 
            playlistData.MonthlyTimeCreated =
            playlistData.YearlyTimeCreated =
                ZonedDateTime.FromDateTimeOffset(timeCreated.Value).WithZone(DateTimeZone.Utc);
        }
        await InsertRow(playlistData).ConfigureAwait(false);
    }
    public async Task InsertRow(PlaylistData playlistData)
    {
        await using DiscordDatabaseContext database = new();
        await database.PlaylistsAdded.AddAsync(playlistData).ConfigureAwait(false);
        await database.SaveChangesAsync().ConfigureAwait(false);
    }

    //TODO: Convert to Entity Framework or delete
    [Obsolete("Uses old database format", true)]
    public async Task<string> GetPlaylistId(string tableName, ulong channelId)
    {
        NpgsqlCommand command = new()
        {
            CommandText = $"SELECT playlist_id FROM {tableName} WHERE channel_id=@channelId;",
            Connection = Connection,
        };
        await using ConfiguredAsyncDisposable _ = command.ConfigureAwait(false);
        command.Parameters.AddWithValue("channelId", NpgsqlDbType.Numeric, (BigInteger)channelId);
        NpgsqlDataReader reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        await using ConfiguredAsyncDisposable __ = reader.ConfigureAwait(false);
        await reader.ReadAsync().ConfigureAwait(false);
        try
        {
            return reader.GetString("channel_id");
        }
        catch (InvalidCastException)
        {
            return "";
        }
    }

    //TODO: Convert to Entity Framework or delete
    [Obsolete("Uses old database format", true)]
    public async Task<ulong> GetChannelId(string tableName, ulong serverId)
    {
        NpgsqlDataReader reader = await ExecuteQuery($"SELECT channel_id FROM {tableName} WHERE server_id={serverId};").ConfigureAwait(false);
        await using ConfiguredAsyncDisposable _ = reader.ConfigureAwait(false);
        await reader.ReadAsync().ConfigureAwait(false);
        return (ulong)reader.GetDecimal(0);
    }

    //TODO: Convert to Entity Framework or delete
    [Obsolete("Uses old database format", true)]
    public async IAsyncEnumerable<ulong> GetChannelIds(string tableName, ulong serverId)
    {
        NpgsqlDataReader reader = await ExecuteQuery($"SELECT channel_id FROM {tableName};").ConfigureAwait(false);
        await using ConfiguredAsyncDisposable _ = reader.ConfigureAwait(false);
        List<ulong> channelIDs = new();
        while (await reader.ReadAsync().ConfigureAwait(false)) yield return (ulong)reader.GetDecimal(1);
    }
    
    public async Task<bool> ChangeChannelId(string tableName, ulong originalChannel, ulong newChannel)
    {
        await using DiscordDatabaseContext database = new();
        int nRows = await database.PlaylistsAdded.Where(pa => pa.ChannelId == originalChannel)
            .UpdateAsync(pa => new PlaylistData { ChannelId = newChannel }).ConfigureAwait(false);
        return nRows > 0;
        //TODO: Learn how to use update statement with Entity Framework Core (Maybe I did with Entity Framework Plus Library)
        NpgsqlCommand command =
            new()
            {
                CommandText = $"UPDATE {tableName} SET channel_id = @newChannel WHERE channel_id = @originalChannel;"
            };
        await using ConfiguredAsyncDisposable _ = command.ConfigureAwait(false);

        command.Parameters.AddWithValue("originalChannel", NpgsqlDbType.Numeric, (BigInteger)originalChannel);
        command.Parameters.AddWithValue("newChannel", NpgsqlDbType.Numeric, (BigInteger)newChannel);
        // var nRows = command.ExecuteNonQuery();
        return nRows != 0;
    }

    //TODO: Allow for handling of deleted time-based playlists
    [Obsolete("Verify that playlists are created when added, otherwise rerun addplaylist")]
    public async Task ChangePlaylistId(ulong serverId, ulong channelId, string newPlaylistId, string oldPlaylistId)
    {
        await using DiscordDatabaseContext database = new();
        int nRows = await database.PlaylistsAdded.Where(pa => pa.ServerId == serverId && pa.ChannelId == channelId && pa.PlaylistId == oldPlaylistId)
            .UpdateAsync(pa => new PlaylistData { PlaylistId = newPlaylistId }).ConfigureAwait(false);
        if (nRows == 0)
        {
            // await this.InsertRow(tableName, serverId, newPlaylistId, channelId: channelId).ConfigureAwait(false);
            nRows = -1;
        }
        else
        {
            await database.VideosSubmitted.Where(vs => vs.PlaylistId == oldPlaylistId)
                .UpdateAsync(vs => new SubmittedVideoData { PlaylistId = newPlaylistId }).ConfigureAwait(false);
        }
        Log.Verbose($"Number of rows updated={nRows}");
    }

    [Obsolete("Uses old database format", true)]
    private async Task ExecuteNonQuery(string commandString)
    {
        NpgsqlCommand command = new(commandString, Connection);
        await using ConfiguredAsyncDisposable _ = command.ConfigureAwait(false);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    [Obsolete("Uses old database format", true)]
    private async Task<NpgsqlDataReader> ExecuteQuery(string commandString)
    {
        NpgsqlCommand command = new(commandString, Connection);
        await using ConfiguredAsyncDisposable _ = command.ConfigureAwait(false);
        Console.WriteLine(commandString);
        return await command.ExecuteReaderAsync().ConfigureAwait(false);
    }

    [Obsolete("Uses old database format", true)]
    private void Other()
    {
        using NpgsqlCommand command1 =
               new("CREATE TABLE inventory(id serial PRIMARY KEY, name VARCHAR(50), quantity INTEGER);",
                   Connection);
        command1.ExecuteNonQuery();
        Log.Verbose("Finished creating table");

        using NpgsqlCommand command =
               new("INSERT INTO inventory (name, quantity) VALUES (@n1, @q1), (@n2, @q2), (@n3, @q3);",
                   Connection);
        command.Parameters.AddWithValue("n1", "banana");
        command.Parameters.AddWithValue("q1", 150);
        command.Parameters.AddWithValue("n2", "orange");
        command.Parameters.AddWithValue("q2", 154);
        command.Parameters.AddWithValue("n3", "apple");
        command.Parameters.AddWithValue("q3", 100);

        int nRows = command.ExecuteNonQuery();
        Log.Verbose(string.Format("Number of rows inserted={0}", nRows));


        Console.WriteLine("Press RETURN to exit");
        Console.ReadLine();
    }
}