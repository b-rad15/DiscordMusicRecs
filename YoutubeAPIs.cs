using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Mime;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Http;
using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.Util.Store;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using Serilog;
using JsonSerializer = System.Text.Json.JsonSerializer;

[assembly: InternalsVisibleTo("DiscordMusicRecsTest")]
namespace DiscordMusicRecs;

// ReSharper disable once InconsistentNaming
internal class YoutubeAPIs
{
	[SuppressMessage("ReSharper", "InconsistentNaming")]
	[SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "Must Match Json")]
    internal class YouTubeClientSecret
	{
		public string client_id { get; set; } = null!;
		public string project_id { get; set; } = null!;
        public string auth_uri { get; set; } = null!;
		public string token_uri { get; set; } = null!;
		public string auth_provider_x509_cert_url { get; set; } = null!;
		public string client_secret { get; set; } = null!;
		public string[] redirect_uris { get; set; } = null!;

		public static async Task<YouTubeClientSecret> ReadFromFileAsync(string secretsFilePath) =>
			await ReadFromFileAsync(new FileInfo(secretsFilePath)).ConfigureAwait(false);
		public static async Task<YouTubeClientSecret> ReadFromFileAsync(FileInfo secretsFile)
		{
			if(!secretsFile.Exists) throw new ArgumentException("Secrets File does not exist");
			if(secretsFile.Length == 0) throw new ArgumentException("Secrets File is empty");
			await using FileStream secretsFileStream = secretsFile.OpenRead();
			YouTubeClientSecret? secretsData = await JsonSerializer.DeserializeAsync<YouTubeClientSecret>(secretsFileStream).ConfigureAwait(false);
			if (secretsData == null)
			{
				throw new ArgumentException("Secrets File contained no valid data");
			}
			return secretsData;
		}
		public static YouTubeClientSecret ReadFromFile(FileInfo secretsFile)
		{
			if (!secretsFile.Exists) throw new ArgumentException("Secrets File does not exist");
			if (secretsFile.Length == 0) throw new ArgumentException("Secrets File is empty");
			YouTubeClientSecret? secretsData =
				JsonSerializer.Deserialize<YouTubeClientSecret>(File.ReadAllText(secretsFile.FullName));
			if (secretsData == null)
			{
				throw new ArgumentException("Secrets File contained no valid data");
			}
			return secretsData;
		}
	}

    private YoutubeAPIs() 
    {
    }

    public static YoutubeAPIs Instance { get; } = new();
    private Playlist? recommendationPlaylist;
	private YouTubeService? youTubeService;
	private bool initialized = false;
	private UserCredential credential = null!;
	public async Task InitializeAutomatic()
	{
		FileStream stream = new("client_secret_youtube.json", FileMode.Open, FileAccess.Read);
		await using (stream.ConfigureAwait(false))
		{
			credential = await GoogleWebAuthorizationBroker.AuthorizeAsync((await GoogleClientSecrets.FromStreamAsync(stream).ConfigureAwait(false)).Secrets,
				// This OAuth 2.0 access scope allows for full read/write access to the
				// authenticated user's account.
				new[] { YouTubeService.Scope.Youtube },
				"user",
				CancellationToken.None,
				new FileDataStore($"{GetType()}--{Dns.GetHostName()}--{Program.Config.PostgresConfig.DbName}")
			).ConfigureAwait(false);
		}
		initialized = true;
	}









	public struct UserAndAccessToken
	{
        public string User { get; }
        public string AccessToken { get; }

        public UserAndAccessToken(string user, string accessToken)
        {
	        User = user;
            AccessToken = accessToken;
        }

}

    [Obsolete("Poorly Implemented", true)]
	public async Task Initialize()
	{
		YouTubeClientSecret secretsData = await YouTubeClientSecret.ReadFromFileAsync(Program.Config.YoutubeSecretsFile).ConfigureAwait(false);
		UserAndAccessToken creds = await DoOAuthAsync(secretsData.client_id, secretsData.client_secret).ConfigureAwait(false);
		youTubeService = new YouTubeService(new BaseClientService.Initializer()
		{
			ApplicationName = $"{GetType()}--{Dns.GetHostName()}--{Program.Config.PostgresConfig.DbName}",
            DefaultExponentialBackOffPolicy = ExponentialBackOffPolicy.UnsuccessfulResponse503 | ExponentialBackOffPolicy.Exception,
            // HttpClientInitializer = new UserCredential(new GoogleAuthorizationCodeFlow(), creds.User, new TokenResponse())
		});
		initialized = true;
    }

    // ref http://stackoverflow.com/a/3978040
    public static int GetRandomUnusedPort()
	{
		TcpListener listener = new(IPAddress.Loopback, 0);
		listener.Start();
		int port = ((IPEndPoint)listener.LocalEndpoint).Port;
		listener.Stop();
		return port;
	}

	private const string AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    private static async Task<UserAndAccessToken> DoOAuthAsync(string clientId, string clientSecret, bool openInBrowser = false)
    {
        // Generates state and PKCE values.
        string state = GenerateRandomDataBase64url(32);
        string codeVerifier = GenerateRandomDataBase64url(32);
        string codeChallenge = Base64UrlEncodeNoPadding(Sha256Ascii(codeVerifier));
        const string codeChallengeMethod = "S256";

        // Creates a redirect URI using an available port on the loopback address.
        string redirectUri = $"http://{IPAddress.Loopback}:{GetRandomUnusedPort()}/";
        Log.Information("redirect URI: " + redirectUri);

        // Creates an HttpListener to listen for requests on that redirect URI.
        HttpListener http = new();
        http.Prefixes.Add(redirectUri);
        Log.Information("Listening..");
        http.Start();

        // Creates the OAuth 2.0 authorization request.
        string authorizationRequest =
	        $"{AuthorizationEndpoint}?response_type=code&scope=openid%20profile&redirect_uri={Uri.EscapeDataString(redirectUri)}&client_id={clientId}&state={state}&code_challenge={codeChallenge}&code_challenge_method={codeChallengeMethod}";

        Log.Information(authorizationRequest);
        if (openInBrowser)
        {
	        // Opens request in the browser.
	        Process.Start(authorizationRequest);
        }
        else
        {
	        Log.Information("Please click the above link and sign in");
        }

        // Waits for the OAuth authorization response.
        HttpListenerContext context = await http.GetContextAsync().ConfigureAwait(false);

#if false
        // Brings the Console to Focus.
        BringConsoleToFront();
#endif 

        // Sends an HTTP response to the browser.
        HttpListenerResponse response = context.Response;
        string responseString = "<html><head><meta http-equiv='refresh' content='10;url=https://google.com'></head><body>Please return to the app.</body></html>";
        byte[] buffer = Encoding.UTF8.GetBytes(responseString);
        response.ContentLength64 = buffer.Length;
        Stream responseOutput = response.OutputStream;
        await responseOutput.WriteAsync(buffer).ConfigureAwait(false);
        responseOutput.Close();
        http.Stop();
        Log.Information("HTTP server stopped.");

        // Checks for errors.
        string? error = context.Request.QueryString.Get("error");
        if (error != null)
        {
            Log.Error($"OAuth authorization error: {error}.");
            throw new Exception($"OAuth authorization error: {error}.");
        }

        // extracts the code
        string? code = context.Request.QueryString.Get("code");
        string? incomingState = context.Request.QueryString.Get("state");

        if (code is null || incomingState is null)
        {
            Log.Error($"Malformed authorization response. {context.Request.QueryString}");
            throw new Exception($"Malformed authorization response. {context.Request.QueryString}");
        }

        // Compares the receieved state to the expected value, to ensure that
        // this app made the request which resulted in authorization.
        if (incomingState != state)
        {
            Log.Error($"Received request with invalid state ({incomingState})");
            throw new Exception($"Received request with invalid state ({incomingState})");
        }
        Log.Information("Authorization code: " + code);

        // Starts the code exchange at the Token Endpoint.
        return await ExchangeCodeForTokensAsync(code, codeVerifier, redirectUri, clientId, clientSecret).ConfigureAwait(false);
    }

    static readonly HttpClient httpClient = new();
    private static async Task<UserAndAccessToken> ExchangeCodeForTokensAsync(string code, string codeVerifier, string redirectUri, string clientId, string clientSecret)
    {
        Log.Information("Exchanging code for tokens...");

        // builds the  request
        string tokenRequestUri = "https://www.googleapis.com/oauth2/v4/token";
        string tokenRequestBody =
	        $"code={code}&redirect_uri={Uri.EscapeDataString(redirectUri)}&client_id={clientId}&code_verifier={codeVerifier}&client_secret={clientSecret}&scope=&grant_type=authorization_code";
        string[] scopes = new String[] { YouTubeService.ScopeConstants.Youtube };
        Dictionary<string, string> tokenRequestBodyDict = new()
        {
	        { "code", code },
	        { "redirect_uri", Uri.EscapeDataString(redirectUri) },
	        { "client_id", clientId },
	        { "code_verifier", codeVerifier },
	        { "client_secret", clientSecret },
	        { "grant_type", "offline" },
	        { "scope", string.Join(' ', scopes)}
        };
        
        // send request with HttpClient
        FormUrlEncodedContent content = new(tokenRequestBodyDict);
        httpClient.DefaultRequestHeaders.Add("Accept",
	        "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        HttpResponseMessage resMessage = await httpClient.PostAsync(tokenRequestUri, content).ConfigureAwait(false);
        //Read body
        try
        {
	        string resText = await resMessage.Content.ReadAsStringAsync().ConfigureAwait(false);
	        dynamic? tokenEndpointDecoded = JsonSerializer.Deserialize<dynamic>(resText);
	        string accessToken = tokenEndpointDecoded.access_token;
	        string user = await RequestUserInfoAsync(accessToken).ConfigureAwait(false);
	        return new UserAndAccessToken(user, accessToken);
        }
        catch (WebException e)
        {
	        if (e.Status == WebExceptionStatus.ProtocolError)
	        {
		        if (e.Response is HttpWebResponse response)
		        {
			        Log.Error("HTTP: " + response.StatusCode);
			        using StreamReader reader = new(response.GetResponseStream());
			        // reads response body
			        string responseText = await reader.ReadToEndAsync().ConfigureAwait(false);
			        Log.Error(responseText);
		        }
	        }
	        throw;
        }
#if false
        // sends the request
        HttpWebRequest tokenRequest = (HttpWebRequest)WebRequest.Create(tokenRequestUri);
        tokenRequest.Method = "POST";
        tokenRequest.ContentType = "application/x-www-form-urlencoded";
        tokenRequest.Accept = "Accept=text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8";
        byte[] tokenRequestBodyBytes = Encoding.ASCII.GetBytes(tokenRequestBody);
        tokenRequest.ContentLength = tokenRequestBodyBytes.Length;
        using (Stream requestStream = tokenRequest.GetRequestStream())
        {
            await requestStream.WriteAsync(tokenRequestBodyBytes, 0, tokenRequestBodyBytes.Length).ConfigureAwait(false);
        }

        try
        {
            // gets the response
            WebResponse tokenResponse = await tokenRequest.GetResponseAsync().ConfigureAwait(false);
            using (StreamReader reader = new StreamReader(tokenResponse.GetResponseStream()))
            {
                // reads response body
                string responseText = await reader.ReadToEndAsync().ConfigureAwait(false);
                Console.WriteLine(responseText);

                // converts to dictionary
                Dictionary<string, string> tokenEndpointDecoded =
 JsonConvert.DeserializeObject<Dictionary<string, string>>(responseText);

                string accessToken = tokenEndpointDecoded["access_token"];
                await RequestUserInfoAsync(accessToken).ConfigureAwait(false);
            }
        }
        catch (WebException ex)
        {
            if (ex.Status == WebExceptionStatus.ProtocolError)
            {
                var response = ex.Response as HttpWebResponse;
                if (response != null)
                {
                    Log.Error("HTTP: " + response.StatusCode);
                    using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                    {
                        // reads response body
                        string responseText = await reader.ReadToEndAsync().ConfigureAwait(false);
                        Log.Error(responseText);
                    }
                }

            }
        }
#endif
    }

    private static async Task<string> RequestUserInfoAsync(string accessToken)
    {
        Log.Information("Making API Call to Userinfo...");

        // builds the  request
        string userinfoRequestUri = "https://www.googleapis.com/oauth2/v3/userinfo";
        httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");
        // gets the response
        HttpResponseMessage userinfoResponse = await httpClient.GetAsync(userinfoRequestUri).ConfigureAwait(false);
        // reads response body
        string userinfoResponseText = await userinfoResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
        Log.Information(userinfoResponseText);
        return userinfoResponseText;
    }

    /// <summary>
    /// Returns URI-safe data with a given input length.
    /// </summary>
    /// <param name="length">Input length (nb. output will be longer)</param>
    /// <returns></returns>
    private static string GenerateRandomDataBase64url(uint length)
    {
        byte[] bytes = new byte[length];
        RandomNumberGenerator.Fill(bytes);
        return Base64UrlEncodeNoPadding(bytes);
    }

    /// <summary>
    /// Returns the SHA256 hash of the input string, which is assumed to be ASCII.
    /// </summary>
    private static byte[] Sha256Ascii(string text)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(text);
        return SHA256.HashData(bytes);
    }
    /// <summary>
    /// Base64url no-padding encodes the given input buffer.
    /// </summary>
    /// <param name="buffer"></param>
    /// <returns></returns>
    public static string Base64UrlEncodeNoPadding(byte[] buffer)
    {
	    return Base64UrlEncoder.Encode(buffer).Replace("=","");
    }

