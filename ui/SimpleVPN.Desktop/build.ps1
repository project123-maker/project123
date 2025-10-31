# run from ui/SimpleVPN.Desktop
"SimpleVPN.Desktop","sing-box","dotnet","MSBuild","VBCSCompiler" |
  % { Get-Process $_ -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue }

$pub = ".\bin\Release\net8.0-windows\win-x64\publish"
if (Test-Path $pub) { Remove-Item $pub -Recurse -Force -ErrorAction SilentlyContinue }

dotnet clean
dotnet publish -c Release -r win-x64 /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true

$sb = Join-Path $pub "sing-box\sing-box.exe"
$wintun = Join-Path $pub "sing-box\wintun.dll"
if (!(Test-Path $sb) -or !(Test-Path $wintun)) { throw "Missing gateway in publish." }

Start-Process "$pub\SimpleVPN.Desktop.exe"
