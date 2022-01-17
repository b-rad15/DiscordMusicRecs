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
using Google;
using Google.Apis.YouTube.v3.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Serilog;
using Serilog.Events;
using Timer = System.Timers.Timer;

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
    public static readonly Regex MyHeavilyModifiedButTheBaseWasCopiedStackOverflowYouTubeRegex = new(@"^((?<protocol>https?:)?\/\/)?((?<prefix>www|m|music)\.)?((?<importantPart>youtube\.com(\/(?:[\w\-]+\?(v=|[\w-=&]*?&v=)|embed\/|v\/))(?<id>[\w\-]+)(&\S+)*|youtu\.be\/(?<id>\S+?)((\/|\?).*)?))$", RegexOptions.Compiled | RegexOptions.ExplicitCapture);
    // public static readonly Regex MyHeavilyModifiedButTheBaseWasCopiedStackOverflowYouTubeRegex = new(@"^((?<protocol>https?:)?\/\/)?((?<prefix>www|m|music)\.)?((?<importantPart>youtube\.com(\/(?:[\w\-]+\?(v=|[\w-=&]*?&v=)|embed\/|v\/))(?<id>[\w\-]+)(&\S+)*|youtu\.be\/(?<id>\S+)))", RegexOptions.Compiled | RegexOptions.ExplicitCapture);
    // public static readonly Regex IShouldJustCopyStackOverflowYoutubeRegex = new(
    //     @"^((?:https?:)?\/\/)?((?:www|m|music)\.)?((?:youtube\.com|youtu\.be))(\/(?:[\w\-]+\?v=|embed\/|v\/)?)([\w\-]+)(\S+)?$", RegexOptions.Compiled);
    // public static readonly Regex IShouldJustCopyStackOverflowYoutubeMatchAnywhereInStringRegex = new(
    //     @"((?:https?:)?\/\/)?((?:www|m|music)\.)?((?:youtube\.com|youtu\.be))(\/(?:[\w\-]+\?v=|embed\/|v\/)?)([\w\-]+)(\S+)?", RegexOptions.Compiled);

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
        //Set up that requires DiscordClient Instantiated
        await HelperFunctions.RemoveOldItemsFromTimeBasedPlaylists().ConfigureAwait(false);
        Log.Information("Removed old time based entries");
        await Database.PopulateTimeBasedPlaylists().ConfigureAwait(false);
        Log.Information("Populated new time based playlist entries");

        BotOwnerDiscordUser = await Discord.GetUserAsync(BotOwnerId).ConfigureAwait(false);
        Discord.MessageCreated += OnDiscordOnMessageCreated;
        Discord.MessageReactionAdded += DiscordOnMessageReactionAdded;
        Discord.MessageReactionRemoved += DiscordOnMessageReactionAdded;
        SlashCommandsExtension? slashCommands = Discord.UseSlashCommands();
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
	    rowData ??= await Database.Instance.GetPlaylistRowData(channelId: channelId).ConfigureAwait(false);
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
				    bool didDeletePlaylist = await YoutubeAPIs.Instance.DeletePlaylist(rowData.PlaylistId).ConfigureAwait(false);
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

	    int upvoteOnlyCount = await GetNumberOfReactions(message, "👍").ConfigureAwait(false) - 1;
	    int downvoteOnlyCount = await GetNumberOfReactions(message, "👎").ConfigureAwait(false) - 1;
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
	    Database.PlaylistData? rowData = await Database.Instance.GetPlaylistRowData(channelId: message.ChannelId).ConfigureAwait(false);
	    if (rowData?.PlaylistId is null)
	    {
		    return;
	    }
	    Database.SubmittedVideoData? playlistEntryData = await Database.Instance.GetPlaylistItem(rowData.PlaylistId, messageId: message.Id).ConfigureAwait(false);
	    if (playlistEntryData is null)
	    {
		    return;
	    }

	    await UpdateVotes(message, rowData.PlaylistId).ConfigureAwait(false);

    }

    private static async Task UpdateVotes(DiscordMessage message, string playlistId)
    {
		    (short upvotes, short downvotes) = await CountVotes(message).ConfigureAwait(false);
		    await Database.Instance.UpdateVotes(messageId: message.Id, playlistId:playlistId, upvotes: upvotes, downvotes: downvotes).ConfigureAwait(false);
    }

    private static Task OnDiscordOnMessageCreated(DiscordClient sender, MessageCreateEventArgs e)
    {
        if (e.Message.Author.Id != Discord.CurrentUser.Id)
            _ = Task.Run(async () =>
            {
                bool shouldDeleteMessage = false;
                Database.PlaylistData? rowData;
                try
                {
                    rowData = await Database.Instance.GetPlaylistRowData(channelId: e.Channel.Id).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    Debugger.Break();
                    Log.Error(exception.ToString());
                    return;
                }
                if (rowData is not null)
                {
                    Match match;
                    if ((match = MyHeavilyModifiedButTheBaseWasCopiedStackOverflowYouTubeRegex.Match(e.Message.Content)).Success &&
                        match.Groups["id"].Value is not "playlist" or "watch" or "channel")
                    {
	                    string id = match.Groups["id"].Value;
                        if (string.IsNullOrEmpty(id))
                        {
                            DiscordMessage? badMessage = await Discord.SendMessageAsync(e.Channel,
	                            "Bad Recommendation, could not find video ID").ConfigureAwait(false);
                            await Task.Delay(5000).ConfigureAwait(false);
                            await badMessage.DeleteAsync().ConfigureAwait(false);
                            shouldDeleteMessage = true;
                            goto deleteMessage;
                        }

                        //Handle banned things
                        await using (Database.DiscordDatabaseContext database = new())
                        {
                            if (!await Database.IsVideoAllowed(e.Guild.Id, rowData.PlaylistId, e.Author.Id, database)
                                    .ConfigureAwait(false))
                            {
                                DiscordMessage? badMessage = await Discord.SendMessageAsync(e.Channel,
                                    "This video is banned from being posted. If you think this is a mistake, ask the mods").ConfigureAwait(false);
                                await Task.Delay(5000).ConfigureAwait(false);
                                await badMessage.DeleteAsync().ConfigureAwait(false);
                                shouldDeleteMessage = true;
                                goto deleteMessage;
                            } else if (!await Database.IsUserAllowed(e.Guild.Id, rowData.PlaylistId, e.Author.Id, database)
                                           .ConfigureAwait(false))
                            {
                                DiscordMessage? badMessage = await Discord.SendMessageAsync(e.Channel,
                                    "You are banned from posting. If you think this is a mistake, ask the mods").ConfigureAwait(false);
                                await Task.Delay(5000).ConfigureAwait(false);
                                await badMessage.DeleteAsync().ConfigureAwait(false);
                                shouldDeleteMessage = true;
                                goto deleteMessage;
                            }
                        }

                        string playlistId = rowData?.PlaylistId!;
                        bool dbSuccess = false;
                        List<string> playlistItemsAdded = new();
                        try
                        {
	                        PlaylistItem usedPlaylistItem = await YoutubeAPIs.Instance.AddToPlaylist(id, playlistId,
		                        $"{e.Guild.Name} Music Recommendation Playlist", checkForDupes:true).ConfigureAwait(false);
                            playlistItemsAdded.Add(usedPlaylistItem.Id);
	                        PlaylistItem weeklyPlaylistItem = await YoutubeAPIs.Instance.AddToPlaylist(id, rowData.WeeklyPlaylistID, makeNewPlaylistOnError: false).ConfigureAwait(false);
	                        playlistItemsAdded.Add(weeklyPlaylistItem.Id);
                            PlaylistItem monthlyPlaylistItem = await YoutubeAPIs.Instance.AddToPlaylist(id, rowData.MonthlyPlaylistID, makeNewPlaylistOnError: false).ConfigureAwait(false);
                            playlistItemsAdded.Add(monthlyPlaylistItem.Id);
                            PlaylistItem yearlyPlaylistItem = await YoutubeAPIs.Instance.AddToPlaylist(id, rowData.YearlyPlaylistID, makeNewPlaylistOnError: false).ConfigureAwait(false);
                            playlistItemsAdded.Add(yearlyPlaylistItem.Id);
                            if (usedPlaylistItem.Snippet.PlaylistId != playlistId)
	                        {
		                        await Database.Instance.ChangePlaylistId(e.Guild.Id, e.Channel.Id, usedPlaylistItem.Snippet.PlaylistId, playlistId).ConfigureAwait(false);
		                        // await Database.Instance.MakePlaylistTable(usedPlaylistItem.Snippet.PlaylistId).ConfigureAwait(false);
	                        }
                            //TODO: Move database insert before playlist add to lower chance of race conditions doubling playlist entries
	                        dbSuccess = 0 < await Database.Instance.AddVideoToPlaylistTable(usedPlaylistItem.Snippet.PlaylistId, id,
		                        usedPlaylistItem.Id, weeklyPlaylistItem.Id, monthlyPlaylistItem.Id, yearlyPlaylistItem.Id, e.Channel.Id, e.Author.Id,
		                        e.Message.Timestamp, e.Message.Id).ConfigureAwait(false);

	                        await Discord.SendMessageAsync(e.Channel, "Thanks for the Recommendation")
		                        .ConfigureAwait(false);
	                        await e.Message.CreateReactionAsync(UpvoteEmoji).ConfigureAwait(false);
	                        //TODO: Make downvotes runtime, per channel configurable
#if EnableDownvote
                            await e.Message.CreateReactionAsync(DownvoteEmoji).ConfigureAwait(false);
#endif
                        }
                        catch (GoogleApiException exception)
                        {
	                        if (exception.Error.Message == "invalid_grant" || exception.ToString().Contains("invalid_grant"))
	                        {
                                Log.Fatal("Invalid youtube token, please refresh it");
                                DiscordMessage? badMessage =
	                                await Discord.SendMessageAsync(e.Channel, "Error Adding Video, please try again in a few minutes").ConfigureAwait(false);
                                await Task.Delay(5000).ConfigureAwait(false);
                                await badMessage.DeleteAsync().ConfigureAwait(false);
                                await e.Message.DeleteAsync().ConfigureAwait(false);
                                await Discord.DisconnectAsync().ConfigureAwait(false);
                                return;
	                        }
                            //Remove any that did work
	                        foreach (string playlistItemId in playlistItemsAdded)
	                        {
		                        await YoutubeAPIs.Instance.RemovePlaylistItem(playlistItemId).ConfigureAwait(false);
	                        }
                        }
                        catch (Exception exception)
                        {
                            //Remove from Playlists
	                        foreach (string playlistItemId in playlistItemsAdded)
	                        {
		                        await YoutubeAPIs.Instance.RemovePlaylistItem(playlistItemId).ConfigureAwait(false);
	                        }
                            //Remove From Database
                            if (dbSuccess)
                            {
	                            await Database.Instance.DeleteVideoSubmitted(playlistId, messageId: e.Message.Id).ConfigureAwait(false);
                            }
                            if (exception.Message == "Video already exists in playlist")
                            {
                                DiscordMessage? badMessage =
                                    await Discord.SendMessageAsync(e.Channel, "Video already in playlist").ConfigureAwait(false);
                                await Task.Delay(5000).ConfigureAwait(false);
                                await badMessage.DeleteAsync().ConfigureAwait(false);
                                shouldDeleteMessage = true;
                            }
                            else
                            {
                                Log.Error(exception.ToString());
                                Log.Error($"Video that didn't work Id: {id} to playlist set {playlistId}");
                                DiscordMessage? badMessage = await Discord.SendMessageAsync(e.Channel, "Failed to add video to playlists").ConfigureAwait(false);
                                await Task.Delay(5000).ConfigureAwait(false);
                                await badMessage.DeleteAsync().ConfigureAwait(false);
                                shouldDeleteMessage = true;
                                throw;
                            }
                        }
                        
                        try
                        {
                            (Video? youTubeVideoDataRaw, _) = await YoutubeAPIs.Instance.GetYoutubeVideoData(id).ConfigureAwait(false);
                            if (youTubeVideoDataRaw == null)
                            {
                                Log.Error($"Youtube Api returned no data on video {id}");
                                goto deleteMessage;
                            }
                            Database.YouTubeVideo youTubeVideoData = new (id, youTubeVideoDataRaw);
                            Database.DiscordDatabaseContext database = new();
                            Database.YouTubeVideo? dbYouTubeVideoData = await database.YouTubeVideoData.Where(ytvd => ytvd.Id == id).FirstOrDefaultAsync().ConfigureAwait(false);
                            if (dbYouTubeVideoData is not null)
                            {
                                database.Remove(dbYouTubeVideoData);
                            }

                            await database.AddAsync(youTubeVideoData).ConfigureAwait(false);
                            await database.SaveChangesAsync().ConfigureAwait(false);
                        }
                        catch (Exception exception)
                        {
                            Log.Error(exception.ToString());
                        }
                    }
                    else
                    {
	                    Log.Debug($"Message \"{e.Message.Content}\" does not match {MyHeavilyModifiedButTheBaseWasCopiedStackOverflowYouTubeRegex}");
                        //TODO: replace with rowData option
                        if (removeNonUrls)
                        {
                            await e.Message.DeleteAsync().ConfigureAwait(false);
                            DiscordMessage? badMessage = await Discord.SendMessageAsync(e.Channel,
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
#if DEBUG
            .MinimumLevel.Verbose()
#else
            .MinimumLevel.Information()
#endif
            .WriteTo.Console()
		    .WriteTo.Async(a=> a.File(Path.Combine(logPath, ".log"), rollingInterval: RollingInterval.Day))
		    .CreateLogger();
	    Log.Information("Starting");
        // await Database.Instance.MakeServerTables().ConfigureAwait(false);
        // await Task.Delay(20_000);
        // var allPlaylistIds = await Database.Instance.GetAllPlaylistIds().ToListAsync(); 
        // foreach (var playlistId in allPlaylistIds)
        // {
        //     await Database.Instance.MakePlaylistTable(playlistId);
        // }
        await YoutubeAPIs.Instance.InitializeAutomatic().ConfigureAwait(false);
        Log.Information("Initialized Youtube Credentials");
        Timer dailyTimer = new()
        {
	        AutoReset = true,
	        Enabled = true,
	        Interval = TimeSpan.FromHours(1).TotalMilliseconds
        };
        dailyTimer.Start();
        dailyTimer.Elapsed += async (_, _) => await HelperFunctions.RemoveOldItemsFromTimeBasedPlaylists().ConfigureAwait(false);
        if (args.Length > 0 && args[0] is "--removeNonUrls" or "--nochatting" or "--no-chatting") removeNonUrls = true; 

        MainAsync(args).GetAwaiter().GetResult();
        // Finally, once just before the application exits...
        Log.CloseAndFlush();
    }
}