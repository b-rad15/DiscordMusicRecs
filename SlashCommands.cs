using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Text.RegularExpressions;
using DSharpPlus;
using DSharpPlus.CommandsNext.Converters;
using DSharpPlus.Entities;
using DSharpPlus.Exceptions;
using DSharpPlus.Interactivity.Enums;
using DSharpPlus.Interactivity.Extensions;
using DSharpPlus.SlashCommands;
using Google.Apis.YouTube.v3.Data;
using Npgsql;

namespace DiscordMusicRecs;

public class RequireBotAdminPrivilegeAttribute : SlashCheckBaseAttribute
{
    private const string PermissionsTableName = "server_permissions";

    public RequireBotAdminPrivilegeAttribute(ulong userId, Permissions permissions, bool ignoreDms = true)
    {
        UserId = userId;
        Permissions = permissions;
        IgnoreDms = ignoreDms;
    }

    //Based on the sealed SlashRequireUserPermissionsAttribute
    private ulong UserId { get; }
    public Permissions Permissions { get; }
    public bool IgnoreDms { get; }

    public override async Task<bool> ExecuteChecksAsync(InteractionContext ctx)
    {
        if (ctx.Guild == null)
            return IgnoreDms;
        var member = ctx.Member;
        if (member == null)
            return false;
        if ((long)member.Id == (long)ctx.Guild.OwnerId)
            return true;
        var permissions = ctx.Channel.PermissionsFor(member);
        if ((permissions & Permissions.Administrator) != Permissions.None)
            return true;
        if ((permissions & Permissions) == Permissions || ctx.Member.Id == UserId) return true;
        //Implement role check here
        return false;
    }
}

public class SlashCommands : ApplicationCommandModule
{
    private static readonly Regex iReallyHopeThisPullsPlaylistIdsRegex = new(
        @"^((?:https?:)?\/\/)?((?:www|m|music)\.)?((?:youtube\.com))(\/(?:[\w\-]+\?list=|embed\/|list\/)?)([\w\-]+)(\S+)?$");

    public static bool IsTextChannel(DiscordChannel channel)
    {
        return channel.Type is ChannelType.Text;
    }

    private IEnumerable<Type> GetAllNestedTypes(Type t)
    {
        foreach (var nestedType in t.GetNestedTypes())
        {
            yield return nestedType;
            foreach (var recursiveNested in GetAllNestedTypes(nestedType))
            {
                yield return recursiveNested;
            }
        }
    }

