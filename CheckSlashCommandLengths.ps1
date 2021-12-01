$files = @('./SlashCommands.cs')
foreach ($file in $files) {
    $content = Get-Content $file
    $regexMatches = [regex]::Matches($content, '(\[(SlashCommand|Option)\(\s*\"(.*?)"\s*?,\s*\"(.*?)\"\s*(,\s*\"(.*?)\"\s*)*\)\])', [System.Text.RegularExpressions.RegexOptions]::Singleline)
    foreach($match in $regexMatches){
        Write-Output $match.Value
        Write-Output $match.Groups[4].Value.Length
        if($match.Groups[4].Value.Length -gt 100){
            $error_string = "`""+$match.Groups[4].Value+"`" Description is too long, "+$match.Groups[4].Value.Length+" characters"
            Write-Output $error_string
        }
    }
}