#if false
    // Hack to bring the Console window to front.
    // ref: http://stackoverflow.com/a/12066376

    [DllImport("kernel32.dll", ExactSpelling = true)]
    public static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    public void BringConsoleToFront()
    {
        SetForegroundWindow(GetConsoleWindow());
    }
#endif








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
			await this.InitializeAutomatic().ConfigureAwait(false);

		YouTubeService youTubeService = new(new BaseClientService.Initializer()
		{
			HttpClientInitializer = credential,
			ApplicationName = $"{GetType()}--{Dns.GetHostName()}--{Program.Config.PostgresConfig.DbName}"
		});
    MakeNewPlaylist:
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
		PlaylistItem? videoToAdd = new()
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
             await this.InitializeAutomatic().ConfigureAwait(false);
	     YouTubeService youTubeService = new(new BaseClientService.Initializer()
	     {
		     HttpClientInitializer = credential,
		     ApplicationName = $"{GetType()}--{Dns.GetHostName()}--{Program.Config.PostgresConfig.DbName}"
	     });
	     string? pagingToken = "";
         while (pagingToken is not null)
         {
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
            await this.InitializeAutomatic().ConfigureAwait(false);
        YouTubeService? youTubeService = new(new BaseClientService.Initializer()
        {
	        HttpClientInitializer = credential,
	        ApplicationName = $"{GetType()}--{Dns.GetHostName()}--{Program.Config.PostgresConfig.DbName}"
        });
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
		YouTubeService? youTubeService = new(new BaseClientService.Initializer
		{
			HttpClientInitializer = credential,
			ApplicationName = $"{GetType()}--{Dns.GetHostName()}--{Program.Config.PostgresConfig.DbName}"
		});
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
            throw new ArgumentNullException(nameof(playlistId));
        }
        YouTubeService? youTubeService = new(new BaseClientService.Initializer()
        {
	        HttpClientInitializer = credential,
	        ApplicationName = $"{GetType()}--{Dns.GetHostName()}--{Program.Config.PostgresConfig.DbName}"
        });
        string? deleteRequest = await youTubeService.Playlists.Delete(playlistId).ExecuteAsync().ConfigureAwait(false);
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
        YouTubeService? youTubeService = new(new BaseClientService.Initializer()
        {
	        HttpClientInitializer = credential,
	        ApplicationName = $"{GetType()}--{Dns.GetHostName()}--{Program.Config.PostgresConfig.DbName}"
        });
        string? deleteResponse = await youTubeService.PlaylistItems.Delete(playlistItemId).ExecuteAsync().ConfigureAwait(false);
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

        YouTubeService? youTubeService = new(new BaseClientService.Initializer
        {
	        HttpClientInitializer = credential,
	        ApplicationName = $"{GetType()}--{Dns.GetHostName()}--{Program.Config.PostgresConfig.DbName}"
        });
        int vidsRemoved = 0;
        await foreach (Playlist playlist in GetMyPlaylists().ConfigureAwait(false))
        {
            if (playlist.Id != playlistId) continue;
            IAsyncEnumerable<PlaylistItem> videosToRemove = GetPlaylistItemsInPlaylist(playlistId).Where(video => video.Snippet.ResourceId.VideoId == videoId);
            await foreach (PlaylistItem videoToRemove in videosToRemove.ConfigureAwait(false))
            {
                string? deleteVideooResponse = await youTubeService.PlaylistItems.Delete(videoToRemove.Id).ExecuteAsync().ConfigureAwait(false);
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