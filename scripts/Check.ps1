param(
    [ValidateSet('Core','Desktop','All')][string]$Mode='Core',
    [string]$ReportPath
)
$ErrorActionPreference='Stop'
$sourceRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$core=@('tests/DockTests.csproj','file-tests/FileTests.csproj','layout-tests/LayoutTests.csproj')
$desktop=@('card-tests/CardTests.csproj','interaction-tests/InteractionTests.csproj','refresh-tests/RefreshTests.csproj','launch-tests/LaunchTests.csproj','activation-tests/ActivationTests.csproj','ui-tests/UiTests.csproj')
$projects=if($Mode -eq 'All'){$core+$desktop}elseif($Mode -eq 'Desktop'){$desktop}else{$core}
$results=@();$exitCode=0
Push-Location -LiteralPath $sourceRoot
try {
    foreach($project in $projects){
        Write-Host "Checking $project"
        $watch=[Diagnostics.Stopwatch]::StartNew()
        $lines=@(& dotnet run --project $project 2>&1)
        $code=$LASTEXITCODE
        $lines | ForEach-Object {Write-Host $_}
        $results+=[pscustomobject]@{Project=$project;ExitCode=$code;Seconds=[Math]::Round($watch.Elapsed.TotalSeconds,2);PassedLines=@($lines | Where-Object {"$_" -match '^PASS '}).Count}
        if($code -ne 0){$exitCode=$code;break}
    }
} catch {
    $exitCode=1
    $results+=[pscustomobject]@{Project='runner';ExitCode=1;Error=$_.Exception.Message}
    Write-Host "FAIL $($_.Exception.Message)"
} finally {
    Pop-Location
    $report=[pscustomobject]@{Timestamp=[DateTimeOffset]::Now.ToString('o');Mode=$Mode;Success=($exitCode -eq 0);Results=$results}
    if($ReportPath){
        $reportFile=[IO.Path]::GetFullPath($ReportPath)
        [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($reportFile))
        $report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $reportFile -Encoding utf8
    }
}
exit $exitCode
