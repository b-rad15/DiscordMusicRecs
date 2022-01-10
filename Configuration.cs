﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;

namespace DiscordMusicRecs
{
	public class Configuration
	{
		public string Token { get; set; } = null!;
		public Postgres PostgresConfig { get; set; } = null!;
		public class Postgres
		{
			public string Host { get; set; } = null!;
			public string Password { get; set; } = null!;
			public string User { get; set; } = null!;
			public string Port { get; set; } = null!;
			public string DbName { get; set; } = null!;
			public string MainTableName { get; set; } = null!;
			public string LogTableName { get; set; } = null!;
		}
		public string InviteUrl { get; set; } = null!;
		public string YoutubeSecretsFile { get; set; } = null!;
		public static Configuration ReadConfig(string configPath = "config.json")
		{
			string jsonString = File.ReadAllText(configPath);
			Configuration configuration = JsonSerializer.Deserialize<Configuration>(jsonString) ?? throw new InvalidOperationException("Configuration cannot be null");
			return configuration;
		}
	}
}
