using System.Diagnostics;
using System.Runtime.CompilerServices;
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
using Serilog;

[assembly: InternalsVisibleTo("DiscordMusicRecsTest")]

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
    // modified from https://stackoverflow.com/questions/3717115/regular-expression-for-youtube-links
    public static readonly Regex MyHeavilyModifiedButTheBaseWasCopiedStackOverflowYouTubeRegex = new(@"^((?<protocol>https?:)?\/\/)?((?<prefix>www|m|music)\.)?((?<importantPart>youtube\.com(\/(?:[\w\-]+\?(v=|list=.*?&v=)|embed\/|v\/))(?<id>[\w\-]+)|youtu\.be\/(?<id>\S+)))$", RegexOptions.Compiled | RegexOptions.ExplicitCapture);
    // public static readonly Regex MyHeavilyModifiedButTheBaseWasCopiedStackOverflowYouTubeRegex = new(@"^((?:https?:)?\/\/)?((?:www|m|music)\.)?((?:youtube\.com(\/(?:[\w\-]+\?(v=|list=.*?&v=)|embed\/|v\/))([\w\-]+)|youtu\.be\/(\S+)))$", RegexOptions.Compiled | RegexOptions.ExplicitCapture);
    // public static readonly Regex IShouldJustCopyStackOverflowYoutubeRegex = new(
    //     @"^((?:https?:)?\/\/)?((?:www|m|music)\.)?((?:youtube\.com|youtu\.be))(\/(?:[\w\-]+\?v=|embed\/|v\/)?)([\w\-]+)(\S+)?$", RegexOptions.Compiled);
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
        BotOwnerDiscordUser = await Discord.GetUserAsync(BotOwnerId).ConfigureAwait(false);
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
        await Discord.ConnectAsync().ConfigureAwait(false);
        await Task.Delay(-1).ConfigureAwait(false);
    }

    private static async Task DiscordOnChannelDeleted(DiscordClient sender, DiscordEventArgs e)
    {
        switch (e)
        {
            case ChannelDeleteEventArgs tmp:
	            await HandleDeletedChannel(tmp.Channel.Id).ConfigureAwait(false);
                break;
            case ThreadDeleteEventArgs tmp:
	            await HandleDeletedChannel(tmp.Thread.Id).ConfigureAwait(false);
                break;
            default:
	            Log.Error("Ok which one of you added this method to another event?");
	            return;
        }
    }

    public static async Task HandleDeletedChannel(ulong channelId, bool shouldDeletePlaylist = true, Database.PlaylistData? rowData = null)
    {
	    rowData ??= await Database.Instance.GetRowData(Database.MainTableName, channelId: channelId).ConfigureAwait(false);
	    if (rowData is null)
	    {
            //No Row Exists for given channel, no playlist to delete
		    return;
	    }
	    try
	    {
		    if (await Database.Instance.DeleteRow(Database.MainTableName, channelId).ConfigureAwait(false))
		    {
			    if (shouldDeletePlaylist)
			    { 
				    var didDeletePlaylist = await YoutubeAPIs.Instance.DeletePlaylist(rowData.PlaylistId).ConfigureAwait(false);
				    if (didDeletePlaylist)
				    {
					    await Database.Instance.DeleteVideosSubmittedFromPlaylist(rowData.PlaylistId).ConfigureAwait(false);
				    }
			    }
		    }
	    }
	    catch (Exception exception)
	    {
		    Debugger.Break();
		    Log.Error(exception.ToString());
	    }
    }

    private static async Task<int> GetNumberOfReactions(DiscordMessage message, string emojiName)
    {
	    IReadOnlyCollection<DiscordUser> reactions;

        try
		{
			reactions = await message.GetReactionsAsync(DiscordEmoji.FromUnicode(emojiName)).ConfigureAwait(false);
		}
		catch (NotFoundException)
		{
			return 0;
		}

        return reactions.Count;
    }

    public static readonly DiscordEmoji UpvoteEmoji = DiscordEmoji.FromUnicode("👍");
    public static readonly DiscordEmoji DownvoteEmoji = DiscordEmoji.FromUnicode("👎");
    private static async Task<(short, short)> CountVotes(DiscordMessage message)
    {

	    var upvoteOnlyCount = await GetNumberOfReactions(message, "👍").ConfigureAwait(false) - 1;
	    var downvoteOnlyCount = await GetNumberOfReactions(message, "👎").ConfigureAwait(false) - 1;
	    return (Convert.ToInt16(upvoteOnlyCount), Convert.ToInt16(downvoteOnlyCount));
    }
    private static async Task DiscordOnMessageReactionAdded(DiscordClient sender, DiscordEventArgs e)
    {
	    DiscordMessage message;
	    switch (e)
        {
            case MessageReactionAddEventArgs mraea:
                if(mraea.User == Discord.CurrentUser)
                    return;
	            message = mraea.Message;
                break;
            case MessageReactionRemoveEventArgs mrrea:
	            if (mrrea.User == Discord.CurrentUser)
		            return;
                message = mrrea.Message;
                break;
            default:
                Log.Error("Ok which one of you added this method to another event?");
                return;
        }
	    var rowData = await Database.Instance.GetRowData(Database.MainTableName, channelId: message.ChannelId).ConfigureAwait(false);
	    if (rowData?.PlaylistId is null)
	    {
		    return;
	    }
	    var playlistEntryData = await Database.Instance.GetPlaylistItem(rowData.PlaylistId, messageId:message.Id).ConfigureAwait(false);
	    if (playlistEntryData is null)
	    {
		    return;
	    }

	    await UpdateVotes(message, rowData.PlaylistId).ConfigureAwait(false);

    }

    private static async Task UpdateVotes(DiscordMessage message, string playlistId)
    {
		    var (upvotes, downvotes) = await CountVotes(message).ConfigureAwait(false);
		    await Database.Instance.UpdateVotes(messageId: message.Id, playlistId:playlistId, upvotes: upvotes, downvotes: downvotes as short?).ConfigureAwait(false);
    }

    private static Task OnDiscordOnMessageCreated(DiscordClient sender, MessageCreateEventArgs e)
    {
        if (e.Message.Author.Id != Discord.CurrentUser.Id)
            _ = Task.Run(async () =>
            {
                var shouldDeleteMessage = false;
                ulong? recsChannelId = null;
                Database.PlaylistData? rowData = null;
                try
                {
                    rowData = await Database.Instance.GetRowData(Database.MainTableName, channelId: e.Channel.Id).ConfigureAwait(false);
                    recsChannelId = rowData?.ChannelId; //null if rowData is null otherwise channelId
                }
                catch (Exception exception)
                {
                    Debugger.Break();
                    Log.Error(exception.ToString());
                    recsChannelId = null;
                    return;
                }
                if (rowData is not null)
                {
                    Match match;
                    if ((match = MyHeavilyModifiedButTheBaseWasCopiedStackOverflowYouTubeRegex.Match(e.Message.Content)).Success &&
                        match.Groups["id"].Value is not "playlist" or "watch" or "channel")
                    {
                        var success = false;
                        var id = match.Groups["id"].Value;
                        if (string.IsNullOrEmpty(id))
                        {
                            var badMessage = await Discord.SendMessageAsync(e.Channel,
	                            "Bad Recommendation, could not find video ID").ConfigureAwait(false);
                            await Task.Delay(5000).ConfigureAwait(false);
                            await badMessage.DeleteAsync().ConfigureAwait(false);
                            shouldDeleteMessage = true;
                            goto deleteMessage;
                        }

                        var playlistId = rowData?.PlaylistId!;
                        success = !string.IsNullOrWhiteSpace(rowData?.PlaylistId);


                        try
                        {
                            var usedPlaylistItem = await YoutubeAPIs.Instance.AddToPlaylist(id, playlistId,
	                            $"{e.Guild.Name} Music Recommendation Playlist").ConfigureAwait(false);
                            await Discord.SendMessageAsync(e.Channel, "Thanks for the Recommendation").ConfigureAwait(false);
                            await e.Message.CreateReactionAsync(UpvoteEmoji).ConfigureAwait(false);
#if EnableDownvote
                            await e.Message.CreateReactionAsync(DownvoteEmoji).ConfigureAwait(false);
#endif
                            if (usedPlaylistItem.Snippet.PlaylistId != playlistId)
                            {
                                await Database.Instance.ChangePlaylistId(Database.MainTableName, e.Guild.Id,
	                                e.Channel.Id, usedPlaylistItem.Snippet.PlaylistId, playlistId).ConfigureAwait(false);
                                // await Database.Instance.MakePlaylistTable(usedPlaylistItem.Snippet.PlaylistId).ConfigureAwait(false);
                            }

                            await Database.Instance.AddVideoToPlaylistTable(usedPlaylistItem.Snippet.PlaylistId, id, usedPlaylistItem.Id, e.Channel.Id, e.Author.Id,
	                            e.Message.Timestamp, e.Message.Id).ConfigureAwait(false);
                        }
                        catch (Exception exception)
                        {
                            if (exception.Message == "Video already exists in playlist")
                            {
                                var badMessage =
                                    await Discord.SendMessageAsync(e.Channel, "Video already in playlist").ConfigureAwait(false);
                                await Task.Delay(5000).ConfigureAwait(false);
                                await badMessage.DeleteAsync().ConfigureAwait(false);
                                shouldDeleteMessage = true;
                            }
                            else
                            {
                                Log.Error(exception.ToString());
                                throw;
                            }
                        }
                    }
                    else
                    {
	                    Log.Debug($"Message \"\"");
                        if (removeNonUrls)
                        {
                            await e.Message.DeleteAsync().ConfigureAwait(false);
                            var badMessage = await Discord.SendMessageAsync(e.Channel,
	                            $"Bad Recommendation, does not match ```cs\n/{MyHeavilyModifiedButTheBaseWasCopiedStackOverflowYouTubeRegex}/```").ConfigureAwait(false);
                            await Task.Delay(5000).ConfigureAwait(false);
                            await badMessage.DeleteAsync().ConfigureAwait(false);
                            shouldDeleteMessage = true;
                        }
                    }

                    deleteMessage:
                    if (shouldDeleteMessage) await e.Message.DeleteAsync().ConfigureAwait(false);
                }
            });
        return Task.CompletedTask;
    }

    private const string logPath = "logs";
    private static async Task Main(string[] args)
    {
	    if (!Directory.Exists(logPath))
		    Directory.CreateDirectory(logPath);
	    Log.Logger = new LoggerConfiguration()
		    .WriteTo.Console()
		    .WriteTo.Async(a=>a.File(Path.Combine(logPath, ".log"), rollingInterval: RollingInterval.Day))
		    .CreateLogger();
	    Log.Information("Starting");
	    // await Database.Instance.MakeServerTables().ConfigureAwait(false);
        await Database.Instance.MakeServerTables().ConfigureAwait(false);
        // await Task.Delay(20_000);
        // var allPlaylistIds = await Database.Instance.GetAllPlaylistIds().ToListAsync(); 
        // foreach (var playlistId in allPlaylistIds)
        // {
        //     await Database.Instance.MakePlaylistTable(playlistId);
        // }
        await YoutubeAPIs.Instance.Initialize().ConfigureAwait(false);
        if (args.Length > 0 && args[0] is "--removeNonUrls" or "--nochatting" or "--no-chatting") removeNonUrls = true; 

        MainAsync(args).GetAwaiter().GetResult();
        // Finally, once just before the application exits...
        Log.CloseAndFlush();
    }
}