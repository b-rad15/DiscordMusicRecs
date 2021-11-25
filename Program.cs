// See https://aka.ms/new-console-template for more information

using System.Diagnostics;
using System.Text.RegularExpressions;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.SlashCommands;
using Google.Apis.YouTube.v3.Data;
using Npgsql;

namespace DiscordMusicRecs;

internal class Program
{
    public const ulong BotOwnerId = 207600801928052737;

    private static Configuration? _config;


    public static DiscordClient Discord;

    // https://www.youtube.com/watch?v=vPwaXytZcgI
    // https://youtu.be/vPwaXytZcgI
    // https://music.youtube.com/watch?v=gzOdfzuFJ3E
    // Message begins with one of the above; optional http(s), www|music and must be watch link or shortlink then followed by any description message
    private static Regex youtubeRegex =
        new(@"(http(s?)://(www|music)\.)?(youtube\.com)/watch\?.*?(v=[a-zA-Z0-9_-]+)&?/?.*");

    private static Regex youtubeShortRegex = new(@"(http(s?)://(www)\.)?(youtu\.be)(/[a-zA-Z0-9_-]+)&?");

    public static readonly Regex iShouldJustCopyStackOverflowYoutubeRegex = new(
        @"^((?:https?:)?\/\/)?((?:www|m|music)\.)?((?:youtube\.com|youtu.be))(\/(?:[\w\-]+\?v=|embed\/|v\/)?)([\w\-]+)(\S+)?$");

