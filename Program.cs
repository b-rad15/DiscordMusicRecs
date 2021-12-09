// See https://aka.ms/new-console-template for more information

using System.Diagnostics;
using System.Text.RegularExpressions;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.Exceptions;
using DSharpPlus.Interactivity;
using DSharpPlus.Interactivity.Extensions;
using DSharpPlus.SlashCommands;
using Google.Apis.YouTube.v3.Data;
using Npgsql;

namespace DiscordMusicRecs;

internal class Program
{
    public const ulong BotOwnerId = 207600801928052737;

    private static Configuration? _config;


    public static DiscordClient Discord = null!;

    // https://www.youtube.com/watch?v=vPwaXytZcgI
    // https://youtu.be/vPwaXytZcgI
    // https://music.youtube.com/watch?v=gzOdfzuFJ3E
    // Message begins with one of the above; optional http(s), www|music and must be watch link or shortlink then followed by any description message
    // private static Regex youtubeRegex = new(@"(http(s?)://(www|music)\.)?(youtube\.com)/watch\?.*?(v=[a-zA-Z0-9_-]+)&?/?.*", RegexOptions.Compiled);

    // private static Regex youtubeShortRegex = new(@"(http(s?)://(www)\.)?(youtu\.be)(/[a-zA-Z0-9_-]+)&?", RegexOptions.Compiled);

    public static readonly Regex IShouldJustCopyStackOverflowYoutubeRegex = new(
        @"^((?:https?:)?\/\/)?((?:www|m|music)\.)?((?:youtube\.com|yout\.be))(\/(?:[\w\-]+\?v=|embed\/|v\/)?)([\w\-]+)(\S+)?$", RegexOptions.Compiled);
    public static readonly Regex IShouldJustCopyStackOverflowYoutubeMatchAnywhereInStringRegex = new(
        @"((?:https?:)?\/\/)?((?:www|m|music)\.)?((?:youtube\.com|youtu\.be))(\/(?:[\w\-]+\?v=|embed\/|v\/)?)([\w\-]+)(\S+)?", RegexOptions.Compiled);

    private static bool removeNonUrls = false;
    public static DiscordUser? BotOwnerDiscordUser;
    public static Configuration Config => _config ??= Configuration.ReadConfig();

    public static InteractivityExtension Interactivity = null!;
    private static async Task MainAsync(string[] args)
    {
        Discord = new DiscordClient(new DiscordConfiguration
        {
            Token = Config.Token,
            TokenType = TokenType.Bot,
            Intents = DiscordIntents.AllUnprivileged
        });
        BotOwnerDiscordUser = await Discord.GetUserAsync(BotOwnerId);
        Discord.MessageCreated += OnDiscordOnMessageCreated;
        Discord.MessageReactionAdded += DiscordOnMessageReactionAdded;
        Discord.MessageReactionRemoved += DiscordOnMessageReactionAdded;
        var slashCommands = Discord.UseSlashCommands();
        Interactivity = Discord.UseInteractivity(new InteractivityConfiguration
        {
            AckPaginationButtons = true
        });
        slashCommands.RegisterCommands<SlashCommands>();
        Discord.ChannelDeleted += DiscordOnChannelDeleted;
        Discord.ThreadDeleted += DiscordOnChannelDeleted;
        await Discord.ConnectAsync();
        await Task.Delay(-1);
    }

    private static async Task DiscordOnChannelDeleted(DiscordClient sender, DiscordEventArgs e)
    {
        switch (e)
        {
            case ChannelDeleteEventArgs tmp:
	            await HandleDeletedChannel(tmp.Channel.Id);
                break;
            case ThreadDeleteEventArgs tmp:
	            await HandleDeletedChannel(tmp.Thread.Id);
                break;
            default:
	            await Console.Error.WriteLineAsync("Ok which one of you added this method to another event?");
	            return;
        }
    }

