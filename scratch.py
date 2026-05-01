import re

file_path = r"s:\Projects\Wallpaper-Manager\src\WallpaperManager\MainWindow.xaml"
with open(file_path, "r", encoding="utf-8") as f:
    content = f.read()

def replace_grid(match):
    grid_start = match.group(1)
    border_part = match.group(2)
    radius = match.group(3)
    rest = match.group(4)
    
    if "PointerEntered" not in grid_start:
        if grid_start.endswith(">"):
            grid_start = grid_start[:-1] + ' PointerEntered="PreviewImage_PointerEntered" PointerExited="PreviewImage_PointerExited">'
        else:
            grid_start = grid_start + ' PointerEntered="PreviewImage_PointerEntered" PointerExited="PreviewImage_PointerExited"'
            
    blur_overlay = f'''
                                                    <Grid Visibility="{{x:Bind BlurOverlayVisibility, Mode=OneWay}}">
                                                        <Border CornerRadius="{radius}">
                                                            <Border.Background>
                                                                <AcrylicBrush BackgroundSource="Backdrop" TintColor="Black" TintOpacity="{{x:Bind CensorshipOverlayOpacity, Mode=OneWay}}" FallbackColor="#AA000000" />
                                                            </Border.Background>
                                                        </Border>
                                                    </Grid>'''
    
    return f"{grid_start}{border_part}{blur_overlay}{rest}"

content = re.sub(
    r'(<Grid[^>]*?(?:Visibility="\{x:Bind ListPreviewVisibility\}"|Height="\{x:Bind CardPreviewHeight\}"|Visibility="\{x:Bind ThumbnailDetailsVisibility\}")[^>]*>)(\s*<Border[^>]*CornerRadius="(\d+)"[^>]*>\s*<Image Source="\{x:Bind PreviewImage\}"[^>]*/>\s*</Border>)(\s*<Grid Visibility="\{x:Bind NsfwOverlayVisibility)',
    replace_grid,
    content
)

content = re.sub(
    r'(<Grid>)(\s*<Image Source="ms-appx:///Assets/StoreLogo\.png"[^>]*/>)(\s*<Grid Visibility="\{x:Bind NsfwPreviewItem\.NsfwOverlayVisibility)',
    r'\1\2\n                                                            <Grid Visibility="{x:Bind NsfwPreviewItem.BlurOverlayVisibility, Mode=OneWay}"><Border CornerRadius="6"><Border.Background><AcrylicBrush BackgroundSource="Backdrop" TintColor="Black" TintOpacity="{x:Bind NsfwPreviewItem.CensorshipOverlayOpacity, Mode=OneWay}" FallbackColor="#AA000000" /></Border.Background></Border></Grid>\3',
    content
)
content = re.sub(
    r'(<Grid>)(\s*<Image Source="ms-appx:///Assets/StoreLogo\.png"[^>]*/>)(\s*<Grid Visibility="\{x:Bind MaturePreviewItem\.MatureOverlayVisibility)',
    r'\1\2\n                                                            <Grid Visibility="{x:Bind MaturePreviewItem.BlurOverlayVisibility, Mode=OneWay}"><Border CornerRadius="6"><Border.Background><AcrylicBrush BackgroundSource="Backdrop" TintColor="Black" TintOpacity="{x:Bind MaturePreviewItem.CensorshipOverlayOpacity, Mode=OneWay}" FallbackColor="#AA000000" /></Border.Background></Border></Grid>\3',
    content
)

toggle_xml = '''
                                    <Grid Padding="18,14" Background="{ThemeResource CardBackgroundFillColorDefaultBrush}" ColumnSpacing="18"><Grid.ColumnDefinitions><ColumnDefinition Width="32" /><ColumnDefinition Width="*" /><ColumnDefinition Width="Auto" /></Grid.ColumnDefinitions><FontIcon Glyph="&#xE9CE;" FontSize="18" VerticalAlignment="Center" /><StackPanel Grid.Column="1" Spacing="2"><TextBlock Text="Remove Censor on Hover" FontSize="17" /><TextBlock Text="Temporarily hide censorship overlay when hovering over a wallpaper" Foreground="{ThemeResource TextFillColorSecondaryBrush}" /></StackPanel><ToggleSwitch x:Name="RemoveCensorOnHoverToggle" Grid.Column="2" Toggled="RemoveCensorOnHoverToggle_Toggled" /></Grid>'''

content = content.replace(
    '<StackPanel x:Name="NsfwMatureSettingsPanel" Visibility="Collapsed" Spacing="20">\n                                <StackPanel Orientation="Horizontal" Spacing="10">\n                                    <TextBlock Text="NSFW and Mature Content" FontSize="28" FontWeight="SemiBold" />\n                                </StackPanel>\n                                <StackPanel Spacing="5">',
    '<StackPanel x:Name="NsfwMatureSettingsPanel" Visibility="Collapsed" Spacing="20">\n                                <StackPanel Orientation="Horizontal" Spacing="10">\n                                    <TextBlock Text="NSFW and Mature Content" FontSize="28" FontWeight="SemiBold" />\n                                </StackPanel>\n                                <StackPanel Spacing="5">' + toggle_xml
)

content = content.replace(
    '<ListView ItemsSource="{x:Bind LibraryRoots, Mode=OneWay}" SelectionMode="None">',
    '<ListView ItemsSource="{x:Bind LibraryRoots, Mode=OneWay}" SelectionMode="None">\n                                        <ListView.ItemContainerStyle>\n                                            <Style TargetType="ListViewItem">\n                                                <Setter Property="Padding" Value="0" />\n                                                <Setter Property="Margin" Value="0,5,0,0" />\n                                                <Setter Property="MinHeight" Value="0" />\n                                                <Setter Property="HorizontalContentAlignment" Value="Stretch" />\n                                            </Style>\n                                        </ListView.ItemContainerStyle>'
)

content = content.replace(
    '<ListView x:Name="TagsListView" ItemsSource="{x:Bind VisibleTags, Mode=OneWay}" SelectionMode="None" CanDragItems="True" CanReorderItems="True" AllowDrop="True" DragItemsCompleted="TagsListView_DragItemsCompleted">',
    '<ListView x:Name="TagsListView" ItemsSource="{x:Bind VisibleTags, Mode=OneWay}" SelectionMode="None" CanDragItems="True" CanReorderItems="True" AllowDrop="True" DragItemsCompleted="TagsListView_DragItemsCompleted">\n                                        <ListView.ItemContainerStyle>\n                                            <Style TargetType="ListViewItem">\n                                                <Setter Property="Padding" Value="0" />\n                                                <Setter Property="Margin" Value="0,5,0,0" />\n                                                <Setter Property="MinHeight" Value="0" />\n                                                <Setter Property="HorizontalContentAlignment" Value="Stretch" />\n                                            </Style>\n                                        </ListView.ItemContainerStyle>'
)

with open(file_path, "w", encoding="utf-8") as f:
    f.write(content)

print("Patching complete!")