    private static bool removeNonUrls;
    public static DiscordUser? BotOwnerDiscordUser;
    public static Configuration Config => _config ??= Configuration.ReadConfig();

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
        var slashCommands = Discord.UseSlashCommands();
        slashCommands.RegisterCommands<SlashCommands>();
        await Discord.ConnectAsync();
        await Task.Delay(-1);
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
                ulong recsChannelId;
                try
                {
                    recsChannelId = await Database.Instance.GetChannelId(Database.TableName, e.Guild.Id);
                }
                catch (Exception exception)
                {
                    Debugger.Break();
                    await Console.Error.WriteLineAsync(exception.ToString());
                    recsChannelId = ulong.MaxValue;
                }
                if (e.Channel.Id == recsChannelId)
                {
                    Match match;
                    if ((match = iShouldJustCopyStackOverflowYoutubeRegex.Match(e.Message.Content)).Success &&
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

                        var playlistId = "";
                        try
                        {
                            playlistId = await Database.Instance.GetPlaylistId(Database.TableName, e.Guild.Id);
                            success = !string.IsNullOrWhiteSpace(playlistId);
                        }
                        catch (InvalidCastException)
                        {
                            //No playlist_id in row, expected
                        }
                        catch (Exception exception)
                        {
                            Debugger.Break();
                            await Console.Error.WriteLineAsync("Non-Raised Error");
                            await Console.Error.WriteLineAsync(exception.ToString());
                        }

                        var usedPlaylistId = await YoutubeAPIs.Instance.AddToPlaylist(id, playlistId,
                            $"{e.Guild.Name} Music Recommendation Playlist");
                        switch (usedPlaylistId)
                        {
                            case "existing":
                                var badMessage =
                                    await Discord.SendMessageAsync(e.Channel, "Video already in playlist");
                                await Task.Delay(5000);
                                await badMessage.DeleteAsync();
                                shouldDeleteMessage = true;
                                break;
                            default:
                                await Discord.SendMessageAsync(e.Channel, "Thanks for the Recommendation");
                                if (usedPlaylistId != playlistId)
                                    await Database.Instance.ChangePlaylistId(Database.TableName, e.Guild.Id,
                                        e.Channel.Id, usedPlaylistId);

                                Console.WriteLine(
                                    await Database.Instance.GetChannelId(Database.TableName, e.Guild.Id));
                                break;
                        }
                    }
                    else
                    {
                        if (removeNonUrls)
                        {
                            await e.Message.DeleteAsync();
                            var badMessage = await Discord.SendMessageAsync(e.Channel,
                                $"Bad Recommendation, does not match ```regex\n{iShouldJustCopyStackOverflowYoutubeRegex}```");
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

    private async void OldSuggestion(MessageCreateEventArgs e)
    {
        // Console.WriteLine($"Message ID: {e.Message.Id}\n" +
        //                   $"Channel ID: {e.Message.ChannelId}\n" +
        //                   $"Server ID: {e.Guild.Id}\n");
        var shouldDeleteMessage = false;
        if (e.Message.Content.StartsWith("!help"))
        {
            var baseHelpCommand =
                "use !setchannel <#channel-mention> to set channel where recommendations are taken\n" +
                "use !recsplaylist to get this server's recommendation playlist\n" +
                "use !randomrec to get a random recommendation from the playlist\n";
            try
            {
                var recsChannel =
                    await Discord.GetChannelAsync(
                        await Database.Instance.GetChannelId(Database.TableName, e.Guild.Id));
                var msg = await new DiscordMessageBuilder()
                    .WithContent(
                        baseHelpCommand +
                        $"send a youtube link in the recommendation channel {recsChannel.Mention} to recommend a song")
                    .SendAsync(e.Channel);
            }
            catch (Exception exception)
            {
                Debugger.Break();
                await Console.Error.WriteLineAsync(exception.ToString());
                var msg = await new DiscordMessageBuilder()
                    .WithContent(
                        baseHelpCommand +
                        "send a youtube link in the recommendation channel after setting up with !setchannel to recommend a song")
                    .SendAsync(e.Channel);
            }
        }
        else if (e.Message.Content.StartsWith("!setchannel"))
        {
            if (e.Author.Id == 207600801928052737)
            {
                if (e.MentionedChannels.Count == 0)
                {
                    await new DiscordMessageBuilder()
                        .WithContent("Mention a channel")
                        .SendAsync(e.Channel);
                    return;
                }

                var newRecChannel = e.MentionedChannels[0];
                try
                {
                    await Database.Instance.ChangeChannelId(Database.TableName, e.Guild.Id, newRecChannel.Id);
                }
                catch (PostgresException pgException)
                {
                    await Database.Instance.InsertRow(Database.TableName, e.Guild.Id, newRecChannel.Id);
                }
                catch (Exception exception)
                {
                    Debugger.Break();
                    await Console.Error.WriteLineAsync(exception.ToString());
                    await new DiscordMessageBuilder()
                        .WithContent("Failed to modify recs channel, ask {user.Mention} to check logs")
                        .WithAllowedMentions(new IMention[] { new UserMention(207600801928052737) })
                        .SendAsync(e.Channel);
                }
            }
            else
            {
                await new DiscordMessageBuilder()
                    .WithContent("https://j.gifs.com/mZYXVp.gif")
                    .SendAsync(e.Channel);
            }
        }
        else if (e.Message.Content.StartsWith("!recsplaylist"))
        {
            try
            {
                var playlistUrl =
                    $"https://www.youtube.com/playlist?list={await Database.Instance.GetPlaylistId(Database.TableName, e.Guild.Id)}";
                await new DiscordMessageBuilder()
                    .WithContent($"This Server's Recommendation is {playlistUrl}")
                    .SendAsync(e.Channel);
            }
            catch (Exception exception)
            {
                Debugger.Break();
                await Console.Error.WriteLineAsync(exception.ToString());
                await new DiscordMessageBuilder()
                    .WithContent(
                        "No Playlist found, if videos have been added, ask {user.Mention} to check logs")
                    .WithAllowedMentions(new IMention[] { new UserMention(207600801928052737) })
                    .SendAsync(e.Channel);
            }
        }
        else if (e.Message.Content.StartsWith("!randomrec"))
        {
            try
            {
                var randomVid =
                    $"https://www.youtube.com/watch?v={await YoutubeAPIs.Instance.GetRandomVideoInPlaylist(await Database.Instance.GetPlaylistId(Database.TableName, e.Guild.Id))}";
                await new DiscordMessageBuilder()
                    .WithContent($"You should listen to {randomVid}")
                    .SendAsync(e.Channel);
            }
            catch (Exception exception)
            {
                Debugger.Break();
                await Console.Error.WriteLineAsync(exception.ToString());
                await new DiscordMessageBuilder()
                    .WithContent(
                        "No Playlist or Videos found, if videos have been added, ask {user.Mention} to check logs")
                    .WithAllowedMentions(new IMention[] { new UserMention(207600801928052737) })
                    .SendAsync(e.Channel);
            }
        }
        else
        {
            ulong recsChannelId;
            try
            {
                recsChannelId = await Database.Instance.GetChannelId(Database.TableName, e.Guild.Id);
            }
            catch (Exception exception)
            {
                Debugger.Break();
                await Console.Error.WriteLineAsync(exception.ToString());
                recsChannelId = ulong.MaxValue;
            }

            if (e.Channel.Id == recsChannelId)
            {
                Match match;
                if ((match = iShouldJustCopyStackOverflowYoutubeRegex.Match(e.Message.Content)).Success &&
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

                    var playlistId = "";
                    try
                    {
                        playlistId = await Database.Instance.GetPlaylistId(Database.TableName, e.Guild.Id);
                        success = !string.IsNullOrWhiteSpace(playlistId);
                    }
                    catch (InvalidCastException)
                    {
                        //No playlist_id in row, expected
                    }
                    catch (Exception exception)
                    {
                        Debugger.Break();
                        await Console.Error.WriteLineAsync("Non-Raised Error");
                        await Console.Error.WriteLineAsync(exception.ToString());
                    }

                    var usedPlaylistId = await YoutubeAPIs.Instance.AddToPlaylist(id, playlistId,
                        $"{e.Guild.Name} Music Recommendation Playlist");
                    switch (usedPlaylistId)
                    {
                        case "existing":
                            var badMessage =
                                await Discord.SendMessageAsync(e.Channel, "Video already in playlist");
                            await Task.Delay(5000);
                            await badMessage.DeleteAsync();
                            shouldDeleteMessage = true;
                            break;
                        default:
                            await Discord.SendMessageAsync(e.Channel, "Thanks for the Recommendation");
                            if (usedPlaylistId != playlistId)
                                await Database.Instance.ChangePlaylistId(Database.TableName, e.Guild.Id,
                                    e.Channel.Id, usedPlaylistId);

                            Console.WriteLine(
                                await Database.Instance.GetChannelId(Database.TableName, e.Guild.Id));
                            break;
                    }
                }
                else
                {
                    if (removeNonUrls)
                    {
                        await e.Message.DeleteAsync();
                        var badMessage = await Discord.SendMessageAsync(e.Channel,
                            $"Bad Recommendation, does not match ```regex\n{iShouldJustCopyStackOverflowYoutubeRegex}```");
                        await Task.Delay(5000);
                        await badMessage.DeleteAsync();
                        shouldDeleteMessage = true;
                    }
                }

                deleteMessage:
                if (shouldDeleteMessage) await e.Message.DeleteAsync();
            }
        }
    }

    private static async Task Main(string[] args)
    {
        Console.WriteLine("Starting Bot");
        await Database.Instance.MakeServerTable(Database.TableName);
        await YoutubeAPIs.Instance.Initialize();
        if (args.Length == 0 || args[0] is "--removeNonUrls" or "--nochatting" or "--no-chatting") removeNonUrls = true;
        MainAsync(args).GetAwaiter().GetResult();
    }
}