    public static async Task HandleDeletedChannel(ulong channelId, bool shouldDeletePlaylist = true, Database.MainData? rowData = null)
    {
	    rowData ??= await Database.Instance.GetRowData(Database.MainTableName, channelId: channelId);
	    if (!rowData.HasValue)
	    {
		    return;
	    }
	    try
	    {
		    if (await Database.Instance.DeleteRow(Database.MainTableName, channelId))
		    {
			    if (shouldDeletePlaylist)
			    {
				    if (rowData.Value.playlistId is not null)
				    {
					    var didDeletePlaylist = await YoutubeAPIs.Instance.DeletePlaylist(rowData.Value.playlistId);
				    }
			    }
		    }
	    }
	    catch (Exception exception)
	    {
		    Debugger.Break();
		    await Console.Error.WriteLineAsync(exception.ToString());
	    }
    }

    private static async Task<int> GetNumberOfReactions(DiscordMessage message, string emojiName)
    {
	    IReadOnlyCollection<DiscordUser> reactions;

        try
		{
			reactions = await message.GetReactionsAsync(DiscordEmoji.FromUnicode(emojiName));
		}
		catch (NotFoundException)
		{
			return 0;
		}

        return reactions.Count;
    }

    public static readonly DiscordEmoji UpvoteEmoji = DiscordEmoji.FromUnicode("👍");
    public static readonly DiscordEmoji DownvoteEmoji = DiscordEmoji.FromUnicode("👎");
    private static async Task<(int, int)> CountVotes(DiscordMessage message)
    {
	    // var reactions = message.Reactions;
	    // int upvotes = 0;
	    // int downvotes = 0;
	    // foreach (var reaction in reactions)
	    // {
		   //  if (reaction.Emoji == UpvoteEmoji)
		   //  {
			  //   ++upvotes;
		   //  }
		   //  else if(reaction.Emoji == DownvoteEmoji)
		   //  {
			  //   ++downvotes;
		   //  }
	    // }

	    var upvoteOnlycount = await GetNumberOfReactions(message, "👍");
	    var downvoteOnlycount = await GetNumberOfReactions(message, "👎");
	    return (upvoteOnlycount, downvoteOnlycount);
    }
    private static async Task DiscordOnMessageReactionAdded(DiscordClient sender, DiscordEventArgs e)
    {
	    DiscordMessage message;
	    switch (e)
        {
            case MessageReactionAddEventArgs mraea:
	            message = mraea.Message;
                break;
            case MessageReactionRemoveEventArgs mrrea:
	            message = mrrea.Message;
                break;
            default:
                await Console.Error.WriteLineAsync("Ok which one of you added this method to another event?");
                return;
        }
	    var rowData = await Database.Instance.GetRowData(Database.MainTableName, channelId: message.ChannelId);
	    if (rowData?.playlistId is null)
	    {
		    return;
	    }
	    var playlistEntryData = await Database.Instance.GetPlaylistItem(rowData.Value.playlistId, messageId:message.Id);
	    if (playlistEntryData is null)
	    {
		    return;
	    }

	    await UpdateVotes(message, rowData.Value.playlistId);

    }

    private static async Task UpdateVotes(DiscordMessage message, string playlistId)
    {
		    var (upvotes, downvotes) = await CountVotes(message);
		    await Database.Instance.UpdateVotes(messageId: message.Id, playlistId:playlistId, upvotes:upvotes, downvotes:downvotes);
    }