    [SlashCommand("help", "Learn how to use Music Recs")]
    public async Task HelpDiscordRecsBotCommand(InteractionContext ctx)
    {
        await ctx.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource);
        var msg = new DiscordWebhookBuilder();
        var embed = new DiscordEmbedBuilder
        {
            Color = DiscordColor.Red,
            Title = $"How Do I Use {Program.Discord.CurrentUser.Username} <:miihinotes:913303041057390644>"
        };
        List<Type> slashCommandGroups = new() { GetType() };
        slashCommandGroups.AddRange(GetAllNestedTypes(GetType()));
        var helpString = "**Commands**:\n";
        foreach (var slashCommandGroup in slashCommandGroups)
        {
            var methodInfos = slashCommandGroup.GetMethods();
            foreach (var methodInfo in methodInfos)
            {
                var slashCommandsAttr = methodInfo
                    .GetCustomAttributes()
                    .OfType<SlashCommandAttribute>();
                helpString = slashCommandsAttr.Aggregate(helpString, (current, attribute) => current + $"`/{attribute.Name}`\n{attribute.Description}\n");
            }
        }
        try
        {
            var rowsData = await Database.Instance.GetRowsData(Database.MainTableName, serverId: ctx.Guild.Id).ToListAsync();
            helpString += "**Recommendation Channels**:\n";
            foreach (var rowData in rowsData)
            {
                if (!rowData.HasValue) //never be true if more than one entry was added
                {
                    helpString +=
                        "send a youtube link in the recommendation channel after setting up with /admin addplaylist to recommend a song";
                }
                else
                {
                    Debug.Assert(rowData.Value.channelId != null, "rowData.Value.channelId != null");
                    var recsChannel = await Program.Discord.GetChannelAsync(rowData.Value.channelId.Value);
                    helpString += recsChannel.Mention;
                    if (rowData.Value.playlistId != null)
                    {
                        var playlistUrl = YoutubeAPIs.IdToPlaylist(rowData.Value.playlistId);
                        helpString += $": <{playlistUrl}>";
                    }

                    helpString += '\n';
                }
            }
                
        }
        catch (Exception exception)
        {
            Debugger.Break();
            await Console.Error.WriteLineAsync(exception.ToString());
            // msg.WithContent(
            //         baseHelpCommand +
            //         "send a youtube link in the recommendation channel after setting up with !setchannel to recommend a song");
            helpString +=
                "send a youtube link in the recommendation channel after setting up with !setchannel to recommend a song";
        }
        finally
        {
            embed.WithDescription(helpString);
            msg.AddEmbed(embed.Build());
            await ctx.EditResponseAsync(msg);
        }
    }

    [SlashCommand("GetRanking",
        "Get's the users' ranking of this playlist")]
    public async Task GetRankedPlaylist(InteractionContext ctx,
        [Option("PlaylistChannel", "channel to rank results from")][ChannelTypes(ChannelType.Text, ChannelType.PublicThread)] DiscordChannel playlistChannel)
    {
        // await ctx.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource);
        var rowData = await Database.Instance.GetRowData(Database.MainTableName, channelId: playlistChannel.Id);
        var msg = new DiscordWebhookBuilder();
        // var interactivity = ctx.Client.GetInteractivity();
        if (rowData?.playlistId is null)
        {
	        await ctx.CreateResponseAsync($"{playlistChannel.Mention} has no playlists");
            return;
        }

        var playlistItems = await Database.Instance.GetPlaylistItems(rowData.Value.playlistId).ToListAsync();
        var sortedPlaylistItems = playlistItems.OrderByDescending(item => item?.upvotes);
        var embedBuilder = new DiscordEmbedBuilder
        {
            Title = $"{playlistChannel.Name}'s Top Tracks"
        };
        string rankingString = "";
        var rank = 0;
        foreach (var item in sortedPlaylistItems)
        {
            if (item is null)
            {
                await ctx.CreateResponseAsync("No items found in playlist");
                return;
            }

            Debug.Assert(item?.videoId != null, "item?.videoId != null");
            rankingString += $"`#{++rank}` {YoutubeAPIs.IdToVideo(item.Value.videoId)} {item.Value.upvotes} upvotes\n";
        }
        
        var paginatedEmbed = Program.Interactivity.GeneratePagesInEmbed(rankingString, SplitType.Line, embedBuilder);
        await ctx.Interaction.SendPaginatedResponseAsync(false, ctx.Member, paginatedEmbed
			, behaviour: PaginationBehaviour.WrapAround, deletion: ButtonPaginationBehavior.DeleteButtons);
    }

    [SlashCommand("randomrec", "gives a random recommendation from the specified channel's playlist")]
    public async Task RandomRec(InteractionContext ctx,
        [Option("PlaylistChannel", "channel that the playlist is bound to")][ChannelTypes(ChannelType.Text, ChannelType.PublicThread)] DiscordChannel playlistChannel)
    {
        await ctx.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource);
        DiscordWebhookBuilder msg = new();
        try
        {
            var rowData = await Database.Instance.GetRowData(Database.MainTableName,
                serverId: ctx.Guild.Id,
                channelId: playlistChannel.Id);
            if (!rowData.HasValue)
            {
                msg.WithContent(
                    $"{playlistChannel.Mention} is not set up as a recommendations channel");
                await ctx.EditResponseAsync(msg);
                return;
            }

            if (string.IsNullOrWhiteSpace(rowData.Value.playlistId))
            {
                msg.WithContent(
                    $"{playlistChannel.Mention} has no assigned playlist");
                await ctx.EditResponseAsync(msg);
                return;
            }

            var randomVideoInPlaylist = await YoutubeAPIs.Instance.GetRandomVideoInPlaylist(rowData.Value.playlistId);
            var vidId = randomVideoInPlaylist.Snippet.ResourceId.VideoId;
            var randomVid = $"https://www.youtube.com/watch?v={vidId}";
            msg.WithContent($"You should listen to {randomVid}");
            await ctx.EditResponseAsync(msg);
        }
        catch (Exception exception)
        {
            Debugger.Break();
            await Console.Error.WriteLineAsync(exception.ToString());
            msg = new DiscordWebhookBuilder();
            msg.WithContent(
                $"No Playlist or Videos found, if videos have been added, ask {Program.BotOwnerDiscordUser?.Mention} to check logs");
            await ctx.EditResponseAsync(msg);
        }
    }

    [SlashCommand("recsplaylist", "List the playlist that is bound to the given channel")]
    public async Task GetRecsPlaylist(InteractionContext ctx,
        [Option("PlaylistChannel", "channel that the playlist is bound to")][ChannelTypes(ChannelType.Text, ChannelType.PublicThread)] DiscordChannel playlistChannel)
    {
        await ctx.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource);
        DiscordWebhookBuilder msg = new();
        try
        {
            var rowData = await Database.Instance.GetRowData(Database.MainTableName, serverId: ctx.Guild.Id,
                channelId: playlistChannel.Id);
            if (!rowData.HasValue)
            {
                msg.WithContent(
                    $"{playlistChannel.Mention} is not set up as a recommendations channel");
                await ctx.EditResponseAsync(msg);
                return;
            }

            if (string.IsNullOrWhiteSpace(rowData.Value.playlistId))
            {
                msg.WithContent(
                    $"{playlistChannel.Mention} has no assigned playlist");
                await ctx.EditResponseAsync(msg);
                return;
            }

            msg.WithContent(
                $"{playlistChannel.Mention}'s playlist is {YoutubeAPIs.IdToPlaylist(rowData.Value.playlistId)}");
            await ctx.EditResponseAsync(msg);
        }
        catch (Exception exception)
        {
            Debugger.Break();
            await Console.Error.WriteLineAsync(exception.ToString());
            msg = new DiscordWebhookBuilder();
            msg.WithContent(
                $"Shits bad {Program.BotOwnerDiscordUser?.Mention}");
            await ctx.EditResponseAsync(msg);
        }
    }


    [SlashCommandGroup("Admin", "Administrative commands")]
    public class AdminSlashCommands : ApplicationCommandModule
    {

        [SlashCommand("addplaylist", "Adds channel to watch with a new playlist, to move existing to new channel use `/moveplaylist`")]
        [RequireBotAdminPrivilege(Program.BotOwnerId, Permissions.Administrator)]
        public async Task AddPlaylist(InteractionContext ctx,
            [Option("BindChannel", "channel to bind to")][ChannelTypes(ChannelType.Text, ChannelType.PublicThread)] DiscordChannel bindChannel,
            [Option("PlaylistTitle", "The title of the playlist")] string? playlistTitle = null,
            [Option("PlaylistDescription", "The description of the playlist")] string? playlistDescription = null,
            [Option("PlaylistBase", "The id of the playlist to add songs too, this MUST be a playlist created by the bot")] string? playlistId = null)
        {
            await ctx.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource);
            if (bindChannel.IsThread)
            {
	            DiscordThreadChannel bindThread = (DiscordThreadChannel) bindChannel;
	            if (bindThread is not null)
	            {
		            await bindThread.JoinThreadAsync();
	            }
            }
            DiscordWebhookBuilder msg = new();
            if (!string.IsNullOrWhiteSpace(playlistId))
            {
                var validPlaylist = false;
                Match? playlistMatch;
                if ((playlistMatch = iReallyHopeThisPullsPlaylistIdsRegex.Match(playlistId)).Success)
                    playlistId = playlistMatch.Groups[5].Value;

                await foreach (var channelPlaylistId in YoutubeAPIs.Instance.GetMyPlaylistIds())
                    if (channelPlaylistId == playlistId)
                    {
                        validPlaylist = true;
                        break;
                    }

                if (!validPlaylist)
                {
                    msg.WithContent("Invalid Playlist Given. Make sure the bot created it");
                    await ctx.EditResponseAsync(msg);
                    return;
                }
            }
            else
            {
                try
                {
                    playlistId = await YoutubeAPIs.Instance.NewPlaylist(playlistTitle, playlistDescription);
                    await Database.Instance.MakePlaylistTable(playlistId);
                }
                catch (Exception exception)
                {
                    await Console.Error.WriteLineAsync(exception.ToString());
                    Debugger.Break();
                    msg = new DiscordWebhookBuilder();
                    msg.WithContent(
                        $"Failed to make a new playlist for this channel, {Program.BotOwnerDiscordUser?.Mention} any ideas?");
                    await ctx.EditResponseAsync(msg);
                    return;
                }
            }

            try
            {
                await Database.Instance.InsertRow(Database.MainTableName, ctx.Guild.Id, bindChannel.Id, playlistId);
                msg.WithContent(
                    $"Successfully watching channel {bindChannel.Mention} and adding to playlist {YoutubeAPIs.IdToPlaylist(playlistId)}");
            }
            catch (Exception exception)
            {
                Debugger.Break();
                await Console.Error.WriteLineAsync(exception.ToString());
                msg = new DiscordWebhookBuilder();
                msg.WithContent(
                    $"Failed to adds recs channel, ask {Program.BotOwnerDiscordUser?.Mention} to check logs");
            }
            finally
            {
                await ctx.EditResponseAsync(msg);
            }
        }

        [SlashCommand("moveplaylist", "Moves existing watch from one channel to another")]
        [RequireBotAdminPrivilege(Program.BotOwnerId, Permissions.Administrator)]
        public async Task MovePlaylist(InteractionContext ctx,
            [Option("OriginalChannel", "channel to move the playlist from")] [ChannelTypes(ChannelType.Text, ChannelType.PublicThread)] DiscordChannel originalChannel,
            [Option("BindChannel", "channel to bind the playlist to")] [ChannelTypes(ChannelType.Text, ChannelType.PublicThread)] DiscordChannel bindChannel)
        {
            await ctx.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource);
            DiscordWebhookBuilder msg = new();
            if (originalChannel.Equals(bindChannel))
            {
                msg.WithContent("Arguments must not be the same channel twice");
                await ctx.EditResponseAsync(msg);
                return;
            }

            try
            {
                var rowData = await Database.Instance.GetRowData(Database.MainTableName, serverId: ctx.Guild.Id,
                    channelId: originalChannel.Id);
                if (!rowData.HasValue)
                {
                    msg.WithContent("There is no playlist connected to this channel");
                    await ctx.EditResponseAsync(msg);
                    return;
                }

                var wasChanged =
                    await Database.Instance.ChangeChannelId(Database.MainTableName, originalChannel.Id, bindChannel.Id);
                if (!wasChanged) await Console.Error.WriteLineAsync($"Fuck, {new StackFrame().GetFileLineNumber()}");

                rowData = await Database.Instance.GetRowData(Database.MainTableName, serverId: ctx.Guild.Id,
                    channelId: bindChannel.Id);
                if (!rowData.HasValue)
                {
                    msg.WithContent(
                        Program.BotOwnerDiscordUser != null
                            ? $"There is no database entry for the new channel, {Program.BotOwnerDiscordUser.Mention}'s not sure what went wrong tbh"
                            : "There is no database entry for the new channel, I'm not sure what went wrong tbh");
                    await ctx.EditResponseAsync(msg);
                    return;
                }

                msg.WithContent(
                    $"Playlist {(string.IsNullOrEmpty(rowData.Value.playlistId) ? "not set up but" : YoutubeAPIs.IdToPlaylist(rowData.Value.playlistId))} watch successfully moved from {originalChannel.Mention} to {bindChannel.Mention}");
            }
            catch (PostgresException pgException)
            {
                await Console.Error.WriteLineAsync(pgException.ToString());
                Debugger.Break();
                msg = new DiscordWebhookBuilder();
                msg.WithContent($"Failed to find entry {originalChannel.Mention}. It probably is not a recs channel");
            }
            catch (Exception exception)
            {
                await Console.Error.WriteLineAsync(exception.ToString());
                Debugger.Break();
                msg = new DiscordWebhookBuilder();
                msg.WithContent(
                    $"Failed to modify recs channel, ask {Program.BotOwnerDiscordUser?.Mention} to check logs");
            }

            await ctx.EditResponseAsync(msg);
        }

        [SlashCommand("deleteplaylist", "Remove channel watch&optionally delete playlist, to move existing to new channel use `/moveplaylist`")]
        [RequireBotAdminPrivilege(Program.BotOwnerId, Permissions.Administrator)]
        public async Task DeletePlaylist(InteractionContext ctx,
            [Option("WatchChannel", "channel to remove watch from")] [ChannelTypes(ChannelType.Text, ChannelType.PublicThread)] DiscordChannel watchChannel,
            [Option("DeletePlaylist", "Should the playlist be deleted?")] bool shouldDeletePlaylist = true)
        {
            await ctx.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource);
            DiscordWebhookBuilder msg = new();
            var rowData =
                await Database.Instance.GetRowData(Database.MainTableName, serverId: ctx.Guild.Id,
                    channelId: watchChannel.Id);
            if (!rowData.HasValue)
            {
                msg.WithContent("There is no playlist connected to this channel");
                await ctx.EditResponseAsync(msg);
                return;
            }

            var msgResponse = "";
            try
            {
                if (await Database.Instance.DeleteRow(Database.MainTableName, ctx.Guild.Id, watchChannel.Id))
                    msgResponse += $"Successfully removed watch on {watchChannel.Mention}\n";

                if (shouldDeletePlaylist)
                {
                    if (rowData.Value.playlistId is null)
                    {
                        msgResponse += "No playlist to delete\n";
                    }
                    else
                    {
                        var didDeletePlaylist = await YoutubeAPIs.Instance.DeletePlaylist(rowData.Value.playlistId);
                        if (didDeletePlaylist)
                            msgResponse += "Successfully deleted playlist\n";
                        else
                            msgResponse +=
                                $"Failed to delete playlist {YoutubeAPIs.IdToPlaylist(rowData.Value.playlistId)}\n";
                    }
                }
                else
                {
                    if (rowData.Value.playlistId != null)
                        msgResponse +=
                            $"Playlist will continue to exist at {YoutubeAPIs.IdToPlaylist(rowData.Value.playlistId)}. Keep this link, you will never be able to retrieve it again later\n";
                    else
                        msgResponse += "This channel had no playlist so there is nothing to save\n";
                }

                if (rowData?.playlistId is not null)
                {
	                ulong channelId;
	                var playlistEntries = await Database.Instance.GetPlaylistItems(rowData.Value.playlistId).ToListAsync();
	                foreach (var playlistEntry in playlistEntries)
	                {
		                if (playlistEntry is null)
		                {
			                //No Need to handle if there are no playlist entries, marking isn't important
                            break;
		                }
		                if (playlistEntry?.channelId is null)
		                {
			                if (rowData.Value.channelId is null)
			                {
				                await Console.Error.WriteLineAsync("No Channel found, cannot delete message");
                                continue;
			                }
			                else
			                {
				                channelId = rowData.Value.channelId.Value;

			                }
		                }
		                else
		                {
			                channelId = playlistEntry.Value.channelId.Value;
		                }
                        await MarkNoVoting(channelId, playlistEntry!.Value.messageId!.Value);
	                }
                }

                msg.WithContent(msgResponse);
            }
            catch (Exception exception)
            {
                Debugger.Break();
                await Console.Error.WriteLineAsync(exception.ToString());
                msgResponse +=
                    $"Failed to remove recs channel, ask {Program.BotOwnerDiscordUser?.Mention} to check logs\n";
            }
            finally
            {
                msg.WithContent(msgResponse.Trim());
                await ctx.EditResponseAsync(msg);
            }
        }

        [SlashCommand("deletevideo",
	        "Removes given video from the given channel's playlist")]
        [RequireBotAdminPrivilege(Program.BotOwnerId, Permissions.Administrator)]
        public async Task DeleteVideo(InteractionContext ctx,
	        [Option("PlaylistChannel", "channel that the playlist is bound to")]
	        [ChannelTypes(ChannelType.Text, ChannelType.PublicThread)]
	        DiscordChannel watchChannel,
	        [Option("Video", "The Video to be removed from the playlist")]
	        string videoUrl)
        {
	        await ctx.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource);
	        var msg = new DiscordWebhookBuilder();
	        Match? match;
	        if ((match = Program.iShouldJustCopyStackOverflowYoutubeRegex.Match(videoUrl)).Success &&
	            match.Groups[5].Value is not "playlist" or "watch" or "channel" &&
	            !string.IsNullOrWhiteSpace(match.Groups[5].Value))
	        {
		        var id = match.Groups[5].Value;
		        var rowData = await Database.Instance.GetRowData(Database.MainTableName, serverId: ctx.Guild.Id,
			        channelId: watchChannel.Id);
		        if (rowData?.playlistId is null)
		        {
			        msg.WithContent("Channel specified has no playlist connected, cannot delete videos from it");
			        await ctx.EditResponseAsync(msg);
			        return;
		        }

		        try
		        {
			        var removeString = $"Removing from playlist {YoutubeAPIs.IdToPlaylist(rowData.Value.playlistId)}\n";
			        await ctx.EditResponseAsync(msg.WithContent(removeString));

			        var vidToDelete = await Database.Instance.GetPlaylistItem(rowData.Value.playlistId, videoId: id);
			        if (!vidToDelete.HasValue)
			        {
				        msg.WithContent(removeString + "No Videos Found");
				        await ctx.EditResponseAsync(msg);
				        return;
			        }

			        removeString += await DeleteEntryFromPlaylist(vidToDelete.Value, rowData.Value.playlistId!);
			        await ctx.EditResponseAsync(msg.WithContent(removeString));
		        }
		        catch (Exception e)
		        {
			        await Console.Error.WriteLineAsync(e.ToString());
			        Debugger.Break();
			        msg = new DiscordWebhookBuilder();
			        msg.WithContent(
				        $"Error removing {YoutubeAPIs.IdToVideo(id)} from {YoutubeAPIs.IdToPlaylist(rowData.Value.playlistId)}");
			        await ctx.EditResponseAsync(msg);
		        }
	        }
	        else
	        {
		        msg.WithContent(
			        $"Cannot extract video id, url given does not match `regex\n{Program.iShouldJustCopyStackOverflowYoutubeRegex}`");
		        await ctx.EditResponseAsync(msg);
	        }
        }
    

        [SlashCommand("deleteallfromuser",
            "Removes all videos submitted by mentioned user from the given channel's playlist")]
        [RequireBotAdminPrivilege(Program.BotOwnerId, Permissions.Administrator)]
        public async Task DeleteAllFromUser(InteractionContext ctx,
            [Option("PlaylistChannel", "channel that the playlist is bound to")] [ChannelTypes(ChannelType.Text, ChannelType.PublicThread)] DiscordChannel watchChannel,
            [Option("User", "The user to delete all from")] DiscordUser badUser,
	        [Option("DeleteSubmissionMessages", "Delete User's Messages? If not, submissions will only be deleted from the playlist, not the channel")] bool deleteMessage = true)
        {
            await ctx.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource);
            var msg = new DiscordWebhookBuilder();
            var rowData = await Database.Instance.GetRowData(Database.MainTableName, serverId: ctx.Guild.Id, channelId: watchChannel.Id);
            if (rowData?.playlistId is null)
            {
                msg.WithContent("Channel specified has no playlist connected, cannot delete videos from it");
                await ctx.EditResponseAsync(msg);
                return;
			}
            var removeString = $"Removing from playlist {YoutubeAPIs.IdToPlaylist(rowData.Value.playlistId)}\n";
            await ctx.EditResponseAsync(msg.WithContent(removeString));

            var vidsToDelete = await Database.Instance.GetPlaylistItems(rowData.Value.playlistId, userId:badUser.Id).ToListAsync();
            foreach (var vidToDelete in vidsToDelete)
            {
                if (!vidToDelete.HasValue)
                {
	                msg.WithContent(removeString + "No Videos Found");
	                await ctx.EditResponseAsync(msg);
	                return;
                }

                removeString += await DeleteEntryFromPlaylist(vidToDelete.Value, rowData.Value.playlistId!);
            }
            await ctx.EditResponseAsync(msg.WithContent(removeString));
        }
    }

    private static async Task MarkNoVoting(ulong channelId, ulong messageId)
    {
	    DiscordMessage disconnectedMessage;
	    try
	    {
		    disconnectedMessage = await (await Program.Discord.GetChannelAsync(channelId)).GetMessageAsync(messageId);
		    await disconnectedMessage.DeleteAllReactionsAsync("Playlist deleted, no longer accepting votes");
		    await disconnectedMessage.CreateReactionAsync(DiscordEmoji.FromUnicode("❌"));
        }
	    catch (NotFoundException)
	    {

	    }
	    catch (UnauthorizedException e)
	    {
            Debugger.Break();
            await Console.Error.WriteLineAsync(e.ToString());
	    }
    }
    private static async Task<string> DeleteEntryFromPlaylist(Database.PlaylistEntry entry, string playlistId, bool removeMessage = true)
    {
	    var removeString = "";
	    try
	    {
		    var vidsRemoved = await YoutubeAPIs.Instance.RemoveFromPlaylist(playlistId, entry.videoId!);
		    if (vidsRemoved > 0)
		    {
			    if (vidsRemoved == 1)
				    removeString += $"Removed {YoutubeAPIs.IdToVideo(entry.videoId!)}\n";
			    else
			    {
				    removeString += $"Removed {vidsRemoved} videos matching {YoutubeAPIs.IdToVideo(entry.videoId!)}\n";
			    }
		    }
		    else
		    {
			    removeString += $"{YoutubeAPIs.IdToVideo(entry.videoId!)} not found in playlist";

		    }

		    var rowsDeleted = Database.Instance.DeletePlaylistItem(playlistId, entry.id);
		    if (removeMessage)
		    {
			    try
			    {
				    var msgToRemove =
					    await (await Program.Discord.GetChannelAsync(entry.channelId!.Value)).GetMessageAsync(
						    entry.messageId!.Value);
				    await msgToRemove.DeleteAsync("Entry not allowed, removed by a mod via the bot");
			    }
			    catch (Exception)
			    {
                    Debugger.Break();
                    try
                    {
	                    await MarkNoVoting(entry.channelId!.Value, entry.messageId!.Value);
	                    removeString += $"Failed to remove message {entry.messageId}, marked no voting instead\n";
                    }
                    catch (Exception e)
                    {
	                    removeString += $"Failed to remove message {entry.messageId} and couldn't react to indicate voting disallowed\n";
                    }
			    }
		    }
		    else
		    {
			    try
			    {
				    await MarkNoVoting(entry.channelId!.Value, entry.messageId!.Value);
                }
			    catch (Exception)
			    {
				    // If I can't mark it, it isn't a big deal
			    }
		    }
	    }
	    catch (Exception e)
	    {
		    await Console.Error.WriteLineAsync(e.ToString());
		    Debugger.Break();
		    removeString +=
			    $"Error removing {YoutubeAPIs.IdToVideo(entry.videoId!)} from {YoutubeAPIs.IdToPlaylist(playlistId)}";
	    }
	    return removeString;
    }
}