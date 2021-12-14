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
// ReSharper disable UnusedMember.Global

namespace DiscordMusicRecs
{
	internal class RecommendationCommands : BaseCommandModule
	{
        [Command("help")]
		public async Task HelpCommand(CommandContext ctx)
		{
			await ctx.RespondAsync("Use Slash Commands").ConfigureAwait(false);
		}
	}
}
