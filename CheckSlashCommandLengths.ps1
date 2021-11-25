$files = @('./SlashCommands.cs')
foreach ($file in $files) {
    $content = Get-Content $file
    $matches = [regex]::Matches($content, '(\[SlashCommand\(\s*\"(.*?)"\s*?,\s*\"(.*?)\"\s*(,\s*\"(.*?)\"\s*)*\)\])', [System.Text.RegularExpressions.RegexOptions]::Singleline)
    foreach($match in $matches){
        Write-Output $match.Value
        if($match.Groups[3].Value.Length -gt 100){
            $error_string = "`""+$match.Groups[3].Value+"`" Description is too long, "+$match.Groups[3].Value.Length+" characters"
            Write-Output $error_string
        }
    }
}