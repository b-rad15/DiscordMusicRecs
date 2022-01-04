using System;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Xunit;

namespace DiscordMusicRecsTest
{
	public class UnitTest1
	{
		[Fact]
		public void Test1()
		{

		}
		[Theory]
		[InlineData("https://www.youtube.com/watch?v=vPwaXytZcgI", true, "vPwaXytZcgI")]
		[InlineData("http://www.youtube.com/watch?v=vPwaXytZcgI", true, "vPwaXytZcgI")]
		[InlineData("www.youtube.com/watch?v=vPwaXytZcgI", true, "vPwaXytZcgI")]
		[InlineData("https://m.youtube.com/watch?v=vPwaXytZcgI", true, "vPwaXytZcgI")]
		[InlineData("m.youtube.com/watch?v=vPwaXytZcgI", true, "vPwaXytZcgI")]
		[InlineData("https://music.youtube.com/watch?v=vPwaXytZcgI", true, "vPwaXytZcgI")]
		[InlineData("music.youtube.com/watch?v=vPwaXytZcgI", true, "vPwaXytZcgI")]
		[InlineData("https://music.youtube.com/watch?v=cfeixfgWrW8&feature=share", true, "cfeixfgWrW8")]
		[InlineData("https://youtube.com/watch?list=asbkhgklahjssfk&v=vPwaXytZcgI", true, "vPwaXytZcgI")]
		[InlineData("https://youtube.com/watch?feature=share&list=asbkhgklahjssfk&v=vPwaXytZcgI", true, "vPwaXytZcgI")]
		[InlineData("https://youtu.be/Z_Al0GXbCm8", true, "Z_Al0GXbCm8")]
		[InlineData("https://www.youtu.be/Z_Al0GXbCm8", true, "Z_Al0GXbCm8")]
		[InlineData("https://music.youtu.be/Z_Al0GXbCm8", true, "Z_Al0GXbCm8")]
		[InlineData("https://m.youtu.be/Z_Al0GXbCm8", true, "Z_Al0GXbCm8")]
		[InlineData("https://www.youtube.com/embed/IL5mHJYcE5I", true, "IL5mHJYcE5I")]
		[InlineData("https://yout.be/Z_Al0GXbCm8", false)]
		[InlineData("https://youtube/Z_Al0GXbCm8", false)]
		[InlineData("https://www.youtube.com/v=vPwaXytZcgI", false)]
		[InlineData("https://www.youtube/watch?v=vPwaXytZcgI", false)]
		[InlineData("https://open.spotify.com/track/1XyzcGhmO7iUamSS94XfqY?si=b94d9da1b7b84e13", false)]
		[InlineData("", false)]
		public void ValidateYouTubeRegex(string link, bool shouldMatch, string expectedId = "")
		{
			//Check Successful Match
			Match match = DiscordMusicRecs.Program.MyHeavilyModifiedButTheBaseWasCopiedStackOverflowYouTubeRegex.Match(link);
			//Assert 1
			Assert.Equal(shouldMatch, match.Success);

			if (!shouldMatch) return;
			//Check Correct ID grabbed
			string id = match.Groups["id"].Value;
			Assert.Equal(expectedId, id);
		}

		[Fact]
		public void TestNewBase64EncodeNoPadding()
		{
			for (int i = 8; i <= 128; i++)
			{

				byte[] bytes = new byte[i];
				RandomNumberGenerator.Fill(bytes);
				string method1 = DiscordMusicRecs.YoutubeAPIs.Base64UrlEncodeNoPadding(bytes);
				string method2 = Base64UrlEncodeNoPaddingOld(bytes);
				Assert.Equal(method2, method1);
			}
		}
		private static string Base64UrlEncodeNoPaddingOld(byte[] buffer)
		{

			string base64 = Convert.ToBase64String(buffer);

			// Converts base64 to base64url.
			base64 = base64.Replace("+", "-");
			base64 = base64.Replace("/", "_");
			// Strips padding.
			base64 = base64.Replace("=", "");

			return base64;
		}
	}
}