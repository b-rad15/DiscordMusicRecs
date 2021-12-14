using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Text.RegularExpressions;
using DSharpPlus;
using DSharpPlus.CommandsNext.Converters;
using DSharpPlus.Entities;
using DSharpPlus.Exceptions;
using DSharpPlus.Interactivity.Enums;
using DSharpPlus.Interactivity.Extensions;
using DSharpPlus.Net;
using DSharpPlus.SlashCommands;
using Google;
using Google.Apis.YouTube.v3.Data;
using Newtonsoft.Json;
using Npgsql;
using Serilog;
// ReSharper disable UnusedMember.Global

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
    private static readonly Regex IReallyHopeThisPullsPlaylistIdsRegex = new(@"^((?:https?:)?\/\/)?((?:www|m|music)\.)?((?:youtube\.com))(\/(?:[\w\-]+\?list=|embed\/|list\/)?)([\w\-]+)(\S+)?$");

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
        await ctx.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource).ConfigureAwait(false);
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
	        var prefix = "";
	        var slashCommandGroupAttributes = slashCommandGroup.GetCustomAttributes().OfType<SlashCommandGroupAttribute>();
	        var commandGroupAttributes = slashCommandGroupAttributes as SlashCommandGroupAttribute[] ?? slashCommandGroupAttributes.ToArray();
	        if (commandGroupAttributes.Any())
	        {
		        prefix += commandGroupAttributes.First().Name;
	        }

	        if (!string.IsNullOrWhiteSpace(prefix))
	        {
		        prefix += " ";
	        }
            var methodInfos = slashCommandGroup.GetMethods();
            foreach (var methodInfo in methodInfos)
            {
	            var slashCommandsAttr = methodInfo
                    .GetCustomAttributes()
                    .OfType<SlashCommandAttribute>();
	            foreach (var attribute in slashCommandsAttr) 
		            helpString += $"`/{prefix}{attribute.Name}`\n{attribute.Description}\n";
            }
        }

        var noPlaylistsFallback = "\nSend a YouTube link in a recommendation channel after setting up with `/admin addplaylist` to recommend a song";
        try
        {
            var rowsData = await Database.Instance.GetRowsData(Database.MainTableName, serverId: ctx.Guild.Id).ConfigureAwait(false);
            if (rowsData.Count > 0)
            {
	            helpString += "**Recommendation Channels**:\n";
                foreach (var rowData in rowsData)
	            {
		            DiscordChannel recsChannel;
		            try
		            {
			            if (rowData.ChannelId != null)
			            {
				            recsChannel = await Program.Discord.GetChannelAsync(rowData.ChannelId.Value).ConfigureAwait(false);
				            helpString += recsChannel.Mention + ": ";
			            }
			            else
			            {
                            //Channel Disconnected
                            helpString += "Orphaned Playlist: ";
			            }
			            var playlistUrl = YoutubeAPIs.IdToPlaylist(rowData.PlaylistId);
			            helpString += $"<{playlistUrl}>\n";
                    }
		            catch (NotFoundException)
		            {
			            await Program.HandleDeletedChannel(rowData.ChannelId!.Value, rowData: rowData).ConfigureAwait(false);
		            }
	            }
            }
            else
            {
	            helpString +=
		            noPlaylistsFallback;
            }
                
        }
        catch (Exception exception)
        {
            Debugger.Break();
            Log.Error(exception.ToString());
            helpString +=
                noPlaylistsFallback;
        }
        finally
        {
            embed.WithDescription(helpString + $"\n[Donate]({DonateLinks[0]}) [Github]({GithubLink})");
            msg.AddEmbed(embed.Build());
            await ctx.EditResponseAsync(msg).ConfigureAwait(false);
        }
    }

    private const string GithubLink = "https://github.com/b-rad15/DiscordMusicRecs";
    [SlashCommand("github", "Get the link to the Github Repo for Discord Music Recs")]
    public async Task GetGithubLink(InteractionContext ctx)
    {
	    await ctx.CreateResponseAsync(GithubLink).ConfigureAwait(false);
    }

    private static readonly string[] DonateLinks = { "https://ko-fi.com/bradocon" };
    [SlashCommand("donate", "Prints all links to donate to the bot owner")]
    public async Task GetDonateLinks(InteractionContext ctx)
    {
	    await ctx.CreateResponseAsync(string.Join('\n', DonateLinks)).ConfigureAwait(false);
    }

    [SlashCommand("GetRanking",
        "Gets the users' ranking of this playlist")]
    public async Task GetRankedPlaylist(InteractionContext ctx,
        [Option("PlaylistChannel", "Channel to rank results from")][ChannelTypes(ChannelType.Text, ChannelType.PublicThread)] DiscordChannel playlistChannel)
    {
        await ctx.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource).ConfigureAwait(false);
        var rowData = await Database.Instance.GetRowData(Database.MainTableName, channelId: playlistChannel.Id).ConfigureAwait(false);
        var msg = new DiscordWebhookBuilder();
        if (rowData?.PlaylistId is null)
        {
	        msg.WithContent($"{playlistChannel.Mention} has no playlists");
	        await ctx.EditResponseAsync(msg).ConfigureAwait(false);
            return;
        }

        var sortedPlaylistItems = await Database.Instance.GetRankedPlaylistItems(rowData.PlaylistId).ConfigureAwait(false);
        var embedBuilder = new DiscordEmbedBuilder
        {
            Title = $"{playlistChannel.Name}'s Top Tracks"
        };
        var rankingString = "";
        var rank = 0;
        if (sortedPlaylistItems.Count == 0)
	        embedBuilder.WithDescription("No Tracks Found in Playlist");
        else
        {
	        foreach (var item in sortedPlaylistItems)
	        {
		        Debug.Assert(item.VideoId != null, "item?.videoId != null");
		        rankingString +=
			        $"`#{++rank}` {YoutubeAPIs.IdToVideo(item.VideoId)} {item.Upvotes} upvotes\n";
	        }
	        var paginatedEmbed =
		        Program.Interactivity.GeneratePagesInEmbed(rankingString, SplitType.Line, embedBuilder);
	        await ctx.Interaction.SendPaginatedResponseAsync(false, ctx.Member, paginatedEmbed, asEditResponse: true,
		        behaviour: PaginationBehaviour.WrapAround, deletion: ButtonPaginationBehavior.DeleteButtons).ConfigureAwait(false);
        }
    }

    [SlashCommand("randomrec", "Gives a random recommendation from the specified channel's playlist")]
    public async Task RandomRec(InteractionContext ctx,
        [Option("PlaylistChannel", "Channel that the playlist is bound to")][ChannelTypes(ChannelType.Text, ChannelType.PublicThread)] DiscordChannel playlistChannel)
    {
        await ctx.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource).ConfigureAwait(false);
        DiscordWebhookBuilder msg = new();
        try
        {
            var rowData = await Database.Instance.GetRowData(Database.MainTableName,
	            serverId: ctx.Guild.Id,
	            channelId: playlistChannel.Id).ConfigureAwait(false);
            if (rowData is null)
            {
                msg.WithContent(
                    $"{playlistChannel.Mention} is not set up as a recommendations channel");
                await ctx.EditResponseAsync(msg).ConfigureAwait(false);
                return;
            }

            if (string.IsNullOrWhiteSpace(rowData.PlaylistId))
            {
                msg.WithContent($"{playlistChannel.Mention} has no assigned playlist");
                await ctx.EditResponseAsync(msg).ConfigureAwait(false);
                return;
            }

            var randomVideoInPlaylist = await YoutubeAPIs.Instance.GetRandomVideoInPlaylist(rowData.PlaylistId).ConfigureAwait(false);
            var vidId = randomVideoInPlaylist.Snippet.ResourceId.VideoId;
            var randomVid = $"https://www.youtube.com/watch?v={vidId}";
            msg.WithContent($"You should listen to {randomVid}");
            await ctx.EditResponseAsync(msg).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Debugger.Break();
            Log.Error(exception.ToString());
            msg = new DiscordWebhookBuilder();
            msg.WithContent(
                $"No Playlist or Videos found, if videos have been added, ask {Program.BotOwnerDiscordUser?.Mention} to check logs");
            await ctx.EditResponseAsync(msg).ConfigureAwait(false);
        }
    }

    [SlashCommand("recsplaylist", "List the playlist that is bound to the given channel")]
    public async Task GetRecsPlaylist(InteractionContext ctx,
        [Option("PlaylistChannel", "Channel that the playlist is bound to")][ChannelTypes(ChannelType.Text, ChannelType.PublicThread)] DiscordChannel playlistChannel)
    {
        await ctx.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource).ConfigureAwait(false);
        DiscordWebhookBuilder msg = new();
        try
        {
            var rowData = await Database.Instance.GetRowData(Database.MainTableName, serverId: ctx.Guild.Id,
	            channelId: playlistChannel.Id).ConfigureAwait(false);
            if (rowData is null)
            {
                msg.WithContent(
                    $"{playlistChannel.Mention} is not set up as a recommendations channel");
                await ctx.EditResponseAsync(msg).ConfigureAwait(false);
                return;
            }

            //PlaylistId should never be blank
            if (string.IsNullOrWhiteSpace(rowData.PlaylistId))
            {
                msg.WithContent(
                    $"{playlistChannel.Mention} has no assigned playlist");
                await ctx.EditResponseAsync(msg).ConfigureAwait(false);
                return;
            }

            msg.WithContent(
                $"{playlistChannel.Mention}'s playlist is {YoutubeAPIs.IdToPlaylist(rowData.PlaylistId)}");
            await ctx.EditResponseAsync(msg).ConfigureAwait(false) ;
        }
        catch (Exception exception)
        {
            Debugger.Break();
            Log.Error(exception.ToString());
            msg = new DiscordWebhookBuilder();
            msg.WithContent(
                $"Shits bad {Program.BotOwnerDiscordUser?.Mention}");
            await ctx.EditResponseAsync(msg).ConfigureAwait(false);
        }
    }


    [SlashCommandGroup("Admin", "Administrative commands")]
    public class AdminSlashCommands : ApplicationCommandModule
    {

        [SlashCommand("addplaylist", "Adds channel to watch with a new playlist. Move existing with `/admin moveplaylist`")]
        [RequireBotAdminPrivilege(Program.BotOwnerId, Permissions.Administrator)]
        public async Task AddPlaylist(InteractionContext ctx,
            [Option("BindChannel", "Channel to bind to")][ChannelTypes(ChannelType.Text, ChannelType.PublicThread)] DiscordChannel bindChannel,
            [Option("PlaylistTitle", "The title of the playlist")] string? playlistTitle = null,
            [Option("PlaylistDescription", "The description of the playlist")] string? playlistDescription = null,
            [Option("PlaylistBase", "The id of the playlist to add songs too, this MUST be a playlist created by the bot")] string? playlistId = null)
        {
            await ctx.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource).ConfigureAwait(false);
            DiscordWebhookBuilder msg = new();
            if (await Database.Instance.CheckPlaylistChannelExists(Database.MainTableName, channelId: bindChannel.Id).ConfigureAwait(false))
            {
	            msg.WithContent("There is already a playlist bound to this channel, it must be deleted or moved before adding a new one");
	            await ctx.EditResponseAsync(msg).ConfigureAwait(false);
                return;
            }
            if (bindChannel.IsThread)
            {
                DiscordThreadChannel bindThread = (DiscordThreadChannel) await Program.Discord.GetChannelAsync(bindChannel.Id).ConfigureAwait(false);
	            if (bindThread is not null)
	            {
		            await bindThread.JoinThreadAsync().ConfigureAwait(false);
	            }
            }
            if (!string.IsNullOrWhiteSpace(playlistId))
            {
                var validPlaylist = false;
                Match? playlistMatch;
                if ((playlistMatch = IReallyHopeThisPullsPlaylistIdsRegex.Match(playlistId)).Success)
                    playlistId = playlistMatch.Groups["id"].Value;

                if(string.IsNullOrWhiteSpace(playlistId))
                {
	                if (await YoutubeAPIs.Instance.GetMyPlaylistIds().AnyAsync(channelPlaylistId => channelPlaylistId == playlistId).ConfigureAwait(false))
	                {
		                validPlaylist = true;
	                }
                }
                if (!validPlaylist)
                {
                    msg.WithContent("Invalid Playlist Given. Make sure the bot created it");
                    await ctx.EditResponseAsync(msg).ConfigureAwait(false);
                    return;
                }
            }
            else
            {
	            try
	            {
		            playlistId = await YoutubeAPIs.Instance.NewPlaylist(playlistTitle, playlistDescription)
			            .ConfigureAwait(false);
		            // await Database.Instance.MakePlaylistTable(playlistId); Videos Submitted now stored in same table TODO: Learn to make tables of identical schema at runtime 
	            }
	            catch (GoogleApiException e)
	            {
		            if (e.Message.Contains("Reason[youtubeSignupRequired]"))
		            {
			            Log.Fatal("Must Sign up used google account for youtube channel");
			            msg.WithContent(
				            $"Google Account associated with bot does not have a youtube channel created, have {Program.BotOwnerDiscordUser?.Mention} create one on youtube's website");
			            await ctx.EditResponseAsync(msg).ConfigureAwait(false);
			            return;
                    }
		            else
		            {
			            throw;
		            }
                }
                catch (Exception e)
                {
                    Log.Fatal(e.ToString());
                    Debugger.Break();
                    msg = new DiscordWebhookBuilder();
                    msg.WithContent(
                        $"Failed to make a new playlist for this channel, {Program.BotOwnerDiscordUser?.Mention} any ideas?");
                    await ctx.EditResponseAsync(msg).ConfigureAwait(false);
                    return;
                }
            }

            try
            {
                await Database.Instance.InsertRow(Database.MainTableName, ctx.Guild.Id, playlistId, bindChannel.Id).ConfigureAwait(false);
                msg.WithContent($"Successfully watching channel {bindChannel.Mention} and adding to playlist {YoutubeAPIs.IdToPlaylist(playlistId)}");
            }
            catch (Exception exception)
            {
                Debugger.Break();
                Log.Error(exception.ToString());
                msg = new DiscordWebhookBuilder();
                msg.WithContent(
                    $"Failed to adds recs channel, ask {Program.BotOwnerDiscordUser?.Mention} to check logs");
            }
            finally
            {
                await ctx.EditResponseAsync(msg).ConfigureAwait(false);
            }
        }

        [SlashCommand("moveplaylist", "Moves existing watch from one channel to another")]
        [RequireBotAdminPrivilege(Program.BotOwnerId, Permissions.Administrator)]
        public async Task MovePlaylist(InteractionContext ctx,
            [Option("OriginalChannel", "Channel to move the playlist from")] [ChannelTypes(ChannelType.Text, ChannelType.PublicThread)] DiscordChannel originalChannel,
            [Option("BindChannel", "Channel to bind the playlist to")] [ChannelTypes(ChannelType.Text, ChannelType.PublicThread)] DiscordChannel bindChannel)
        {
            await ctx.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource).ConfigureAwait(false);
            DiscordWebhookBuilder msg = new();
            if (originalChannel.Equals(bindChannel))
            {
                msg.WithContent("Arguments must not be the same channel twice");
                await ctx.EditResponseAsync(msg).ConfigureAwait(false);
                return;
            }

            try
            {
                var rowData = await Database.Instance.GetRowData(Database.MainTableName, serverId: ctx.Guild.Id,
	                channelId: originalChannel.Id).ConfigureAwait(false);
                if (rowData is null)
                {
                    msg.WithContent("There is no playlist connected to this channel");
                    await ctx.EditResponseAsync(msg).ConfigureAwait(false);
                    return;
                }

                var wasChanged =
                    await Database.Instance.ChangeChannelId(Database.MainTableName, originalChannel.Id, bindChannel.Id).ConfigureAwait(false);
                if (!wasChanged) Log.Error($"Fuck, {new StackFrame().GetFileLineNumber()}");

                rowData = await Database.Instance.GetRowData(Database.MainTableName, serverId: ctx.Guild.Id,
	                channelId: bindChannel.Id).ConfigureAwait(false);
                if (rowData is null)
                {
                    msg.WithContent(
                        Program.BotOwnerDiscordUser != null
                            ? $"There is no database entry for the new channel, {Program.BotOwnerDiscordUser.Mention}'s not sure what went wrong tbh"
                            : "There is no database entry for the new channel, I'm not sure what went wrong tbh");
                    await ctx.EditResponseAsync(msg).ConfigureAwait(false);
                    return;
                }
                msg.WithContent($"Playlist {(string.IsNullOrEmpty(rowData.PlaylistId) ? "not set up but" : YoutubeAPIs.IdToPlaylist(rowData.PlaylistId))} watch successfully moved from {originalChannel.Mention} to {bindChannel.Mention}");
            }
            catch (PostgresException pgException)
            {
                Log.Error(pgException.ToString());
                Debugger.Break();
                msg = new DiscordWebhookBuilder();
                msg.WithContent($"Failed to find entry {originalChannel.Mention}. It probably is not a recs channel");
            }
            catch (Exception exception)
            {
                Log.Error(exception.ToString());
                Debugger.Break();
                msg = new DiscordWebhookBuilder();
                msg.WithContent(
                    $"Failed to modify recs channel, ask {Program.BotOwnerDiscordUser?.Mention} to check logs");
            }

            await ctx.EditResponseAsync(msg).ConfigureAwait(false);
        }

        [SlashCommand("deleteplaylist", "Remove channel watch&optionally delete playlist. Move existing with `/admin moveplaylist`")]
        [RequireBotAdminPrivilege(Program.BotOwnerId, Permissions.Administrator)]
        public async Task DeletePlaylist(InteractionContext ctx,
            [Option("WatchChannel", "Channel to remove watch from")] [ChannelTypes(ChannelType.Text, ChannelType.PublicThread)] DiscordChannel watchChannel,
            [Option("DeletePlaylist", "Should the playlist be deleted?")] bool shouldDeletePlaylist = true)
        {
            await ctx.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource).ConfigureAwait(false);
            DiscordWebhookBuilder msg = new();
            var rowData =
                await Database.Instance.GetRowData(Database.MainTableName, serverId: ctx.Guild.Id,
	                channelId: watchChannel.Id).ConfigureAwait(false);
            if (rowData is null)
            {
                msg.WithContent("There is no playlist connected to this channel");
                await ctx.EditResponseAsync(msg).ConfigureAwait(false);
                return;
            }

            var msgResponse = "";
            try
            {
                //TODO: Delete channel link but do not delete playlist
                if (await Database.Instance.DeleteRowWithServer(Database.MainTableName, ctx.Guild.Id, watchChannel.Id).ConfigureAwait(false))
                    msgResponse += $"Successfully removed watch on {watchChannel.Mention}\n";

                if (shouldDeletePlaylist)
                {
                    if (rowData.PlaylistId is null)
                    {
                        msgResponse += "No playlist to delete\n";
                    }
                    else
                    {
                        var didDeletePlaylist = await YoutubeAPIs.Instance.DeletePlaylist(rowData.PlaylistId).ConfigureAwait(false);
                        if (didDeletePlaylist)
                            msgResponse += "Successfully deleted playlist\n";
                        else
                            msgResponse +=
                                $"Failed to delete playlist {YoutubeAPIs.IdToPlaylist(rowData.PlaylistId)}\n";
                    }
                }
                else
                {
                    if (rowData.PlaylistId != null)
                        msgResponse +=
                            $"Playlist will continue to exist at {YoutubeAPIs.IdToPlaylist(rowData.PlaylistId)}. Keep this link, you will never be able to retrieve it again later\n";
                    else
                        msgResponse += "This channel had no playlist so there is nothing to save\n";
                }

                if (rowData?.PlaylistId is not null)
                {
	                ulong channelId;
	                var playlistEntries = await Database.Instance.GetPlaylistItems(rowData.PlaylistId).ConfigureAwait(false);
	                foreach (var playlistEntry in playlistEntries)
	                {
		                if (playlistEntry?.ChannelId is null)
		                {
			                if (rowData.ChannelId is null)
			                {
				                Log.Error("No Channel found, cannot delete message");
                                continue;
			                }
			                else
			                {
				                channelId = rowData.ChannelId.Value;
			                }
		                }
		                else
		                {
			                channelId = playlistEntry.ChannelId.Value;
		                }
                        await MarkNoVoting(channelId, playlistEntry!.MessageId).ConfigureAwait(false);
	                }

	                var nRows = Database.Instance.DeleteVideosSubmittedFromPlaylist(rowData.PlaylistId);
                }

                msg.WithContent(msgResponse);
            }
            catch (Exception exception)
            {
                Debugger.Break();
                Log.Error(exception.ToString());
                msgResponse +=
                    $"Failed to remove recs channel, ask {Program.BotOwnerDiscordUser?.Mention} to check logs\n";
            }
            finally
            {
                msg.WithContent(msgResponse.Trim());
                await ctx.EditResponseAsync(msg).ConfigureAwait(false);
            }
        }

        [SlashCommand("deletevideo",
	        "Removes given video from the given channel's playlist")]
        [RequireBotAdminPrivilege(Program.BotOwnerId, Permissions.Administrator)]
        public async Task DeleteVideo(InteractionContext ctx,
	        [Option("PlaylistChannel", "Channel that the playlist is bound to")]
	        [ChannelTypes(ChannelType.Text, ChannelType.PublicThread)]
	        DiscordChannel watchChannel,
	        [Option("Video", "The video to be removed from the playlist")]
	        string videoUrl)
        {
	        await ctx.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource).ConfigureAwait(false);
	        var msg = new DiscordWebhookBuilder();
	        Match? match;
	        if ((match = Program.MyHeavilyModifiedButTheBaseWasCopiedStackOverflowYouTubeRegex.Match(videoUrl)).Success &&
	            match.Groups["id"].Value is not "playlist" or "watch" or "channel" &&
	            !string.IsNullOrWhiteSpace(match.Groups["id"].Value))
	        {
		        var id = match.Groups["id"].Value;
		        var rowData = await Database.Instance.GetRowData(Database.MainTableName, serverId: ctx.Guild.Id,
			        channelId: watchChannel.Id).ConfigureAwait(false);
		        if (rowData?.PlaylistId is null)
		        {
			        msg.WithContent("Channel specified has no playlist connected, cannot delete videos from it");
			        await ctx.EditResponseAsync(msg).ConfigureAwait(false);
			        return;
		        }

		        try
		        {
			        var removeString = $"Removing from playlist {YoutubeAPIs.IdToPlaylist(rowData.PlaylistId)}\n";
			        await ctx.EditResponseAsync(msg.WithContent(removeString)).ConfigureAwait(false);

			        var vidToDelete = await Database.Instance.GetPlaylistItem(rowData.PlaylistId, videoId: id).ConfigureAwait(false);
			        if (vidToDelete is null)
			        {
				        msg.WithContent(removeString + "No Videos Found");
				        await ctx.EditResponseAsync(msg).ConfigureAwait(false);
				        return;
			        }

			        removeString += await DeleteEntryFromPlaylist(vidToDelete, rowData.PlaylistId!).ConfigureAwait(false);
			        await ctx.EditResponseAsync(msg.WithContent(removeString)).ConfigureAwait(false);
		        }
		        catch (Exception e)
		        {
			        Log.Error(e.ToString());
			        Debugger.Break();
			        msg = new DiscordWebhookBuilder();
			        msg.WithContent(
				        $"Error removing {YoutubeAPIs.IdToVideo(id)} from {YoutubeAPIs.IdToPlaylist(rowData.PlaylistId)}");
			        await ctx.EditResponseAsync(msg).ConfigureAwait(false);
		        }
	        }
	        else
	        {
		        msg.WithContent(
			        $"Cannot extract video id, url given does not match `regex\n{Program.MyHeavilyModifiedButTheBaseWasCopiedStackOverflowYouTubeRegex}`");
		        await ctx.EditResponseAsync(msg).ConfigureAwait(false);
	        }
        }
    

        [SlashCommand("deleteallfromuser",
            "Removes all videos submitted by mentioned user from the given channel's playlist")]
        [RequireBotAdminPrivilege(Program.BotOwnerId, Permissions.Administrator)]
        public async Task DeleteAllFromUser(InteractionContext ctx,
            [Option("PlaylistChannel", "Channel that the playlist is bound to")] [ChannelTypes(ChannelType.Text, ChannelType.PublicThread)] DiscordChannel watchChannel,
            [Option("User", "The user to delete all from")] DiscordUser badUser,
	        [Option("DeleteSubmissionMessages", "Delete User's Messages? If not, submissions will only be deleted from the playlist, not the channel")] bool deleteMessage = true)
        {
            await ctx.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource).ConfigureAwait(false);
            var msg = new DiscordWebhookBuilder();
            var rowData = await Database.Instance.GetRowData(Database.MainTableName, serverId: ctx.Guild.Id, channelId: watchChannel.Id).ConfigureAwait(false);
            if (rowData?.PlaylistId is null)
            {
                msg.WithContent("Channel specified has no playlist connected, cannot delete videos from it");
                await ctx.EditResponseAsync(msg).ConfigureAwait(false);
                return;
			}
            var removeString = $"Removing from playlist {YoutubeAPIs.IdToPlaylist(rowData.PlaylistId)}\n";
            await ctx.EditResponseAsync(msg.WithContent(removeString)).ConfigureAwait(false);

            var vidsToDelete = await Database.Instance.GetPlaylistItems(rowData.PlaylistId, userId: badUser.Id).ConfigureAwait(false);
            if (vidsToDelete.Count == 0)
            {
	            msg.WithContent(removeString + "No Videos Found");
	            await ctx.EditResponseAsync(msg).ConfigureAwait(false);
	            return;
            }
            foreach (var vidToDelete in vidsToDelete)
            {
	            removeString += await DeleteEntryFromPlaylist(vidToDelete, rowData.PlaylistId!).ConfigureAwait(false);
            }
            await ctx.EditResponseAsync(msg.WithContent(removeString)).ConfigureAwait(false);
        }
    }

    private static async Task MarkNoVoting(ulong channelId, ulong messageId)
    {
	    DiscordMessage disconnectedMessage;
	    try
	    {
		    disconnectedMessage = await (await Program.Discord.GetChannelAsync(channelId).ConfigureAwait(false)).GetMessageAsync(messageId).ConfigureAwait(false);
		    await disconnectedMessage.DeleteAllReactionsAsync("Playlist deleted, no longer accepting votes").ConfigureAwait(false);
		    await disconnectedMessage.CreateReactionAsync(DiscordEmoji.FromUnicode("❌")).ConfigureAwait(false);
        }
	    catch (NotFoundException)
	    {

	    }
	    catch (UnauthorizedException e)
	    {
            Debugger.Break();
            Log.Error(e.ToString());
	    }
    }
    private static async Task<string> DeleteEntryFromPlaylist(Database.VideoData entry, string playlistId, bool removeMessage = true)
    {
	    var removeString = "";
	    try
	    {
		    var vidsRemoved = await YoutubeAPIs.Instance.RemoveFromPlaylist(playlistId, entry!.VideoId).ConfigureAwait(false);
		    if (vidsRemoved > 0)
		    {
			    if (vidsRemoved == 1)
				    removeString += $"Removed {YoutubeAPIs.IdToVideo(entry.VideoId!)}\n";
			    else
			    {
				    removeString += $"Removed {vidsRemoved} videos matching {YoutubeAPIs.IdToVideo(entry.VideoId!)}\n";
			    }
		    }
		    else
		    {
			    removeString += $"{YoutubeAPIs.IdToVideo(entry.VideoId!)} not found in playlist\n";

		    }

		    var rowsDeleted = Database.Instance.DeletePlaylistItem(playlistId, messageId:entry.MessageId);
		    if (removeMessage)
		    {
			    try
			    {
				    var msgToRemove =
					    await (await Program.Discord.GetChannelAsync(entry.ChannelId!.Value).ConfigureAwait(false)).GetMessageAsync(
						    entry.MessageId).ConfigureAwait(false);
				    await msgToRemove.DeleteAsync("Entry not allowed, removed by a mod via the bot").ConfigureAwait(false);
			    }
			    catch (Exception)
			    {
                    Debugger.Break();
                    try
                    {
	                    await MarkNoVoting(entry.ChannelId!.Value, entry.MessageId).ConfigureAwait(false);
	                    removeString += $"Failed to remove message {entry.MessageId}, marked no voting instead\n";
                    }
                    catch (Exception)
                    {
	                    Debugger.Break();
                        removeString += $"Failed to remove message {entry.MessageId} and couldn't react to indicate voting disallowed\n";
                    }
			    }
		    }
		    else
		    {
			    try
			    {
				    await MarkNoVoting(entry.ChannelId!.Value, entry.MessageId).ConfigureAwait(false);
                }
			    catch (Exception)
			    {
				    // If I can't mark it, it isn't a big deal
			    }
		    }
	    }
	    catch (Exception e)
	    {
		    Log.Error(e.ToString());
		    Debugger.Break();
		    removeString +=
			    $"Error removing {YoutubeAPIs.IdToVideo(entry.VideoId!)} from {YoutubeAPIs.IdToPlaylist(playlistId)}";
	    }
	    return removeString;
    }
}