    private static Task OnDiscordOnMessageCreated(DiscordClient sender, MessageCreateEventArgs e)
    {
        if (e.Message.Author.Id != Discord.CurrentUser.Id)
            _ = Task.Run(async () =>
            {
                // Console.WriteLine($"Message ID: {e.Message.Id}\n" +
                //                   $"Channel ID: {e.Message.ChannelId}\n" +
                //                   $"Server ID: {e.Guild.Id}\n");
                var shouldDeleteMessage = false;
                ulong? recsChannelId = null;
                Database.MainData? rowData = null;
                try
                {
                    rowData = await Database.Instance.GetRowData(Database.MainTableName, channelId: e.Channel.Id);
                    recsChannelId = rowData?.channelId; //null if rowData is null otherwise channelId
                }
                catch (Exception exception)
                {
                    Debugger.Break();
                    await Console.Error.WriteLineAsync(exception.ToString());
                    recsChannelId = null;
                    return;
                }
                if (rowData.HasValue)
                {
                    Match match;
                    if ((match = IShouldJustCopyStackOverflowYoutubeRegex.Match(e.Message.Content)).Success &&
                        match.Groups[5].Value is not "playlist" or "watch" or "channel")
                    {
                        var success = false;
                        var id = match.Groups[5].Value;
                        if (string.IsNullOrEmpty(id))
                        {
                            var badMessage = await Discord.SendMessageAsync(e.Channel,
                                "Bad Recommendation, could not find video ID");
                            await Task.Delay(5000);
                            await badMessage.DeleteAsync();
                            shouldDeleteMessage = true;
                            goto deleteMessage;
                        }

                        var playlistId = rowData?.playlistId!;
                        success = !string.IsNullOrWhiteSpace(rowData?.playlistId);


                        try
                        {
                            var usedPlaylistItem = await YoutubeAPIs.Instance.AddToPlaylist(id, playlistId,
                                $"{e.Guild.Name} Music Recommendation Playlist");
                            await Discord.SendMessageAsync(e.Channel, "Thanks for the Recommendation");
                            await e.Message.CreateReactionAsync(UpvoteEmoji);
                            await e.Message.CreateReactionAsync(DownvoteEmoji);
                            if (usedPlaylistItem.Snippet.PlaylistId != playlistId)
                            {
                                await Database.Instance.ChangePlaylistId(Database.MainTableName, e.Guild.Id,
                                    e.Channel.Id, usedPlaylistItem.Snippet.PlaylistId);
                                await Database.Instance.MakePlaylistTable(usedPlaylistItem.Snippet.PlaylistId);
                            }

                            await Database.Instance.AddVideoToPlaylistTable(usedPlaylistItem.Snippet.PlaylistId, id, usedPlaylistItem.Id, e.Channel.Id, e.Author.Id,
                                e.Message.Timestamp, e.Message.Id);
                        }
                        catch (Exception exception)
                        {
                            if (exception.Message == "Video already exists in playlist")
                            {
                                var badMessage =
                                    await Discord.SendMessageAsync(e.Channel, "Video already in playlist");
                                await Task.Delay(5000);
                                await badMessage.DeleteAsync();
                                shouldDeleteMessage = true;
                            }
                            else
                            {
                                await Console.Error.WriteLineAsync(exception.ToString());
                                throw;
                            }
                        }
                    }
                    else
                    {
                        if (removeNonUrls)
                        {
                            await e.Message.DeleteAsync();
                            var badMessage = await Discord.SendMessageAsync(e.Channel,
                                $"Bad Recommendation, does not match ```javascript\n/{IShouldJustCopyStackOverflowYoutubeRegex}/```");
                            await Task.Delay(5000);
                            await badMessage.DeleteAsync();
                            shouldDeleteMessage = true;
                        }
                    }

                    deleteMessage:
                    if (shouldDeleteMessage) await e.Message.DeleteAsync();
                }
            });
        return Task.CompletedTask;
    }

    private static async Task Main(string[] args)
    {
        Console.WriteLine("Starting Bot");
        await Database.Instance.MakeServerTables();
        // await Task.Delay(20_000);
        var allPlaylistIds = await Database.Instance.GetAllPlaylistIds().ToListAsync(); 
        foreach (var playlistId in allPlaylistIds)
        {
            await Database.Instance.MakePlaylistTable(playlistId);
        }
        await YoutubeAPIs.Instance.Initialize();
        if (args.Length > 0 && args[0] is "--removeNonUrls" or "--nochatting" or "--no-chatting") removeNonUrls = true;
        MainAsync(args).GetAwaiter().GetResult();
    }
}