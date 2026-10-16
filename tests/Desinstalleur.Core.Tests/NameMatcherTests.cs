using Desinstalleur.Core;

namespace Desinstalleur.Core.Tests;

public class NameMatcherTests
{
    [Theory]
    [InlineData("VLC media player", "VideoLAN", "VLC media player", MatchConfidence.High)]
    [InlineData("Mozilla Firefox (x64 fr)", "Mozilla", "Firefox", MatchConfidence.High)]
    [InlineData("Notepad++ (64-bit x64)", "Notepad++ Team", "Notepad++", MatchConfidence.High)]
    [InlineData("7-Zip 23.01 (x64)", "Igor Pavlov", "7-Zip", MatchConfidence.High)]
    [InlineData("Spotify", "Spotify AB", "SpotifyWebHelper", MatchConfidence.Low)]
    [InlineData("Discord", "Discord Inc.", "Steam", MatchConfidence.None)]
    public void Matches(string program, string publisher, string folder, MatchConfidence expected) =>
        Assert.Equal(expected, NameMatcher.Match(folder, program, publisher));

    [Theory]
    [InlineData("Microsoft")]
    [InlineData("Common Files")]
    [InlineData("Temp")]
    [InlineData("Packages")]
    public void GenericFoldersNeverMatch(string folder)
    {
        Assert.True(NameMatcher.IsGenericFolderName(folder));
        Assert.Equal(MatchConfidence.None, NameMatcher.Match(folder, folder, null));
    }

    [Fact]
    public void PublisherFolderIsRecognised()
    {
        Assert.True(NameMatcher.IsPublisherFolder("Mozilla", "Mozilla Corporation"));
        Assert.False(NameMatcher.IsPublisherFolder("Firefox", "Mozilla Corporation"));
    }

    [Fact]
    public void NormalizeDropsVersionsAndAccents() =>
        Assert.Equal("editeur video", NameMatcher.Normalize("Éditeur Vidéo 2.4.1 (x64)"));
}
