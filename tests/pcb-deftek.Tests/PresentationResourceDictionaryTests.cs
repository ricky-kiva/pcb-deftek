using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using FluentAssertions;
using Xunit;

public class PresentationResourceDictionaryTests
{
    private static ResourceDictionary LoadDictionary(string fileName)
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var solutionDir = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", ".."));
        var filePath = Path.Combine(solutionDir, "Presentation", "Styles", fileName);

        using var stream = File.OpenRead(filePath);
        return (ResourceDictionary)System.Windows.Markup.XamlReader.Load(stream);
    }

    [Fact]
    public void StatusColors_ContainsExpectedStatusAndSeverityBrushes()
    {
        // Arrange
        var dictionary = LoadDictionary("StatusColors.xaml");

        // Assert
        dictionary.Contains("StatusPassBrush").Should().BeTrue();
        dictionary.Contains("StatusFailBrush").Should().BeTrue();
        dictionary.Contains("SeverityCriticalBrush").Should().BeTrue();
        dictionary.Contains("SeverityHighBrush").Should().BeTrue();
        dictionary.Contains("SeverityMediumHighBrush").Should().BeTrue();
        dictionary.Contains("SeverityMediumBrush").Should().BeTrue();
        dictionary.Contains("SeverityLowMediumBrush").Should().BeTrue();
        dictionary.Contains("HudCardBackgroundBrush").Should().BeTrue();
    }

    [Fact]
    public void Icons_ContainsAllStandardNavigationAndActionGeometries()
    {
        // Arrange
        var dictionary = LoadDictionary("Icons.xaml");

        // Assert - Navigation icons
        dictionary.Contains("IconDashboard").Should().BeTrue();
        dictionary.Contains("IconLiveDetection").Should().BeTrue();
        dictionary.Contains("IconFileDetection").Should().BeTrue();
        dictionary.Contains("IconHistory").Should().BeTrue();
        dictionary.Contains("IconSettings").Should().BeTrue();
        dictionary.Contains("IconMenu").Should().BeTrue();

        // Assert - Footer icons
        dictionary.Contains("IconGitHub").Should().BeTrue();
        dictionary.Contains("IconLinkedIn").Should().BeTrue();
        dictionary.Contains("IconInstagram").Should().BeTrue();
        dictionary.Contains("IconCoffee").Should().BeTrue();
        dictionary.Contains("IconHeart").Should().BeTrue();

        // Assert - Action & Layout icons
        dictionary.Contains("IconExport").Should().BeTrue();
        dictionary.Contains("IconNotification").Should().BeTrue();
        dictionary.Contains("IconOneColumn").Should().BeTrue();
        dictionary.Contains("IconTwoColumn").Should().BeTrue();
        dictionary.Contains("IconZoomReset").Should().BeTrue();
        dictionary.Contains("IconCrop").Should().BeTrue();
        dictionary.Contains("IconCapture").Should().BeTrue();
        dictionary.Contains("IconClose").Should().BeTrue();
    }

    [Fact]
    public void Controls_ContainsExpectedSidebarAndFooterStyles()
    {
        // Arrange
        var dictionary = LoadDictionary("Controls.xaml");

        // Assert
        dictionary.Contains("SidebarNavigationItemStyle").Should().BeTrue();
        dictionary.Contains("FooterLinkButtonStyle").Should().BeTrue();
        dictionary.Contains("FloatingHudCardStyle").Should().BeTrue();
        dictionary.Contains("StatusBadgeStyle").Should().BeTrue();
        dictionary.Contains("SeverityBadgeStyle").Should().BeTrue();
    }
}
