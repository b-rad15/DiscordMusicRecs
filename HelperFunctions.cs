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
			List<string?> videosToRemoveWeekly = await Database.Instance.GetPlaylistItemsToRemoveWeekly(runTime).ConfigureAwait(false);
			foreach (string video in videosToRemoveWeekly)
			{
				await YoutubeAPIs.Instance.RemovePlaylistItem(video).ConfigureAwait(false);
				++count;
			}
			await Database.Instance.NullAllWeeklyPlaylistItem(runTime).ConfigureAwait(false);
			List<string?> videosToRemoveMonthly = await Database.Instance.GetPlaylistItemsToRemoveMonthly(runTime).ConfigureAwait(false);
			foreach (string video in videosToRemoveMonthly)
			{
				await YoutubeAPIs.Instance.RemovePlaylistItem(video).ConfigureAwait(false);
				++count;
			}
			await Database.Instance.NullAllMonthlyPlaylistItem(runTime).ConfigureAwait(false);
			List<string?> videosToRemoveYearly = await Database.Instance.GetPlaylistItemsToRemoveYearly(runTime).ConfigureAwait(false);
			foreach (string video in videosToRemoveYearly)
			{
				await YoutubeAPIs.Instance.RemovePlaylistItem(video).ConfigureAwait(false);
				++count;
			}
			await Database.Instance.NullAllYearlyPlaylistItem(runTime).ConfigureAwait(false);
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
			return new DateTime(dt.Year, dt.Month, 1, 0, 0, 0, dt.Kind);
		}
		public static DateTime EndOfMonth(this DateTime dt)
		{
			return new DateTime(dt.Year, dt.Month + 1, 1, 0, 0, 0, dt.Kind).AddTicks(-1);
		}
		public static DateTime StartOfYear(this DateTime dt)
		{
			return new DateTime(dt.Year, 1, 1, 0, 0, 0, dt.Kind);
		}
		public static DateTime EndOfYear(this DateTime dt)
		{
			return new DateTime(dt.Year + 1, 1, 1, 0, 0, 0, dt.Kind).AddTicks(-1);
		}
	}
}
