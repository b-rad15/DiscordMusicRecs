using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Net.Http.Headers;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using DSharpPlus.Entities;
using Google.Apis.YouTube.v3.Data;
using Npgsql;
using NpgsqlTypes;

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

	private NpgsqlConnection Connection;
	private string connectionString;

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
    private Database()
	{
		// Build connection string using parameters from portal
		//
		connectionString =
			$"Server={Host};Username={User};Database={DBname};Port={Port};Password={Password};SSLMode=Prefer;Pooling=true;Command Timeout=5";
		Connection = GetConnection();
	}

    public static Database Instance { get; } = new();

    private NpgsqlConnection GetConnection()
	{
		var conn = new NpgsqlConnection(connectionString);
		conn.StateChange += async (sender, args) =>
		{
			if (args.CurrentState is ConnectionState.Closed or ConnectionState.Broken)
			{
				await conn.OpenAsync();
            }
		};


		Console.Out.WriteLine("Opening connection");
		conn.Open();
		return conn;
	}

	public async Task MakeServerTables()
	{
		await ExecuteNonQuery($"CREATE TABLE IF NOT EXISTS {MainTableName}(id serial PRIMARY KEY,server_id NUMERIC(20,0), channel_id NUMERIC(20,0) UNIQUE,playlist_id TEXT);");
	}

    public async Task<bool> MakePlaylistTable(string playlistId)
    {
        await ExecuteNonQuery(
            $"CREATE TABLE IF NOT EXISTS \"{playlistId}\" (id SERIAL PRIMARY KEY, video_id TEXT NOT NULL, playlist_item_id TEXT NOT NULL, channel_id NUMERIC(20,0), user_id NUMERIC(20,0) NOT NULL, time_submitted TIMESTAMP DEFAULT CURRENT_TIMESTAMP, message_id NUMERIC(20,0), upvotes SMALLINT DEFAULT 0, downvotes SMALLINT DEFAULT 0);");
        return true;
    }

    public async IAsyncEnumerable<string> GetAllPlaylistIds(ulong? serverId = null)
    {
        await using var command = new NpgsqlCommand
        {
            CommandText = $"SELECT playlist_id FROM {MainTableName};",
            Connection = Connection
        };
		await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            yield return reader.GetValue("playlist_id") as string ?? throw new InvalidOperationException("Null playlist_id read from table where playlist_id should be marked as NOT NULL, ensure that playlist_id column is marked NOT NULL");
        }
    }

    public async Task<int> AddVideoToPlaylistTable(string playlistId, string videoId, string playlistItemId,
        ulong? channelId, ulong userId, DateTimeOffset timeSubmitted, ulong? messageId)
    {
	    await using var command = new NpgsqlCommand
        {
			CommandText = $"INSERT INTO \"{playlistId}\" (video_id, playlist_item_id, channel_id, user_id, time_submitted, message_id) VALUES (@videoId, @playlistItemId, @channelId, @userId, @timeSubmitted, @messageId);",
			Connection = Connection
        };
        command.Parameters.AddWithValue("videoId", NpgsqlDbType.Text, videoId);
        command.Parameters.AddWithValue("playlistItemId", NpgsqlDbType.Text, playlistId);
        command.Parameters.AddWithValue("channelId", NpgsqlDbType.Numeric, (BigInteger)channelId!);
        command.Parameters.AddWithValue("userId", NpgsqlDbType.Numeric, (BigInteger)userId);
        command.Parameters.AddWithValue("timeSubmitted", NpgsqlDbType.Timestamp, timeSubmitted.DateTime);
        command.Parameters.AddWithValue("messageId", NpgsqlDbType.Numeric, (BigInteger)messageId!);
        var nRows = await command.ExecuteNonQueryAsync();
        return nRows;
    }

    public async Task<int> UpdateVotes(string? videoId = null, ulong? messageId = null, ulong? channelId = null, string? playlistId = null,
        int? upvotes = null, int? downvotes = null)
    {
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

        await using var command = new NpgsqlCommand
        {
            Connection = Connection
        };
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
        var nRows = await command.ExecuteNonQueryAsync();
        return nRows;
    }

    public async Task<MainData?> GetRowData(string tableName, 
        int? id = null, ulong? serverId = null, ulong? channelId = null, string? playlistId = null)
    {
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

        command.CommandText = $"SELECT * FROM {tableName} {whereString};";
		Debug.WriteLine(command.CommandText);
		await using var reader = await command.ExecuteReaderAsync();
        if (!reader.HasRows)
        {
            return null;
        }
        await reader.ReadAsync();
        id = reader.GetValue(0) as int?;
        var value = reader.GetValue(1);
		ulong tmpvalue;
        serverId = value is null ? null : Convert.ToUInt64(value); //must use convert here, casting gives a null answer
        value = reader.GetValue(2);
        channelId = value is null ? null : Convert.ToUInt64(value); //must use convert here, casting gives a null answer
        playlistId = reader.GetValue(3) as string;
        return new MainData(id, serverId, channelId, playlistId);
	}
	public async IAsyncEnumerable<MainData?> GetRowsData(string tableName, int? id = null, ulong? serverId = null, ulong? channelId = null, string? playlistId = null)
	{
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

		whereString = " WHERE" + whereString;
		command.CommandText = $"SELECT * FROM {tableName}{whereString};";
		Debug.WriteLine(command.CommandText);
		await using var reader = await command.ExecuteReaderAsync();
		if (!reader.HasRows)
		{
			yield return null;
			yield break;
		}

        while (await reader.ReadAsync())
        {
            id = reader.GetValue("id") as int?;
            var value = reader.GetValue("server_id");
            // serverId = ulong.TryParse((string)value, out tmpvalue) ? tmpvalue : (ulong?)null;
            serverId = value is null ? null : Convert.ToUInt64(value); //must use convert here, casting gives a null answer
            value = reader.GetValue("channel_id");
            channelId = value is null ? null : Convert.ToUInt64(value); //must use convert here, casting gives a null answer
			playlistId = reader.GetValue("playlist_id") as string;
            yield return new MainData(id, serverId, channelId, playlistId);
		}
	}

	public async Task<int> DeletePlaylistItem(string playlistId, int? id = null, ulong? messageId = null,
		ulong? userId = null, string? videoId = null)
	{
		await using NpgsqlCommand command = new NpgsqlCommand
		{
			Connection = Connection
		};
		var whereString = "";
		if (id is not null)
		{
			whereString += $"{(string.IsNullOrEmpty(whereString) ? "WHERE" : " AND")} id = @id";
			command.Parameters.AddWithValue("id", NpgsqlDbType.Integer, id);
		}
		if (userId is not null)
		{
			whereString += $"{(string.IsNullOrEmpty(whereString) ? "WHERE" : " AND")} user_id = @userId";
			command.Parameters.AddWithValue("userId", NpgsqlDbType.Numeric, (BigInteger)userId);
		}
		if (messageId is not null)
		{
			whereString += $"{(string.IsNullOrEmpty(whereString) ? "WHERE" : " AND")} message_id = @messageId";
			command.Parameters.AddWithValue("messageId", NpgsqlDbType.Numeric, (BigInteger)messageId);
		}
		if (videoId is not null)
		{
			whereString += $"{(string.IsNullOrEmpty(whereString) ? "WHERE" : " AND")} video_id = @videoId";
			command.Parameters.AddWithValue("videoId", NpgsqlDbType.Text, videoId);
		}

		command.CommandText = $"DELETE FROM \"{playlistId}\" {whereString};";
		var nRows = await command.ExecuteNonQueryAsync();
		return nRows;
	}
	//TODO: Add selectors for other columns
	public async IAsyncEnumerable<PlaylistEntry?> GetPlaylistItems(string playlistId, int? id = null, ulong? messageId = null, ulong? userId = null, string? videoId = null)
	{
		await using NpgsqlCommand command = new NpgsqlCommand
		{
			Connection = Connection
		};
		var whereString = "";
		if (id is not null)
		{
			whereString += $"{(string.IsNullOrEmpty(whereString) ? "WHERE" : " AND")} id = @id";
			command.Parameters.AddWithValue("id", NpgsqlDbType.Integer, id);
		}
		if (userId is not null)
		{
			whereString += $"{(string.IsNullOrEmpty(whereString) ? "WHERE" : " AND")} user_id = @userId";
			command.Parameters.AddWithValue("userId", NpgsqlDbType.Numeric, (BigInteger)userId);
		}
		if (messageId is not null)
		{
			whereString += $"{(string.IsNullOrEmpty(whereString) ? "WHERE" : " AND")} message_id = @messageId";
			command.Parameters.AddWithValue("messageId", NpgsqlDbType.Numeric, (BigInteger)messageId);
		}
		if (videoId is not null)
		{
			whereString += $"{(string.IsNullOrEmpty(whereString) ? "WHERE" : " AND")} video_id = @videoId";
			command.Parameters.AddWithValue("videoId", NpgsqlDbType.Text, videoId);
		}

		command.CommandText = $"SELECT * FROM \"{playlistId}\" {whereString};";
		await using var reader = command.ExecuteReader();
		if (!reader.HasRows)
		{
			yield return null;
			yield break;
		}

		while (await reader.ReadAsync())
		{
			id = reader.GetInt32("id");
			videoId = reader.GetString("video_id");
			var playlistItemId = reader.GetString("playlist_item_id");
			var tmpValue = reader.GetValue("channel_id");
			ulong? channelId = tmpValue is null ? null : Convert.ToUInt64(tmpValue); //must use convert here, casting gives a null answer
			tmpValue = reader.GetValue("user_id");
			userId = Convert.ToUInt64(tmpValue); //must use convert here, casting gives a null answer
			var timeSubmitted = reader.GetDateTime("time_submitted");
			tmpValue = reader.GetValue("message_id");
			messageId = tmpValue is null ? null : Convert.ToUInt64(tmpValue); //must use convert here, casting gives a null answer
			var upvotes = reader.GetInt16("upvotes");
			var downvotes = reader.GetInt16("downvotes");
			yield return new PlaylistEntry(id, videoId, playlistItemId, channelId, userId, timeSubmitted, messageId, upvotes, downvotes);
		}
	}

	public async Task<PlaylistEntry?> GetPlaylistItem(string playlistId, int? id = null, ulong? messageId = null, ulong? userId = null, string? videoId = null)
	{
		await using NpgsqlCommand command = new()
		{
			Connection = Connection
		};
		var whereString = "";
		if (id is not null)
		{
			whereString += $"{(string.IsNullOrEmpty(whereString) ? "WHERE" : " AND")} id = @id";
			command.Parameters.AddWithValue("id", NpgsqlDbType.Integer, id);
		}
		if (userId is not null)
		{
			whereString += $"{(string.IsNullOrEmpty(whereString) ? "WHERE" : " AND")} user_id = @userId";
			command.Parameters.AddWithValue("userId", NpgsqlDbType.Numeric, (BigInteger)userId);
		}
		if (messageId is not null)
		{
			whereString += $"{(string.IsNullOrEmpty(whereString) ? "WHERE" : " AND")} message_id = @messageId";
			command.Parameters.AddWithValue("messageId", NpgsqlDbType.Numeric, (BigInteger)messageId);
		}
		if (videoId is not null)
		{
			whereString += $"{(string.IsNullOrEmpty(whereString) ? "WHERE" : " AND")} video_id = @videoId";
			command.Parameters.AddWithValue("videoId", NpgsqlDbType.Text, videoId);
		}

		command.CommandText = $"SELECT * FROM \"{playlistId}\" {whereString};";
		await using var reader = command.ExecuteReader(CommandBehavior.SingleRow);
		if (!reader.HasRows)
		{
			return null;
		}

		await reader.ReadAsync();
		id = reader.GetInt32("id");
		videoId = reader.GetString("video_id");
		var playlistItemId = reader.GetString("playlist_item_id");
		var tmpValue = reader.GetValue("channel_id");
		ulong? channelId = tmpValue is null ? null : Convert.ToUInt64(tmpValue); //must use convert here, casting gives a null answer
		tmpValue = reader.GetValue("user_id");
		userId = Convert.ToUInt64(tmpValue); //must use convert here, casting gives a null answer
		var timeSubmitted = reader.GetDateTime("time_submitted");
		// tmpValue = reader.GetValue("message_id");
		// messageId = Convert.ToUInt64(tmpValue); //must use convert here, casting gives a null answer
		var upvotes = reader.GetInt16("upvotes");
		var downvotes = reader.GetInt16("downvotes");
		return new PlaylistEntry(id, videoId, playlistItemId, channelId, userId, timeSubmitted, messageId, upvotes, downvotes);
	}

	public async Task<bool> DeleteRow(string tableName, ulong serverId, ulong channelId)
    {
        await using NpgsqlCommand command = new(){Connection = Connection};
        string deleteCommand = $"DELETE FROM {tableName} WHERE server_id = @serverId AND channel_id = @channelId;";
        command.CommandText = deleteCommand;
        command.Parameters.AddWithValue("serverId", NpgsqlDbType.Numeric, (BigInteger)serverId);
        command.Parameters.AddWithValue("channelId", NpgsqlDbType.Numeric, (BigInteger)channelId);
        var rowsChanged = await command.ExecuteNonQueryAsync();
        return rowsChanged > 0;
    }
	public async Task InsertRow(string tableName, ulong serverId, ulong? channelId = null, string? playlistId = null)
	{
		var insertCommand = $"INSERT INTO " +
		                    $"{tableName}(server_id{(channelId is not null ? ", channel_id" : "")} {(string.IsNullOrWhiteSpace(playlistId) ? "" : ", playlist_id")})" +
		                    $" VALUES(@server_id{(channelId is not null ? ", @channel_id" : "")} {(string.IsNullOrWhiteSpace(playlistId) ? "" : ", @playlist_id")});";
		await using var cmd = new NpgsqlCommand{CommandText = insertCommand,Connection = Connection};
		Console.WriteLine(insertCommand);
		cmd.Parameters.AddWithValue("server_id", NpgsqlDbType.Numeric, (BigInteger)serverId);
		if (channelId > 0)
		{
			cmd.Parameters.AddWithValue("channel_id", NpgsqlDbType.Numeric, (BigInteger)channelId);
		}

		if (!string.IsNullOrWhiteSpace(playlistId))
		{
			cmd.Parameters.AddWithValue("playlist_id", NpgsqlDbType.Text, playlistId);
		}
		await cmd.ExecuteNonQueryAsync();
	}

	public async Task<string> GetPlaylistId(string tableName, ulong channelId)
    {
        await using var command = new NpgsqlCommand
        {
            CommandText = $"SELECT playlist_id FROM {tableName} WHERE channel_id=@channelId;",
			Connection = Connection,
        };
        command.Parameters.AddWithValue("channelId", NpgsqlDbType.Numeric, (BigInteger)channelId);
        await using var reader = await command.ExecuteReaderAsync();
		await reader.ReadAsync();
		try
		{
			return reader.GetString("channel_id");
		}
		catch (InvalidCastException)
		{
			return "";
		}
	}

	public async Task<ulong> GetChannelId(string tableName, ulong serverId)
	{
		await using var reader = await ExecuteQuery($"SELECT channel_id FROM {tableName} WHERE server_id={serverId};");
		await reader.ReadAsync();
		return (ulong)reader.GetDecimal(0);
	}

	public async IAsyncEnumerable<ulong> GetChannelIds(string tableName, ulong serverId)
	{
		await using var reader = await ExecuteQuery($"SELECT channel_id FROM {tableName};");
		var channelIDs = new List<ulong>();
		while (await reader.ReadAsync()) yield return (ulong)reader.GetDecimal(1);
	}
	
	public async Task<bool> ChangeChannelId(string tableName, ulong originalChannel, ulong newChannel)
    {
        await using var command =
            new NpgsqlCommand
            {
                CommandText = $"UPDATE {tableName} SET channel_id = @newChannel WHERE channel_id = @originalChannel;",
                Connection = Connection
            };

		command.Parameters.AddWithValue("originalChannel", NpgsqlDbType.Numeric, (BigInteger)originalChannel);
		command.Parameters.AddWithValue("newChannel", NpgsqlDbType.Numeric, (BigInteger)newChannel);
		var nRows = command.ExecuteNonQuery();
        return nRows != 0;
    }
	public async Task ChangePlaylistId(string tableName, ulong serverId, ulong channelId, string playlistId)
	{
		await using var command =
			new NpgsqlCommand($"UPDATE {tableName} SET playlist_id = @playlist_id WHERE channel_id = @channel_id;",
				Connection);

		command.Parameters.AddWithValue("playlist_id", NpgsqlDbType.Text, playlistId);
		command.Parameters.AddWithValue("channel_id", NpgsqlDbType.Numeric, (BigInteger)channelId);
		var nRows = command.ExecuteNonQuery();
		if (nRows == 0)
		{
			await this.InsertRow(tableName, serverId, channelId, playlistId);
			nRows = -1;
		}
		await Console.Out.WriteLineAsync($"Number of rows updated={nRows}");
	}

	private async Task ExecuteNonQuery(string commandString)
	{
		await using var command = new NpgsqlCommand(commandString, Connection);
		await command.ExecuteNonQueryAsync();
	}

	private async Task<NpgsqlDataReader> ExecuteQuery(string commandString)
	{
		await using var command = new NpgsqlCommand(commandString, Connection);
		Console.WriteLine(commandString);
		return await command.ExecuteReaderAsync();
	}

	private void other()
	{
		using var command1 =
		       new NpgsqlCommand("CREATE TABLE inventory(id serial PRIMARY KEY, name VARCHAR(50), quantity INTEGER);",
			       Connection);
		command1.ExecuteNonQuery();
			Console.Out.WriteLine("Finished creating table");

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
			Console.Out.WriteLine(string.Format("Number of rows inserted={0}", nRows));


			Console.WriteLine("Press RETURN to exit");
		Console.ReadLine();
	}
}