using System.Diagnostics;
using System.Text.RegularExpressions;
using DSharpPlus;
using DSharpPlus.Entities;
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

    private readonly string baseHelpCommand =
        "use /setchannel <#channel-mention> to set channel where recommendations are taken\n" +
        "use /recsplaylist <#channel-mention> to get this server's recommendation playlist\n" +
        "use /randomrec <#channel-mention> to get a random recommendation from the playlist\n";

    [SlashCommand("help", "Learn how to use Music Recs")]
    public async Task HelpDiscordRecsBotCommand(InteractionContext ctx)
    {
        var msg = new DiscordWebhookBuilder();
        var msgstring = baseHelpCommand;
        try
        {
            var recsChannel =
                await Program.Discord.GetChannelAsync(
                    await Database.Instance.GetChannelId(Database.TableName, ctx.Guild.Id));
            // msg.WithContent(
            //         baseHelpCommand +
            //         $"send a youtube link in the recommendation channel {recsChannel.Mention} to recommend a song");
            msgstring +=
                $"send a youtube link in the recommendation channel {recsChannel.Mention} to recommend a song";
        }
        catch (Exception exception)
        {
            Debugger.Break();
            await Console.Error.WriteLineAsync(exception.ToString());
            // msg.WithContent(
            //         baseHelpCommand +
            //         "send a youtube link in the recommendation channel after setting up with !setchannel to recommend a song");
            msgstring +=
                "send a youtube link in the recommendation channel after setting up with !setchannel to recommend a song";
        }
        finally
        {
            var embed = new DiscordEmbedBuilder()
                .WithDescription(msgstring);
            await ctx.CreateResponseAsync(embed.Build());
        }
    }

    public static bool IsTextChannel(DiscordChannel channel)
    {
        return channel.Type is ChannelType.Text;
    }

    [SlashCommand("addplaylist",
        "Adds channel to watch with a new playlist, to move existing to new channel use /moveplaylist")]
    [RequireBotAdminPrivilege(Program.BotOwnerId, Permissions.Administrator)]
    public async Task AddPlaylist(InteractionContext ctx,
        [Option("BindChannel", "channel to bind to")] [ChannelTypes(ChannelType.Text)]
        DiscordChannel bindChannel,
        [Option("PlaylistTitle", "The title of the playlist")]
        string? playlistTitle = null,
        [Option("PlaylistDescription", "The description of the playlist")]
        string? playlistDescription = null,
        [Option("PlaylistBase", "The id of the playlist to add songs too, this MUST be a playlist created by the bot")]
        string? playlistId = null)
    {
        await ctx.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource);
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
            }
            catch (Exception exception)
            {
                await Console.Error.WriteLineAsync(exception.ToString());
                Debugger.Break();
                msg = new();
                msg.WithContent(
                    $"Failed to make a new playlist for this channel, {Program.BotOwnerDiscordUser?.Mention} any ideas?");
                await ctx.EditResponseAsync(msg);
                return;
            }
        }

        try
        {
            await Database.Instance.InsertRow(Database.TableName, ctx.Guild.Id, bindChannel.Id, playlistId);
            msg.WithContent(
                $"Successfully watching channel {bindChannel.Mention} and adding to playlist {YoutubeAPIs.IdToPlaylist(playlistId)}");
        }
        catch (Exception exception)
        {
            Debugger.Break();
            await Console.Error.WriteLineAsync(exception.ToString());
            msg = new();
            msg.WithContent(
                $"Failed to adds recs channel, ask {Program.BotOwnerDiscordUser?.Mention} to check logs");
        }
        finally
        {
            await ctx.EditResponseAsync(msg);
        }
    }

    [SlashCommand("moveplaylist",
        "Moves existing watch from one channel to another")]
    [RequireBotAdminPrivilege(Program.BotOwnerId, Permissions.Administrator)]
    public async Task MovePlaylist(InteractionContext ctx,
        [Option("OriginalChannel", "channel to move the playlist from")] [ChannelTypes(ChannelType.Text)]
        DiscordChannel originalChannel,
        [Option("BindChannel", "channel to bind the playlist to")] [ChannelTypes(ChannelType.Text)]
        DiscordChannel bindChannel)
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
            var rowData = await Database.Instance.GetRowData(Database.TableName, serverId: ctx.Guild.Id,
                channelId: originalChannel.Id);
            if (!rowData.HasValue)
            {
                msg.WithContent("There is no playlist connected to this channel");
                await ctx.EditResponseAsync(msg);
                return;
            }

            var wasChanged =
                await Database.Instance.ChangeChannelId(Database.TableName, originalChannel.Id, bindChannel.Id);
            if (!wasChanged) await Console.Error.WriteLineAsync($"Fuck, {new StackFrame().GetFileLineNumber()}");

            rowData = await Database.Instance.GetRowData(Database.TableName, serverId: ctx.Guild.Id,
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
            msg = new();
            msg.WithContent($"Failed to find entry {originalChannel.Mention}. It probably is not a recs channel");
        }
        catch (Exception exception)
        {
            await Console.Error.WriteLineAsync(exception.ToString());
            Debugger.Break();
            msg = new();
            msg.WithContent($"Failed to modify recs channel, ask {Program.BotOwnerDiscordUser?.Mention} to check logs");
        }

        await ctx.EditResponseAsync(msg);
    }

    [SlashCommand("deleteplaylist",
        "Removes channel watch, optionally delete playlist, to move existing to new channel use /moveplaylist")]
    [RequireBotAdminPrivilege(Program.BotOwnerId, Permissions.Administrator)]
    public async Task DeletePlaylist(InteractionContext ctx,
        [Option("WatchChannel", "channel to remove watch from")] [ChannelTypes(ChannelType.Text)]
        DiscordChannel watchChannel,
        [Option("DeletePlaylist", "Should the playlist be deleted?")]
        bool shouldDeletePlaylist = true)
    {
        await ctx.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource);
        DiscordWebhookBuilder msg = new();
        var rowData =
            await Database.Instance.GetRowData(Database.TableName, serverId: ctx.Guild.Id, channelId: watchChannel.Id);
        if (!rowData.HasValue)
        {
            msg.WithContent("There is no playlist connected to this channel");
            await ctx.EditResponseAsync(msg);
            return;
        }

        var msgResponse = "";
        try
        {
            if (await Database.Instance.DeleteRow(Database.TableName, ctx.Guild.Id, watchChannel.Id))
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
        "Removes channel watch, optionally delete playlist, to move existing to new channel use /moveplaylist")]
    [RequireBotAdminPrivilege(Program.BotOwnerId, Permissions.Administrator)]
    public async Task DeleteVideo(InteractionContext ctx,
        [Option("PlaylistChannel", "channel that the playlist is bound to")] [ChannelTypes(ChannelType.Text)]
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
            var rowData = await Database.Instance.GetRowData(Database.TableName, serverId: ctx.Guild.Id,
                channelId: watchChannel.Id);
            if (!rowData.HasValue || rowData.Value.playlistId is null)
            {
                msg.WithContent("Channel specified has no playlist connected, cannot delete videos from it");
                await ctx.EditResponseAsync(msg);
                return;
            }

            try
            {
                var vidsRemoved = await YoutubeAPIs.Instance.RemoveFromPlaylist(rowData.Value.playlistId, id);
                if (vidsRemoved > 0)
                {
                    msg.WithContent(
                        $"Removed {vidsRemoved} videos matching {YoutubeAPIs.IdToVideo(id)} from {YoutubeAPIs.IdToPlaylist(rowData.Value.playlistId)}");
                    await ctx.EditResponseAsync(msg);
                    return;
                }

                msg.WithContent(
                    $"{YoutubeAPIs.IdToVideo(id)} not found in {YoutubeAPIs.IdToPlaylist(rowData.Value.playlistId)}");
                await ctx.EditResponseAsync(msg);
                return;
            }
            catch (Exception e)
            {
                await Console.Error.WriteLineAsync(e.ToString());
                Debugger.Break();
                msg = new();
                msg.WithContent(
                    $"Error removing  {YoutubeAPIs.IdToVideo(id)} from {YoutubeAPIs.IdToPlaylist(rowData.Value.playlistId)}");
                await ctx.EditResponseAsync(msg);
                return;
            }
        }

        msg.WithContent(
            $"Cannot extract video id, url given does not match `regex\n{Program.iShouldJustCopyStackOverflowYoutubeRegex}`");
        await ctx.EditResponseAsync(msg);
    }

    [SlashCommand("randomrec",
        "gives a random recommendation from the specified channel's playlist")]
    public async Task RandomRec(InteractionContext ctx,
        [Option("PlaylistChannel", "channel that the playlist is bound to")] [ChannelTypes(ChannelType.Text)]
        DiscordChannel playlistChannel)
    {
        await ctx.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource);
        DiscordWebhookBuilder msg = new();
        try
        {
            var rowData = await Database.Instance.GetRowData(Database.TableName,
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

            PlaylistItem randomVideoInPlaylist = await YoutubeAPIs.Instance.GetRandomVideoInPlaylist(rowData.Value.playlistId);
            string vidId = randomVideoInPlaylist.Snippet.ResourceId.VideoId;
            var randomVid = $"https://www.youtube.com/watch?v={vidId}";
            msg.WithContent($"You should listen to {randomVid}");
            await ctx.EditResponseAsync(msg);
        }
        catch (Exception exception)
        {
            Debugger.Break();
            await Console.Error.WriteLineAsync(exception.ToString());
            msg = new();
            msg.WithContent(
                $"No Playlist or Videos found, if videos have been added, ask {Program.BotOwnerDiscordUser?.Mention} to check logs");
            await ctx.EditResponseAsync(msg);
        }
    }

    [SlashCommand("recsplaylist",
        "List the playlist that is bound to the given channel")]
    public async Task GetRecsPlaylist(InteractionContext ctx,
        [Option("PlaylistChannel", "channel that the playlist is bound to")] [ChannelTypes(ChannelType.Text)]
        DiscordChannel playlistChannel)
    {
        await ctx.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource);
        DiscordWebhookBuilder msg = new();
        try
        {
            var rowData = await Database.Instance.GetRowData(Database.TableName, serverId: ctx.Guild.Id,
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
            msg.WithContent($"{playlistChannel.Mention}'s playlist is {YoutubeAPIs.IdToPlaylist(rowData.Value.playlistId)}");
            await ctx.EditResponseAsync(msg);
        }
        catch (Exception exception)
        {
            Debugger.Break();
            await Console.Error.WriteLineAsync(exception.ToString());
            msg = new();
            msg.WithContent(
                $"Shits bad {Program.BotOwnerDiscordUser?.Mention}");
            await ctx.EditResponseAsync(msg);
        }
    }
}