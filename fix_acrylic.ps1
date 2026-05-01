$content = Get-Content "s:\Projects\Wallpaper-Manager\src\WallpaperManager\MainWindow.xaml" -Raw
$content = $content -replace 'BackgroundSource="Backdrop" ', ''
$content | Set-Content "s:\Projects\Wallpaper-Manager\src\WallpaperManager\MainWindow.xaml" -Encoding utf8
Write-Host "Removed invalid BackgroundSource property."
