using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;

namespace SynthwaveNowPlaying.UI;

/// <summary>Shared vector shuffle button for the two player skins.</summary>
public sealed class ShuffleControl : Button
{
    public ShuffleControl()
    {
        Cursor=System.Windows.Input.Cursors.Hand;
        FocusVisualStyle=null;
        SetResourceReference(ForegroundProperty,"AlbumSecondaryBrush");
        Template=(ControlTemplate)XamlReader.Parse("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Button">
         <Grid Background="Transparent"><Border x:Name="Face" CornerRadius="6" Margin="1.5" BorderThickness=".65" BorderBrush="#727B7598" Background="#65191131" RenderTransformOrigin=".5,.5">
          <Border.RenderTransform><ScaleTransform/></Border.RenderTransform>
          <Grid><Border x:Name="Tint" CornerRadius="5" Background="{DynamicResource AlbumPrimaryBrush}" Opacity="0"/>
           <Viewbox Width="15" Height="15" Margin="2,1,2,2"><Canvas Width="22" Height="22"><Path Data="M2,5 L5,5 C9,5 10,17 14,17 L20,17 M17,14 L20,17 17,20 M2,17 L5,17 C7,17 7.5,14.5 8.4,12 M10.5,8 C11.4,6 12,5 14,5 L20,5 M17,2 L20,5 17,8" Stroke="{TemplateBinding Foreground}" StrokeThickness="1.55" StrokeStartLineCap="Round" StrokeEndLineCap="Round" StrokeLineJoin="Round"/></Canvas></Viewbox>
           <Ellipse x:Name="Dot" Width="2.7" Height="2.7" Fill="{TemplateBinding Foreground}" HorizontalAlignment="Right" VerticalAlignment="Bottom" Margin="0,0,2,2" Visibility="Collapsed"/>
          </Grid>
         </Border></Grid>
         <ControlTemplate.Triggers>
          <Trigger Property="Tag" Value="Active"><Setter TargetName="Tint" Property="Opacity" Value=".22"/><Setter TargetName="Face" Property="BorderBrush" Value="{DynamicResource AlbumSecondaryBrush}"/><Setter TargetName="Dot" Property="Visibility" Value="Visible"/></Trigger>
          <Trigger Property="IsMouseOver" Value="True"><Trigger.EnterActions><BeginStoryboard><Storyboard><DoubleAnimation Storyboard.TargetName="Face" Storyboard.TargetProperty="(UIElement.RenderTransform).(ScaleTransform.ScaleX)" To="1.035" Duration="0:0:.14"/><DoubleAnimation Storyboard.TargetName="Face" Storyboard.TargetProperty="(UIElement.RenderTransform).(ScaleTransform.ScaleY)" To="1.035" Duration="0:0:.14"/></Storyboard></BeginStoryboard></Trigger.EnterActions><Trigger.ExitActions><BeginStoryboard><Storyboard><DoubleAnimation Storyboard.TargetName="Face" Storyboard.TargetProperty="(UIElement.RenderTransform).(ScaleTransform.ScaleX)" To="1" Duration="0:0:.18"/><DoubleAnimation Storyboard.TargetName="Face" Storyboard.TargetProperty="(UIElement.RenderTransform).(ScaleTransform.ScaleY)" To="1" Duration="0:0:.18"/></Storyboard></BeginStoryboard></Trigger.ExitActions></Trigger>
          <Trigger Property="IsPressed" Value="True"><Setter TargetName="Face" Property="Opacity" Value=".65"/></Trigger>
         </ControlTemplate.Triggers>
        </ControlTemplate>
        """);
    }
    public void SetShuffleState(bool enabled)
    {
        Tag=enabled?"Active":null;
        var accent=(TryFindResource("AlbumSecondaryBrush") as SolidColorBrush)?.Color??Color.FromRgb(139,219,226);
        Foreground=new SolidColorBrush(enabled?Color.FromRgb((byte)((accent.R+255)/2),(byte)((accent.G+255)/2),(byte)((accent.B+255)/2)):Color.FromRgb(203,199,221));
        ToolTip=enabled?"Shuffle on · click for playlist order":"Shuffle off · click for random playback";
        System.Windows.Automation.AutomationProperties.SetName(this,enabled?"Shuffle on":"Shuffle off");
    }
}
