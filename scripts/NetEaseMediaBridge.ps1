$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
$WarningPreference='SilentlyContinue'
[Console]::InputEncoding=New-Object Text.UTF8Encoding($false)
[Console]::OutputEncoding=New-Object Text.UTF8Encoding($false)
Add-Type -AssemblyName System.Runtime.WindowsRuntime
$managerType=[Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager,Windows.Media.Control,ContentType=WindowsRuntime]
$propsType=[Windows.Media.Control.GlobalSystemMediaTransportControlsSessionMediaProperties,Windows.Media.Control,ContentType=WindowsRuntime]
$asTask=[System.WindowsRuntimeSystemExtensions].GetMethods()|Where-Object {$_.Name -eq 'AsTask' -and $_.IsGenericMethod -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1'}|Select-Object -First 1
function Await-Result($operation,[Type]$kind){
 $pending=$asTask.MakeGenericMethod($kind).Invoke($null,@($operation))
 if(-not $pending.Wait(4500)){throw 'Media operation timeout'}
 return $pending.Result
}
$manager=$null
$coverKey='';$coverValue=''
while($null -ne ($line=[Console]::ReadLine())){
 try{
  $request=$line|ConvertFrom-Json
  if($request.command -eq 'quit'){break}
  if($request.command -notin @('poll','toggle','previous','next','seek')){throw 'Unknown media command'}
  if($null -eq $manager){$manager=Await-Result ($managerType::RequestAsync()) $managerType}
  $session=$manager.GetSessions()|Where-Object {$_.SourceAppUserModelId -match '(?i)(^|[\\/.!_-])cloudmusic(\.exe|[.!_-]|$)'}|Select-Object -First 1
  if($null -eq $session){[Console]::WriteLine('{"available":false}');continue}
  $playback=$session.GetPlaybackInfo();$controls=$playback.Controls;$timeline=$session.GetTimelineProperties()
  switch($request.command){
   'toggle'{if($controls.IsPlayPauseToggleEnabled){$null=Await-Result ($session.TryTogglePlayPauseAsync()) ([bool])}elseif($playback.PlaybackStatus.ToString() -eq 'Playing' -and $controls.IsPauseEnabled){$null=Await-Result ($session.TryPauseAsync()) ([bool])}elseif($controls.IsPlayEnabled){$null=Await-Result ($session.TryPlayAsync()) ([bool])}}
   'previous'{if($controls.IsPreviousEnabled){$null=Await-Result ($session.TrySkipPreviousAsync()) ([bool])}}
   'next'{if($controls.IsNextEnabled){$null=Await-Result ($session.TrySkipNextAsync()) ([bool])}}
   'seek'{if($controls.IsPlaybackPositionEnabled){$seconds=[double]$request.position;if([double]::IsNaN($seconds)-or[double]::IsInfinity($seconds)){throw 'Invalid position'};$duration=($timeline.EndTime-$timeline.StartTime).TotalSeconds;$seconds=[Math]::Max(0,[Math]::Min($duration,$seconds));$ticks=$timeline.StartTime.Ticks+[long]($seconds*10000000);$null=Await-Result ($session.TryChangePlaybackPositionAsync($ticks)) ([bool])}}
  }
  $props=Await-Result ($session.TryGetMediaPropertiesAsync()) $propsType
  $key=$session.SourceAppUserModelId+'|'+$props.Title+'|'+$props.Artist
  if($key -ne $coverKey){
   $coverKey=$key;$coverValue=''
   try{
    if($null -ne $props.Thumbnail){
     $stream=Await-Result ($props.Thumbnail.OpenReadAsync()) ([Windows.Storage.Streams.IRandomAccessStreamWithContentType,Windows.Storage.Streams,ContentType=WindowsRuntime])
     # Windows PowerShell may expose this interface-only WinRT result as __ComObject.
     # Invoke its declared interface rather than relying on dynamic member lookup.
     $randomType=[Windows.Storage.Streams.IRandomAccessStream,Windows.Storage.Streams,ContentType=WindowsRuntime]
     try{
      $size=[uint64]$randomType.GetProperty('Size').GetValue($stream,$null)
      if($size -gt 0 -and $size -le 262144){
       $coverInput=$randomType.GetMethod('GetInputStreamAt').Invoke($stream,@([uint64]0))
       $readerType=[Windows.Storage.Streams.DataReader,Windows.Storage.Streams,ContentType=WindowsRuntime]
       $inputType=[Windows.Storage.Streams.IInputStream,Windows.Storage.Streams,ContentType=WindowsRuntime]
       $reader=$readerType.GetConstructor(@($inputType)).Invoke(@($coverInput))
       try{$loaded=Await-Result ($reader.LoadAsync([uint32]$size)) ([uint32]);$bytes=New-Object byte[] $loaded;$reader.ReadBytes($bytes);$coverValue=[Convert]::ToBase64String($bytes)}finally{$null=$reader.DetachStream();$reader.Dispose()}
      }
     }finally{[IDisposable].GetMethod('Dispose').Invoke($stream,@())}
    }
   }catch{$coverValue=''}
  }
  $playback=$session.GetPlaybackInfo();$controls=$playback.Controls;$timeline=$session.GetTimelineProperties()
  $duration=($timeline.EndTime-$timeline.StartTime).TotalSeconds;$position=($timeline.Position-$timeline.StartTime).TotalSeconds
  if($playback.PlaybackStatus.ToString() -eq 'Playing' -and $timeline.LastUpdatedTime.Year -ge 2000){$position+=[Math]::Max(0,[Math]::Min($duration,([DateTimeOffset]::UtcNow-$timeline.LastUpdatedTime).TotalSeconds))}
  $reply=@{Available=$true;Source=$session.SourceAppUserModelId;Title=$props.Title;Artist=$props.Artist;Album=$props.AlbumTitle;State=$playback.PlaybackStatus.ToString();CanToggle=($controls.IsPlayPauseToggleEnabled -or $controls.IsPlayEnabled -or $controls.IsPauseEnabled);CanPrevious=$controls.IsPreviousEnabled;CanNext=$controls.IsNextEnabled;CanSeek=$controls.IsPlaybackPositionEnabled;Position=$position;Duration=$duration;Cover=$coverValue}
  [Console]::WriteLine(($reply|ConvertTo-Json -Compress -Depth 3))
 }catch{[Console]::WriteLine('{"available":false}');$manager=$null}
}
