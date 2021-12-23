using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Mime;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.Util.Store;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using Serilog;

namespace DiscordMusicRecs;

// ReSharper disable once InconsistentNaming
internal class YoutubeAPIs
{
    private YoutubeAPIs()
    {
    }

    public static YoutubeAPIs Instance { get; } = new YoutubeAPIs();
    private Playlist? recommendationPlaylist;
	private YouTubeService? youTubeService;
	private bool initialized = false;
	public async Task Initialize()
	{
		UserCredential credential;
		var stream = new FileStream("client_secret_youtube.json", FileMode.Open, FileAccess.Read);
		await using (stream.ConfigureAwait(false))
		{
			credential = await GoogleWebAuthorizationBroker.AuthorizeAsync((await GoogleClientSecrets.FromStreamAsync(stream).ConfigureAwait(false)).Secrets,
				// This OAuth 2.0 access scope allows for full read/write access to the
				// authenticated user's account.
				new[] { YouTubeService.Scope.Youtube},
				"user",
				CancellationToken.None,
				new FileDataStore(GetType().ToString())
			).ConfigureAwait(false);
		}

		youTubeService = new YouTubeService(new BaseClientService.Initializer()
		{
			HttpClientInitializer = credential,
			ApplicationName = GetType().ToString() + Dns.GetHostName() + Program.Config.PostgresConfig.DbName
		});
		initialized = true;
	}

    
    public static string IdToVideo(string id, bool useYoutubeMusic = false)
    {
        return !useYoutubeMusic ? 
            $"https://youtu.be/{id}" :
            $"https://music.youtube.com/watch?v={id}";
    }

    public static string IdToPlaylist(string id, bool useYoutubeMusic = false)
    {
        return !useYoutubeMusic ? 
            $"https://youtube.com/playlist?list={id}" : 
            $"https://music.youtube.com/playlist?list={id}";
    }

    //https://developers.google.com/youtube/v3/code_samples/dotnet
	public async Task<PlaylistItem> AddToPlaylist(string videoID, string playlistId = "", string playlistName = "",
		string playlistDescription = "", bool makeNewPlaylistOnError = true, bool checkForDupes = false)
	{
		if (!initialized)
			await this.Initialize().ConfigureAwait(false);
		MakeNewPlaylist:
        Debug.Assert(youTubeService != null, nameof(youTubeService) + " != null");
        if (string.IsNullOrWhiteSpace(playlistId))
		{
			if (!makeNewPlaylistOnError)
			{
				throw new ArgumentNullException(nameof(playlistId));
			}
			playlistId = await NewPlaylist(playlistName, playlistDescription).ConfigureAwait(false);
		}
		else if(checkForDupes)
		{
			await foreach (string existingVideoId in GetVideoIdsInPlaylist(playlistId).ConfigureAwait(false))
			{
				if (existingVideoId == videoID)
                {
                    throw new Exception("Video already exists in playlist");
                }
			}
		}

		// Add a video to the playlist.
		var videoToAdd = new PlaylistItem()
		{
			Snippet = new PlaylistItemSnippet
			{
				Position = 0,
				PlaylistId = playlistId,
				ResourceId = new ResourceId
				{
					Kind = "youtube#video",
					VideoId = videoID
				}
			}
		};
		try
		{
			videoToAdd = await youTubeService.PlaylistItems.Insert(videoToAdd, "snippet").ExecuteAsync().ConfigureAwait(false);
		}
		catch (Google.GoogleApiException e)
		{
			if (e.Message.Contains("Reason[playlistNotFound]"))
			{
				playlistId = "";
				goto MakeNewPlaylist;
			}
		}

		Log.Verbose($"Playlist item id {videoToAdd.Id} (video ID {videoID}) was added to playlist id {videoToAdd.Snippet.PlaylistId} @ url https://www.youtube.com/playlist?list={videoToAdd.Snippet.PlaylistId}");
		return videoToAdd;
	}
    //TODO: Get Playlist Items from database to save on API calls
     private async IAsyncEnumerable<PlaylistItem> GetPlaylistItemsInPlaylist(string playlistId)
     {
	     if (!initialized)
             await this.Initialize().ConfigureAwait(false);
         string? pagingToken = "";
         while (pagingToken is not null)
         {
             Debug.Assert(youTubeService != null, nameof(youTubeService) + " != null");
             PlaylistItemsResource.ListRequest? playlistItemsRequest = youTubeService.PlaylistItems.List("snippet");
             playlistItemsRequest.PlaylistId = playlistId;
             playlistItemsRequest.MaxResults = 100;
             playlistItemsRequest.PageToken = pagingToken;
             PlaylistItemListResponse? playlistItems = await playlistItemsRequest.ExecuteAsync().ConfigureAwait(false);
             foreach (PlaylistItem? playlistItem in playlistItems.Items)
             {
                 yield return playlistItem;
             }
             pagingToken = playlistItems.NextPageToken;
         }
     }
	private IAsyncEnumerable<string> GetVideoIdsInPlaylist(string playlistId) => GetPlaylistItemsInPlaylist(playlistId).Select(video => video.Snippet.ResourceId.VideoId);

