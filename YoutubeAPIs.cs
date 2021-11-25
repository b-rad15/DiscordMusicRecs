using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.Util.Store;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;

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
		await using (var stream = new FileStream("client_secret_youtube.json", FileMode.Open, FileAccess.Read))
		{
			credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
				(await GoogleClientSecrets.FromStreamAsync(stream)).Secrets,
				// This OAuth 2.0 access scope allows for full read/write access to the
				// authenticated user's account.
				new[] { YouTubeService.Scope.Youtube },
				"user",
				CancellationToken.None,
				new FileDataStore(GetType().ToString())
			);
		}

		youTubeService = new YouTubeService(new BaseClientService.Initializer()
		{
			HttpClientInitializer = credential,
			ApplicationName = GetType().ToString()
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
		public async Task<string> AddToPlaylist(string videoID, string playlistId = "", string playlistName = "", string playlistDescription = "")
	{
		if (!initialized)
			await this.Initialize();
		MakeNewPlaylist:
		// Create a new, private playlist in the authorized user's channel.
		if (string.IsNullOrWhiteSpace(playlistId))
		{
			Debug.Assert(youTubeService != null, nameof(youTubeService) + " != null");
			playlistId = await NewPlaylist(playlistName, playlistDescription);
		}
		else
		{
			await foreach (var existingVideoId in GetVideoIdsInPlaylist(playlistId))
			{
				if (existingVideoId == videoID)
				{
					return "existing";
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
			videoToAdd = await youTubeService.PlaylistItems.Insert(videoToAdd, "snippet").ExecuteAsync();
		}
		catch (Google.GoogleApiException e)
		{
			if (e.Message.Contains("Reason[playlistNotFound]"))
			{
				playlistId = "";
				goto MakeNewPlaylist;
			}
		}

		Console.WriteLine(
			$"Playlist item id {videoToAdd.Id} (video ID {videoID}) was added to playlist id {videoToAdd.Snippet.PlaylistId} @ url https://www.youtube.com/playlist?list={videoToAdd.Snippet.PlaylistId}");
		return videoToAdd.Snippet.PlaylistId;
	}

        private async IAsyncEnumerable<PlaylistItem> GetPlaylistItemsInPlaylist(string playlistId)
        {
            if (!initialized)
                await this.Initialize();
            var pagingToken = "";
            while (pagingToken is not null)
            {
                Debug.Assert(youTubeService != null, nameof(youTubeService) + " != null");
                var playlistItemsRequest = youTubeService.PlaylistItems.List("snippet");
                playlistItemsRequest.PlaylistId = playlistId;
                playlistItemsRequest.MaxResults = 100;
                playlistItemsRequest.PageToken = pagingToken;
                var playlistItems = await playlistItemsRequest.ExecuteAsync();
                foreach (var playlistItem in playlistItems.Items)
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
            await this.Initialize();
        var pagingToken = "";
        while (pagingToken is not null)
        {
            Debug.Assert(youTubeService != null, nameof(youTubeService) + " != null");
            var playlistsRequest = youTubeService.Playlists.List("snippet");
            playlistsRequest.Mine = true;
            playlistsRequest.MaxResults = 100;
            playlistsRequest.PageToken = pagingToken;
            var playlistItems = await playlistsRequest.ExecuteAsync();
            foreach (var playlistItem in playlistItems.Items)
            {
                yield return playlistItem;
            }

            pagingToken = playlistItems.NextPageToken;
        }
	}

    public IAsyncEnumerable<string> GetMyPlaylistIds() => GetMyPlaylists().Select(playlist => playlist.Id);

    public async Task<PlaylistItem> GetRandomVideoInPlaylist(string playlistId)
	{
		var allVideos = await GetPlaylistItemsInPlaylist(playlistId).ToListAsync();
		return allVideos[RandomNumberGenerator.GetInt32(allVideos.Count - 1)];
	}

	public async Task<string> NewPlaylist(string? playlistName = null, string? playlistDescription = null)
	{
		recommendationPlaylist = new Playlist
		{
			Snippet = new PlaylistSnippet
			{
				Title = !string.IsNullOrEmpty(playlistName) ? playlistName : "Recommendation Playlist",
				Description = !string.IsNullOrEmpty(playlistDescription) ? playlistDescription : "Auto-Generated Discord Recommendation Playlist"
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
				await youTubeService.Playlists.Insert(recommendationPlaylist, "snippet,status").ExecuteAsync();
        }
		catch (Google.GoogleApiException e)
		{
			if (e.Message.Contains("Reason[youtubeSignupRequired]"))
				await Console.Error.WriteLineAsync("Must Sign up used google account for youtube channel");
			throw;
		}

		return recommendationPlaylist.Id;
	}

    public async Task<bool> DeletePlaylist(string playlistId)
    {
        if (string.IsNullOrWhiteSpace(playlistId))
        {
            throw new ArgumentNullException("playlistId is null or whitespace");
        }
        var deleteRequest = await youTubeService.Playlists.Delete(playlistId).ExecuteAsync();
        if (!string.IsNullOrWhiteSpace(deleteRequest))
        {
			Console.WriteLine($"Delete Request returned ${deleteRequest}");
        }

        return true;
    }

    public async Task<int> RemoveFromPlaylist(string playlistId, string videoId)
    {
        if (string.IsNullOrWhiteSpace(playlistId))
        {
            throw new ArgumentNullException("playlistId is null or whitespace");
        }
        if (string.IsNullOrWhiteSpace(videoId))
        {
            throw new ArgumentNullException("videoId is null or whitespace");
        }

        var vidsRemoved = 0;
        await foreach (var playlist in GetMyPlaylists())
        {
            if (playlist.Id != playlistId) continue;
            var videosToRemove = GetPlaylistItemsInPlaylist(playlistId).Where(video => video.Snippet.ResourceId.VideoId == videoId);
            await foreach (var videoToRemove in videosToRemove)
            {
                var deleteVideooResponse = await youTubeService.PlaylistItems.Delete(videoToRemove.Id).ExecuteAsync();
                if (!string.IsNullOrWhiteSpace(deleteVideooResponse))
                {
                    Console.WriteLine($"Delete Request returned ${deleteVideooResponse}");
                }

                ++vidsRemoved;
            }
        }

        return vidsRemoved;
    }
}