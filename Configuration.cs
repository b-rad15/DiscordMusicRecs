using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;

namespace DiscordMusicRecs
{
	public class Configuration
	{
		public string Token { get; set; }
		public Postgres PostgresConfig { get; set; }

		public static Configuration ReadConfig(string congigPath = "config.json")
		{
			string jsonstring = File.ReadAllText(congigPath);
			Configuration? configuration = JsonSerializer.Deserialize<Configuration>(jsonstring);
			return configuration;
		}

		public class Postgres
		{
			public string Host { get; set; }
			public string Password { get; set; }
			public string User { get; set; }
			public string Port { get; set; }
			public string DbName { get; set; }
			public string MainTableName { get; set; }
			public string LogTableName { get; set; }

		}
	}
}
