using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.SlashCommands;
using DSharpPlus.Entities;

namespace DiscordMusicRecs
{
	internal class RecommendationCommands : BaseCommandModule
	{
        string baseHelpCommand = "use !setchannel <#channel-mention> to set channel where recommendations are taken\n" +
                                 "use !recsplaylist to get this server's recommendation playlist\n" +
                                 "use !randomrec to get a random recommendation from the playlist\n";
        [Command("rec")]
		public async Task RecommendCommand(CommandContext ctx, string url)
		{
		}
	}
}
