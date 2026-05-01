$content = Get-Content "s:\Projects\Wallpaper-Manager\src\WallpaperManager\MainWindow.xaml" -Raw

$content = [System.Text.RegularExpressions.Regex]::Replace($content, 
    '(<Grid[^>]*?(?:Visibility="\{x:Bind ListPreviewVisibility\}"|Height="\{x:Bind CardPreviewHeight\}"|Visibility="\{x:Bind ThumbnailDetailsVisibility\}")[^>]*>)(\s*<Border[^>]*CornerRadius="(\d+)"[^>]*>\s*<Image Source="\{x:Bind PreviewImage\}"[^>]*/>\s*</Border>)(\s*<Grid Visibility="\{x:Bind NsfwOverlayVisibility)',
    {
        param($match)
        $gridStart = $match.Groups[1].Value
        $borderPart = $match.Groups[2].Value
        $radius = $match.Groups[3].Value
        $rest = $match.Groups[4].Value
        
        if ($gridStart -notmatch 'PointerEntered') {
            if ($gridStart.EndsWith('>')) {
                $gridStart = $gridStart.Substring(0, $gridStart.Length - 1) + ' PointerEntered="PreviewImage_PointerEntered" PointerExited="PreviewImage_PointerExited">'
            } else {
                $gridStart = $gridStart + ' PointerEntered="PreviewImage_PointerEntered" PointerExited="PreviewImage_PointerExited"'
            }
        }
        
        $blurOverlay = "
                                                    <Grid Visibility=`"{x:Bind BlurOverlayVisibility, Mode=OneWay}`">
                                                        <Border CornerRadius=`"$radius`">
                                                            <Border.Background>
                                                                <AcrylicBrush BackgroundSource=`"Backdrop`" TintColor=`"Black`" TintOpacity=`"{x:Bind CensorshipOverlayOpacity, Mode=OneWay}`" FallbackColor=`"#AA000000`" />
                                                            </Border.Background>
                                                        </Border>
                                                    </Grid>"
        
        return $gridStart + $borderPart + $blurOverlay + $rest
    }
)

$content | Set-Content "s:\Projects\Wallpaper-Manager\src\WallpaperManager\MainWindow.xaml" -Encoding utf8
Write-Host "Patched successfully!"
