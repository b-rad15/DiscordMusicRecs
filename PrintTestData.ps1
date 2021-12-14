$regexString = '\[InlineData\(\"(.*?)\",.*?,.*?\)\]'
$files = @('./DiscordMusicRecsTest/UnitTest1.cs')
$success = $true;
foreach ($file in $files) {
    $content = Get-Content $file
    $regexMatches = [regex]::Matches($content, $regexString, [System.Text.RegularExpressions.RegexOptions]::Singleline)
    foreach($match in $regexMatches){
        Write-Output $match.Groups[1].Value
    }
}