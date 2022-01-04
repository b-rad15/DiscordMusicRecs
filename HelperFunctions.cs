using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using Google.Apis.YouTube.v3.Data;
using Serilog;

namespace DiscordMusicRecs
{
	internal static class HelperFunctions
	{
		public static async Task<int> RemoveOldItemsFromTimeBasedPlaylists()
		{
			DateTime runTime = DateTime.Now;
			int count = 0;
			List<string> videosToRemoveWeekly = await Database.Instance.GetPlaylistItemsToRemoveWeekly(runTime).ConfigureAwait(false);
			foreach (string video in videosToRemoveWeekly)
			{
				await YoutubeAPIs.Instance.RemovePlaylistItem(video).ConfigureAwait(false);
				++count;
			}
			await Database.Instance.NullAllWeeklyPlaylistItem(runTime).ConfigureAwait(false);
			List<string> videosToRemoveMonthly = await Database.Instance.GetPlaylistItemsToRemoveMonthly(runTime).ConfigureAwait(false);
			foreach (string video in videosToRemoveMonthly)
			{
				await YoutubeAPIs.Instance.RemovePlaylistItem(video).ConfigureAwait(false);
				++count;
			}
			await Database.Instance.NullAllMonthlyPlaylistItem(runTime).ConfigureAwait(false);
			List<string> videosToRemoveYearly = await Database.Instance.GetPlaylistItemsToRemoveMonthly(runTime).ConfigureAwait(false);
			foreach (string video in videosToRemoveYearly)
			{
				await YoutubeAPIs.Instance.RemovePlaylistItem(video).ConfigureAwait(false);
				++count;
			}
			await Database.Instance.NullAllYearlyPlaylistItem(runTime).ConfigureAwait(false);
			return count;
		}

		public static async Task<int> PopulateTimeBasedPlaylists()
		{
			int count = 0;
			DateTime runTime = DateTime.Now;
			count += await PopulateWeeklyPlaylist(runTime, true).ConfigureAwait(false);
			count += await PopulateMonthlyPlaylist(runTime, true).ConfigureAwait(false);
			count += await PopulateYearlyPlaylist(runTime, true).ConfigureAwait(false);
			return count;
		}

		public static async Task<int> PopulateWeeklyPlaylist(DateTime? passedDateTime = null, bool saveDatabase = true)
		{
			DateTime runTime = passedDateTime ?? DateTime.Now;
			List<Database.PlaylistData> playlistsData = await Database.Instance.GetPlaylistItemsToAddWeekly(runTime).ConfigureAwait(false);
			int count = 0;
			foreach (Database.PlaylistData playlistData in playlistsData)
			{
				foreach (Database.VideoData videoData in playlistData.Videos)
				{
					//TODO: Allow Making new playlist on error and handle that
					PlaylistItem playlistItem = await YoutubeAPIs.Instance.AddToPlaylist(videoData.VideoId, playlistId: playlistData.WeeklyPlaylistID, makeNewPlaylistOnError: false).ConfigureAwait(false);
					videoData.WeeklyPlaylistItemId = playlistItem.Id;
					++count;
				}
			}

			int nRows = 0;
			if(saveDatabase) 
				nRows = await Database.Instance.SaveDatabase().ConfigureAwait(false);
			Log.Verbose($"Count: {count}, Rows Updated: {nRows}");
			return count;
		}
		public static async Task<int> PopulateMonthlyPlaylist(DateTime? passedDateTime = null, bool saveDatabase = true)
		{
			DateTime runTime = passedDateTime ?? DateTime.Now;
			List<Database.PlaylistData> playlistsData = await Database.Instance.GetPlaylistItemsToAddMonthly(runTime).ConfigureAwait(false);
			int count = 0;
			foreach (Database.PlaylistData playlistData in playlistsData)
			{
				foreach (Database.VideoData videoData in playlistData.Videos)
				{
					//TODO: Allow Making new playlist on error and handle that
					PlaylistItem playlistItem = await YoutubeAPIs.Instance.AddToPlaylist(videoData.VideoId, playlistId: playlistData.MonthlyPlaylistID, makeNewPlaylistOnError: false).ConfigureAwait(false);
					videoData.MonthlyPlaylistItemId = playlistItem.Id;
					++count;
				}
			}

			int nRows = 0;
			if (saveDatabase)
				nRows = await Database.Instance.SaveDatabase().ConfigureAwait(false);
			Log.Verbose($"Count: {count}, Rows Updated: {nRows}");
			return count;
		}
		public static async Task<int> PopulateYearlyPlaylist(DateTime? passedDateTime = null, bool saveDatabase = true)
		{
			DateTime runTime = passedDateTime ?? DateTime.Now;
			List<Database.PlaylistData> playlistsData = await Database.Instance.GetPlaylistItemsToAddYearly(runTime).ConfigureAwait(false);
			int count = 0;
			foreach (Database.PlaylistData playlistData in playlistsData)
			{
				foreach (Database.VideoData videoData in playlistData.Videos)
				{
					//TODO: Allow Making new playlist on error and handle that
					PlaylistItem playlistItem = await YoutubeAPIs.Instance.AddToPlaylist(videoData.VideoId, playlistId: playlistData.YearlyPlaylistID, makeNewPlaylistOnError: false).ConfigureAwait(false);
					videoData.YearlyPlaylistItemId = playlistItem.Id;
					++count;
				}
			}

			int nRows = 0;
			if (saveDatabase)
				nRows = await Database.Instance.SaveDatabase().ConfigureAwait(false);
			Log.Verbose($"Count: {count}, Rows Updated: {nRows}");
			return count;
		}
	}

	public static class DateTimeExtensions
	{
		private static readonly TimeSpan oneWeek = TimeSpan.FromDays(7);
		public static DateTime StartOfWeek(this DateTime dt, DayOfWeek startOfWeek)
		{
			int diff = (7 + (dt.DayOfWeek - startOfWeek)) % 7;
			return dt.AddDays(-1 * diff).Date;
		}

		public static DateTime EndOfWeek(this DateTime dt, DayOfWeek startOfWeek) =>
			dt.StartOfWeek(startOfWeek).AddTicks(7 * TimeSpan.TicksPerDay - 1);
		public static DateTime OneWeekAgo(this DateTime dt) => dt.Subtract(oneWeek);

		public static DateTime StartOfMonth(this DateTime dt)
		{
			return new DateTime(dt.Year, dt.Month, 1);
		}
		public static DateTime EndOfMonth(this DateTime dt)
		{
			return new DateTime(dt.Year, dt.Month + 1, 1).AddTicks(-1);
		}
		public static DateTime StartOfYear(this DateTime dt)
		{
			return new DateTime(dt.Year, 1, 1);
		}
		public static DateTime EndOfYear(this DateTime dt)
		{
			return new DateTime(dt.Year + 1, 1, 1).AddTicks(-1);
		}
	}
}
