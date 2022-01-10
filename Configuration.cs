using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace DiscordMusicRecs
{
	public class Configuration
	{
		[DataMember(IsRequired = true)]
		public string Token { get; set; } = null!;
        [DataMember(IsRequired = true)]
		public Postgres PostgresConfig { get; set; } = null!;
		public class Postgres
		{
            [DataMember(IsRequired = true)]
			public string Host { get; set; } = null!;
            [DataMember(IsRequired = true)]
			public string Password { get; set; } = null!;
            [DataMember(IsRequired = true)]
			public string User { get; set; } = null!;
            [DataMember(IsRequired = true)]
			public string Port { get; set; } = null!;
            [DataMember(IsRequired = true)]
			public string DbName { get; set; } = null!;
            [DataMember(IsRequired = true)]
			public string MainTableName { get; set; } = null!;
            [DataMember(IsRequired = true)]
			public string LogTableName { get; set; } = null!;
		}
        [DataMember(IsRequired = true)]
		public string InviteUrl { get; set; } = null!;
        [DataMember(IsRequired = true)]
		public string YoutubeSecretsFile { get; set; } = null!;
		public static Configuration ReadConfig(string configPath = "config.json")
		{
			string jsonString = File.ReadAllText(configPath);
            var options = new JsonSerializerOptions
            {
                AllowTrailingCommas = true,
				PropertyNameCaseInsensitive = true,
				ReadCommentHandling = JsonCommentHandling.Skip,
                WriteIndented = true
            };
			Configuration configuration = JsonSerializer.Deserialize<Configuration>(jsonString, options) ?? throw new InvalidOperationException("Configuration cannot be null");
            if (configuration.Token is null)
            {
                
            }
			return configuration;
		}
	}
}
