using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Net.Http.Headers;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using DSharpPlus.Entities;
using Google.Apis.YouTube.v3.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Npgsql;
using NpgsqlTypes;
using Serilog;
using Z.EntityFramework.Plus;

namespace DiscordMusicRecs;

internal class Database
{
	// Obtain connection string information from the portal
	//
	private static string Host = Program.Config.PostgresConfig.Host; //IP or Url of Postgres Server
	private static string User = Program.Config.PostgresConfig.User; //Username for use on postgres database, probably postgres
    private static string Port = Program.Config.PostgresConfig.Port; //TCP port that Postgres server is listening on, probably 5432
	private static string Password = Program.Config.PostgresConfig.Password; //Password use when setting up postgres, not root password of server
	private static string DBname = Program.Config.PostgresConfig.DbName; //Name of the database used
    public static string MainTableName = Program.Config.PostgresConfig.MainTableName; //Name of main table, for server channel watches
    public static string LogTableName = Program.Config.PostgresConfig.LogTableName; //Name of main table, for server channel watches

    private readonly NpgsqlConnection Connection = null!;

    // Build connection string using parameters from portal
    private static string BaseConnectionString => $"Server={Host};Username={User};Database={DBname};Port={Port};Password={Password};SSLMode=Prefer;Pooling=true;Command Timeout=5";

	public class VideoData
	{
		[Required]
		public string VideoId { get; set; } = null!;
		[Required]
		public string PlaylistItemId { get; set; } = null!;
		public ulong? ChannelId { get; set; }
		[Required]
		public ulong UserId { get; set; }
		[Required]
		public DateTime TimeSubmitted { get; set; }
		[Required]
		[Key]
		public ulong MessageId { get; set; }
		[Required]
		public short Upvotes { get; set; }
		[Required]
		public short Downvotes { get; set; }
		//Relation
		public string PlaylistId { get; set; } = null!;
		public PlaylistData Playlist { get; set; } = null!;
	}
	public class PlaylistData
	{
		public ulong ServerId { get; set; }
		public ulong? ChannelId { get; set; }
		public bool IsConnectedToChannel { get; set; }
		public DateTime TimeCreated { get; set; }
		[Required]
		[Key]
		public string PlaylistId { get; set; } = null!;
		//Relation
		public List<VideoData> Videos { get; set; } = null!;
	}
	public class DiscordDatabaseContext : DbContext
	{
		//TODO: Enable AutoHistory https://github.com/Arch/AutoHistory
		//TODO: Consider NeinLinq https://nein.tech/nein-linq/
		//TODO:
		public DbSet<VideoData> VideosSubmitted { get; set; } = null!;
		public DbSet<PlaylistData> PlaylistsAdded { get; set; } = null!;

		protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
			optionsBuilder
				.UseNpgsql(
					BaseConnectionString,
					options => options
						.EnableRetryOnFailure()
						.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)
						.UseAdminDatabase("postgres")
						.EnableRetryOnFailure(5))
				.UseSnakeCaseNamingConvention()
				.LogTo(Log.Information);
		#region Required
		protected override void OnModelCreating(ModelBuilder modelBuilder)
		{
			modelBuilder.ApplyConfigurationsFromAssembly(typeof(PlaylistDataEntityTypeConfiguration).Assembly);
			modelBuilder.ApplyConfigurationsFromAssembly(typeof(VideoDataEntityTypeConfiguration).Assembly);
		}
		#endregion
	}
	public class VideoDataEntityTypeConfiguration : IEntityTypeConfiguration<VideoData>
	{
		public void Configure(EntityTypeBuilder<VideoData> builder)
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
				.HasDefaultValueSql("CURRENT_TIMESTAMP")
				.HasColumnType("timestamp"); ;
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
			builder
				.Property(vd => vd.Downvotes)
				.IsRequired()
				.HasDefaultValue(0);
			builder
				.ToTable(LogTableName);
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
				.HasDefaultValueSql("CURRENT_TIMESTAMP")
				.HasColumnType("timestamp");
			builder
				.ToTable(MainTableName);
		}
	}
	public readonly struct MainData
    {
        public readonly int? id;
        public readonly ulong? serverId;
        public readonly ulong? channelId;
        public readonly string? playlistId;

        public MainData(int? id, ulong? serverId, ulong? channelId, string? playlistId)
        {
            this.id = id;
            this.serverId = serverId;
            this.channelId = channelId;
            this.playlistId = playlistId;
        }
    }
    public readonly struct PlaylistEntry
    {
	    public readonly int? id;
	    public readonly string? videoId;
	    public readonly string? playlistItemId;
	    public readonly ulong? channelId;
	    public readonly ulong? userId;
	    public readonly DateTime? timeSubmitted;
	    public readonly ulong? messageId;
	    public readonly short? upvotes;
	    public readonly short? downvotes;

	    public PlaylistEntry(int? id = null, string? videoId = null, string? playlistItemId = null, ulong? channelId = null, ulong? userId = null, DateTime? timeSubmitted = null, ulong? messageId = null, short? upvotes = null, short? downvotes = null)
	    {
		    this.id = id;
		    this.videoId = videoId;
		    this.playlistItemId = playlistItemId;
		    this.channelId = channelId;
		    this.userId = userId;
		    this.timeSubmitted = timeSubmitted;
		    this.messageId = messageId;
		    this.upvotes = upvotes;
		    this.downvotes = downvotes;
	    }
    }

    private readonly DiscordDatabaseContext database;
    private Database()
	{
		database = new DiscordDatabaseContext();
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

	public async Task MakeServerTables()
	{
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
		return await database.PlaylistsAdded.Select(pa => pa.PlaylistId).ToListAsync().ConfigureAwait(false);
    }

    public async Task<int> AddVideoToPlaylistTable(string playlistId, string videoId, string playlistItemId, ulong? channelId, ulong userId, DateTimeOffset timeSubmitted, ulong messageId)
    {
	    await database.AddAsync(new VideoData
	    {
		    VideoId = videoId,
		    PlaylistItemId = playlistItemId,
		    ChannelId = channelId,
		    UserId = userId,
		    TimeSubmitted = timeSubmitted.DateTime,
		    MessageId = messageId,
		    PlaylistId = playlistId
	    }).ConfigureAwait(false);
	    var nRows = await database.SaveChangesAsync().ConfigureAwait(false);
	    return nRows;
    }

    public async Task<int> UpdateVotes(string? videoId = null, ulong? messageId = null, ulong? channelId = null, string? playlistId = null,
        short? upvotes = null, short? downvotes = null)
    {
	    var videoItem = await database.VideosSubmitted.Where(vs => vs.MessageId == messageId && vs.PlaylistId == playlistId).FirstAsync().ConfigureAwait(false);
		if (upvotes is not null)
			videoItem.Upvotes = upvotes.Value;
		if(downvotes is not null)
			videoItem.Downvotes = downvotes.Value;
		var nRows = await database.SaveChangesAsync().ConfigureAwait(false);
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

        var command = new NpgsqlCommand
        {
            Connection = Connection
        };
        await using var _ = command.ConfigureAwait(false);
        var setString = "";
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
        var whereString = "";
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
		var updateString = $"UPDATE \"{playlistId}\" SET {setString} WHERE {whereString} ;";
        command.CommandText = updateString;
        return nRows;
    }

	//TODO: Add variable selector
    public async Task<bool> CheckPlaylistChannelExists(string tableName, int? id = null, ulong? serverId = null, ulong? channelId = null, string? playlistId = null)
	{
		var videoItem = await database.PlaylistsAdded.Where(vs => vs.ChannelId == channelId).FirstOrDefaultAsync().ConfigureAwait(false);
		return videoItem != default(PlaylistData);

		if (id is null && serverId is null && channelId is null && playlistId is null)
	    {
		    throw new Exception("At least one of id, serverId, channelId, or playlistId");
	    }

	    await using NpgsqlCommand command = new()
	    {
		    Connection = Connection,
	    };
	    var whereString = "";
	    if (id is not null)
	    {
		    whereString += $"{(string.IsNullOrEmpty(whereString) ? "WHERE" : " AND")} id = @id";
		    command.Parameters.AddWithValue("id", NpgsqlDbType.Integer, id);
	    }

	    if (channelId is not null)
	    {
		    whereString += $"{(string.IsNullOrEmpty(whereString) ? "WHERE" : " AND")} channel_id = @channelId";
		    command.Parameters.AddWithValue("channelId", NpgsqlDbType.Numeric, (BigInteger)channelId);
	    }

	    if (serverId is not null)
	    {
		    whereString += $"{(string.IsNullOrEmpty(whereString) ? "WHERE" : " AND")} server_id = @serverId";
		    command.Parameters.AddWithValue("serverId", NpgsqlDbType.Numeric, (BigInteger)serverId);
	    }

	    if (playlistId is not null)
	    {
		    whereString += $"{(string.IsNullOrEmpty(whereString) ? "WHERE" : " AND")} playlist_id = @playlistId";
		    command.Parameters.AddWithValue("playlistId", NpgsqlDbType.Numeric, playlistId);
	    }

	    command.CommandText = $"SELECT channel_id FROM {tableName} {whereString};";
	    Debug.WriteLine(command.CommandText);
	    try
		{
			var data = await command.ExecuteScalarAsync().ConfigureAwait(false);
			return data is not null;
		}
	    catch (DbException)
	    {
		    return false;
	    }

    }

    public async Task<PlaylistData?> GetRowData(string tableName, ulong? serverId = null, ulong? channelId = null,
	    string? playlistId = null)
    {
	    var rowBase = database.PlaylistsAdded;
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
			var rowData = await whereQuery.FirstAsync().ConfigureAwait(false);
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
	public async Task<List<PlaylistData>> GetRowsData(string tableName, int? id = null, ulong? serverId = null, ulong? channelId = null, string? playlistId = null)
	{
		var rowBase = database.PlaylistsAdded;
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
		Debug.Assert(whereQuery != null, nameof(whereQuery) + " != null");
		var rowData = await whereQuery.ToListAsync().ConfigureAwait(false);
		return rowData;
	}

	public async Task<int> DeletePlaylistItem(string playlistId, ulong? messageId = null,
		ulong? userId = null, string? videoId = null)
	{
		var whereQuery = database.VideosSubmitted.Where(vs => vs.PlaylistId == playlistId);
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
			var rowData = await whereQuery.ToListAsync().ConfigureAwait(false);
			database.RemoveRange(rowData);
			var nRows = await database.SaveChangesAsync().ConfigureAwait(false);
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
		database.Remove(rowData);
		return await database.SaveChangesAsync().ConfigureAwait(false);
	}

	public async Task<List<VideoData>> GetRankedPlaylistItems(string playlistId)
	{
		var whereQuery = database.VideosSubmitted.Where(vs => vs.PlaylistId == playlistId).OrderByDescending(vs=>vs.Upvotes);
		return await whereQuery.ToListAsync().ConfigureAwait(false);
	}

	//Remove All Videos from given playlist from the videos submitted table
	public async Task<int> DeleteVideosSubmittedFromPlaylist(string playlistID)
	{
		return await database.VideosSubmitted.Where(vs => vs.PlaylistId == playlistID).DeleteAsync().ConfigureAwait(false);
	}
	//TODO: Add selectors for other columns
	//TODO: Replace returning null item with returning none and checking with .Any()
	public async Task<List<VideoData>> GetPlaylistItems(string playlistId, ulong? messageId = null, ulong? userId = null, string? videoId = null)
	{
		var whereQuery = database.VideosSubmitted.Where(vs => vs.PlaylistId == playlistId);
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
		
		var rowData = await whereQuery.ToListAsync().ConfigureAwait(false);
		return rowData;
	}

	public async Task<VideoData?> GetPlaylistItem(string playlistId, int? id = null, ulong? messageId = null, ulong? userId = null, string? videoId = null)
	{
		var whereQuery = database.VideosSubmitted.Where(vs => vs.PlaylistId == playlistId);
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

			var rowData = await whereQuery.FirstAsync().ConfigureAwait(false);
			return rowData;
		}
		catch (InvalidOperationException)
		{
			return null;
		}
		catch (Exception)
		{
			Debugger.Break();
			return null;
		}
	}

	//Security Measure for user comamnd, without verifying server via context you could theoretically delete any playlist as long as you can mention the channel
	public async Task<bool> DeleteRowWithServer(string tableName, ulong serverId, ulong channelId)
	{
		var nRows = await database.PlaylistsAdded.Where(pa => pa.ServerId == serverId && pa.ChannelId == channelId).DeleteAsync().ConfigureAwait(false);
		return nRows > 0;
	}
	
	//Insecure version for internal use only, should not be executable by a user. As long as channelIds are unique (and they have to be since the database table column is marked unique) this is reliable
	//Deletes all rows with given channel Id from the watch list table
	public async Task<bool> DeleteRow(string tableName, ulong channelId)
	{
		return await database.PlaylistsAdded.Where(pa => pa.ChannelId == channelId).DeleteAsync().ConfigureAwait(false) > 0;
    }
	public async Task InsertRow(string tableName, ulong serverId, string playlistId, ulong? channelId = null, DateTime? timeCreated = null)
	{
		var playlistData = new PlaylistData
		{
			ChannelId = channelId, 
			IsConnectedToChannel = channelId is not null, 
			PlaylistId = playlistId,
			ServerId = serverId
		};
		if (timeCreated is not null)
			playlistData.TimeCreated = timeCreated.Value;
		await database.PlaylistsAdded.AddAsync(playlistData).ConfigureAwait(false);
		await database.SaveChangesAsync().ConfigureAwait(false);
	}
	//TODO: Convert to Entity Framework or delete
	[Obsolete("Uses old database format", true)]
	public async Task<string> GetPlaylistId(string tableName, ulong channelId)
    {
	    var command = new NpgsqlCommand
        {
            CommandText = $"SELECT playlist_id FROM {tableName} WHERE channel_id=@channelId;",
			Connection = Connection,
        };
        await using var _ = command.ConfigureAwait(false);
        command.Parameters.AddWithValue("channelId", NpgsqlDbType.Numeric, (BigInteger)channelId);
        var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        await using var __ = reader.ConfigureAwait(false);
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
		var reader = await ExecuteQuery($"SELECT channel_id FROM {tableName} WHERE server_id={serverId};").ConfigureAwait(false);
		await using var _ = reader.ConfigureAwait(false);
		await reader.ReadAsync().ConfigureAwait(false);
		return (ulong)reader.GetDecimal(0);
	}

	//TODO: Convert to Entity Framework or delete
	[Obsolete("Uses old database format", true)]
	public async IAsyncEnumerable<ulong> GetChannelIds(string tableName, ulong serverId)
	{
		var reader = await ExecuteQuery($"SELECT channel_id FROM {tableName};").ConfigureAwait(false);
		await using var _ = reader.ConfigureAwait(false);
		var channelIDs = new List<ulong>();
		while (await reader.ReadAsync().ConfigureAwait(false)) yield return (ulong)reader.GetDecimal(1);
	}
	
	public async Task<bool> ChangeChannelId(string tableName, ulong originalChannel, ulong newChannel)
	{
		var nRows = await database.PlaylistsAdded.Where(pa => pa.ChannelId == originalChannel)
			.UpdateAsync(pa => new PlaylistData { ChannelId = newChannel }).ConfigureAwait(false);
		return nRows > 0;
		//TODO: Learn how to use update statement with Entity Framework Core (Maybe I did with Entity Framework Plus Library)
		var command =
            new NpgsqlCommand
            {
                CommandText = $"UPDATE {tableName} SET channel_id = @newChannel WHERE channel_id = @originalChannel;"
            };
		await using var _ = command.ConfigureAwait(false);

		command.Parameters.AddWithValue("originalChannel", NpgsqlDbType.Numeric, (BigInteger)originalChannel);
		command.Parameters.AddWithValue("newChannel", NpgsqlDbType.Numeric, (BigInteger)newChannel);
		// var nRows = command.ExecuteNonQuery();
        return nRows != 0;
    }
	public async Task ChangePlaylistId(string tableName, ulong serverId, ulong channelId, string newPlaylistId, string oldPlaylistId)
	{
		var nRows = await database.PlaylistsAdded.Where(pa => pa.ServerId == serverId && pa.ChannelId == channelId && pa.PlaylistId == oldPlaylistId)
			.UpdateAsync(pa => new PlaylistData { PlaylistId = newPlaylistId }).ConfigureAwait(false);
		if (nRows == 0)
		{
			await this.InsertRow(tableName, serverId, newPlaylistId, channelId: channelId).ConfigureAwait(false);
			nRows = -1;
		}
		else
		{
			await database.VideosSubmitted.Where(vs => vs.PlaylistId == oldPlaylistId)
				.UpdateAsync(vs => new VideoData { PlaylistId = newPlaylistId }).ConfigureAwait(false);
		}
		Log.Verbose($"Number of rows updated={nRows}");
	}

	[Obsolete("Uses old database format", true)]
	private async Task ExecuteNonQuery(string commandString)
	{
		var command = new NpgsqlCommand(commandString, Connection);
		await using var _ = command.ConfigureAwait(false);
		await command.ExecuteNonQueryAsync().ConfigureAwait(false);
	}

	[Obsolete("Uses old database format", true)]
	private async Task<NpgsqlDataReader> ExecuteQuery(string commandString)
	{
		var command = new NpgsqlCommand(commandString, Connection);
		await using var _ = command.ConfigureAwait(false);
		Console.WriteLine(commandString);
		return await command.ExecuteReaderAsync().ConfigureAwait(false);
	}

	[Obsolete("Uses old database format", true)]
	private void Other()
	{
		using var command1 =
		       new NpgsqlCommand("CREATE TABLE inventory(id serial PRIMARY KEY, name VARCHAR(50), quantity INTEGER);",
			       Connection);
		command1.ExecuteNonQuery();
		Log.Verbose("Finished creating table");

		using var command =
		       new NpgsqlCommand("INSERT INTO inventory (name, quantity) VALUES (@n1, @q1), (@n2, @q2), (@n3, @q3);",
			       Connection);
		command.Parameters.AddWithValue("n1", "banana");
		command.Parameters.AddWithValue("q1", 150);
		command.Parameters.AddWithValue("n2", "orange");
		command.Parameters.AddWithValue("q2", 154);
		command.Parameters.AddWithValue("n3", "apple");
		command.Parameters.AddWithValue("q3", 100);

		var nRows = command.ExecuteNonQuery();
		Log.Verbose(string.Format("Number of rows inserted={0}", nRows));


		Console.WriteLine("Press RETURN to exit");
		Console.ReadLine();
	}
}