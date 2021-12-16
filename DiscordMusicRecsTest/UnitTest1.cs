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

			//Check Correct ID grabbed
			if(shouldMatch)
			{
				string id = match.Groups["id"].Value;
				Assert.Equal(expectedId, id);
			}
		}
    }
}