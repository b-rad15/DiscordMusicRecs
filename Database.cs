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
using Npgsql;
using NpgsqlTypes;

namespace DiscordMusicRecs;

internal class Database
{
	// Obtain connection string information from the portal
	//
	private string Host = Program.Config.PostgresConfig.Host;
	private string User = Program.Config.PostgresConfig.User;
	private string DBname = "discord";
	private string Password = Program.Config.PostgresConfig.Password;
	private string Port = Program.Config.PostgresConfig.Port;
	private NpgsqlConnection Connection;
	private string connectionString;

    public const string TableName = "storage_test";

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
    private Database()
	{
		// Build connection string using parameters from portal
		//
		connectionString =
			$"Server={Host};Username={User};Database={DBname};Port={Port};Password={Password};SSLMode=Prefer";
		Connection = GetConnection();
	}

    public static Database Instance { get; } = new Database();

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

	public async Task MakeServerTable(string tableName)
	{
		await ExecuteNonQuery(
			$"CREATE TABLE IF NOT EXISTS {tableName}(id serial PRIMARY KEY,server_id NUMERIC(20,0) UNIQUE, channel_id NUMERIC(20,0),playlist_id TEXT);");
	}

    public async Task<MainData?> GetRowData(string tableName, 
        int? id = null, ulong? serverId = null, ulong? channelId = null, string? playlistId = null)
    {
        if (id is null && serverId is null && channelId is null && playlistId is null)
        {
            throw new Exception("At least one of id, serverId, channelId, or playlistId");
        }

        NpgsqlCommand command = new()
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
		serverId = reader.GetValue(1) as ulong?;
        channelId = reader.GetValue(2) as ulong?;
        playlistId = reader.GetValue(3) as string;
        return new MainData(id, serverId, channelId, playlistId);
    }

    public async Task<bool> DeleteRow(string tableName, ulong serverId, ulong channelId)
    {
        NpgsqlCommand command = new(){Connection = Connection};
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
        var command = new NpgsqlCommand
        {
            CommandText = $"SELECT playlist_id FROM {tableName} WHERE channel_id=@channelId;",
			Connection = Connection,
        };
        command.Parameters.AddWithValue("channelId", NpgsqlDbType.Numeric, (BigInteger)channelId);
        await using var reader = await command.ExecuteReaderAsync();
		await reader.ReadAsync();
		try
		{
			return reader.GetString(0);
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
                CommandText = $"UPDATE {tableName} SET channel_id = @newChannel WHERE channel_id = @originalChannel",
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
			new NpgsqlCommand($"UPDATE {tableName} SET playlist_id = @playlist_id WHERE channel_id = @channel_id",
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
		using (var command =
		       new NpgsqlCommand("CREATE TABLE inventory(id serial PRIMARY KEY, name VARCHAR(50), quantity INTEGER)",
			       Connection))
		{
			command.ExecuteNonQuery();
			Console.Out.WriteLine("Finished creating table");
		}

		using (var command =
		       new NpgsqlCommand("INSERT INTO inventory (name, quantity) VALUES (@n1, @q1), (@n2, @q2), (@n3, @q3)",
			       Connection))
		{
			command.Parameters.AddWithValue("n1", "banana");
			command.Parameters.AddWithValue("q1", 150);
			command.Parameters.AddWithValue("n2", "orange");
			command.Parameters.AddWithValue("q2", 154);
			command.Parameters.AddWithValue("n3", "apple");
			command.Parameters.AddWithValue("q3", 100);

			var nRows = command.ExecuteNonQuery();
			Console.Out.WriteLine(string.Format("Number of rows inserted={0}", nRows));
		}


		Console.WriteLine("Press RETURN to exit");
		Console.ReadLine();
	}
}