    public async IAsyncEnumerable<Playlist> GetMyPlaylists()
    {
        if (!initialized)
            await this.Initialize().ConfigureAwait(false);
        string? pagingToken = "";
        while (pagingToken is not null)
        {
            Debug.Assert(youTubeService != null, nameof(youTubeService) + " != null");
            PlaylistsResource.ListRequest? playlistsRequest = youTubeService.Playlists.List("snippet");
            playlistsRequest.Mine = true;
            playlistsRequest.MaxResults = 100;
            playlistsRequest.PageToken = pagingToken;
            PlaylistListResponse? playlistItems = await playlistsRequest.ExecuteAsync().ConfigureAwait(false);
            foreach (Playlist? playlistItem in playlistItems.Items)
            {
                yield return playlistItem;
            }

            pagingToken = playlistItems.NextPageToken;
        }
	}

    public IAsyncEnumerable<string> GetMyPlaylistIds() => GetMyPlaylists().Select(playlist => playlist.Id);

    public async Task<PlaylistItem> GetRandomVideoInPlaylist(string playlistId)
	{
		List<PlaylistItem> allVideos = await GetPlaylistItemsInPlaylist(playlistId).ToListAsync().ConfigureAwait(false);
		return allVideos[RandomNumberGenerator.GetInt32(allVideos.Count)]; //Upper Limit is exclusive
	}

    public const string defaultPlaylistName = "Recommendation Playlist";
    public const string defaultPlaylistDescription = "Auto-Generated Discord Recommendation Playlist";
    public async Task<string> NewPlaylist(string? playlistName = null, string? playlistDescription = null)
	{
		recommendationPlaylist = new Playlist
		{
			Snippet = new PlaylistSnippet
			{
				Title = !string.IsNullOrEmpty(playlistName) ? playlistName : defaultPlaylistName,
				Description = !string.IsNullOrEmpty(playlistDescription) ? playlistDescription : defaultPlaylistDescription
			},
			Status = new PlaylistStatus
			{
				PrivacyStatus = "public"
			}
		};
		try
        {
            Debug.Assert(youTubeService != null, nameof(youTubeService) + " != null");
            recommendationPlaylist =
				await youTubeService.Playlists.Insert(recommendationPlaylist, "snippet,status").ExecuteAsync().ConfigureAwait(false);
        }
		catch (Google.GoogleApiException e)
		{
			if (e.Message.Contains("Reason[youtubeSignupRequired]"))
			{
				Log.Error("Must Sign up used google account for youtube channel");
			}

			throw;
		}

		return recommendationPlaylist.Id;
	}

    public async Task<bool> DeletePlaylist(string playlistId)
    {
        if (string.IsNullOrWhiteSpace(playlistId))
        {
            throw new ArgumentNullException("playlistId");
        }
        string? deleteRequest = await youTubeService!.Playlists.Delete(playlistId).ExecuteAsync().ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(deleteRequest))
        {
			Log.Verbose($"Delete Request returned ${deleteRequest}");
        }

        return true;
    }

    public async Task RemovePlaylistItem(string playlistItemId)
    {
	    if (string.IsNullOrWhiteSpace(playlistItemId))
	    {
		    throw new ArgumentNullException(nameof(playlistItemId));
	    }
	    string? deleteResponse = await youTubeService!.PlaylistItems.Delete(playlistItemId).ExecuteAsync().ConfigureAwait(false);
	    if (!string.IsNullOrWhiteSpace(deleteResponse))
	    {
		    Log.Verbose($"Delete Request returned {deleteResponse}");
	    }
    }
    public async Task<int> RemoveFromPlaylist(string playlistId, string videoId)
    {
        if (string.IsNullOrWhiteSpace(playlistId))
        {
            throw new ArgumentException("playlistId is null or whitespace");
        }
        if (string.IsNullOrWhiteSpace(videoId))
        {
            throw new ArgumentException("videoId is null or whitespace");
        }

        int vidsRemoved = 0;
        await foreach (Playlist playlist in GetMyPlaylists().ConfigureAwait(false))
        {
            if (playlist.Id != playlistId) continue;
            IAsyncEnumerable<PlaylistItem> videosToRemove = GetPlaylistItemsInPlaylist(playlistId).Where(video => video.Snippet.ResourceId.VideoId == videoId);
            await foreach (PlaylistItem videoToRemove in videosToRemove.ConfigureAwait(false))
            {
                string? deleteVideooResponse = await youTubeService!.PlaylistItems.Delete(videoToRemove.Id).ExecuteAsync().ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(deleteVideooResponse))
                {
                   Log.Verbose($"Delete Request returned {deleteVideooResponse}");
                }

                ++vidsRemoved;
            }
        }

        return vidsRemoved;
    }
}