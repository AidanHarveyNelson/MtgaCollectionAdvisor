using MtgaCollectionAdvisor.Core.Creators;
using Xunit;

namespace MtgaCollectionAdvisor.Core.Tests;

/// <summary>
/// A channel that mixes Magic with other videos (vlogs, other games) contributes only its
/// Magic ones. Titles here are shaped like real Portuguese and English ones.
/// </summary>
public class CreatorVideoRelevanceTests
{
    [Theory]
    [InlineData("MONO VERMELHO NO MTG ARENA - RANQUEADA ATÉ O MÍTICO")]
    [InlineData("Esse deck de Magic Standard está quebrado")]
    [InlineData("Magic: The Gathering Arena - abrindo pacotes")]
    [InlineData("Gastei todos os meus curingas nesse deck")]
    [InlineData("MTGA Historic Brawl com o novo comandante")]
    [InlineData("Arena Standard BO1 - Esper Control")]
    public void IsMagicVideo_Should_AcceptMagicTitles(string title)
    {
        Assert.True(CreatorVideoRelevance.IsMagicVideo(Video(title), VideoDeckSource.None));
    }

    [Theory]
    [InlineData("VLOG: um dia na praia com a família")]
    [InlineData("Arena Breakout - primeira partida")]
    [InlineData("Hearthstone: meu novo deck de mago")]
    [InlineData("Reagindo aos melhores memes da semana")]
    public void IsMagicVideo_Should_RejectOtherVideos(string title)
    {
        Assert.False(CreatorVideoRelevance.IsMagicVideo(Video(title), VideoDeckSource.None));
    }

    [Fact]
    public void IsMagicVideo_Should_Accept_When_ADeckWasFound_WhateverTheTitle()
    {
        var source = new VideoDeckSource(DeckSourceKind.External, ExternalSite: "AetherHub", ExternalUrl: "https://aetherhub.com/x");

        Assert.True(CreatorVideoRelevance.IsMagicVideo(Video("Olha isso!!"), source));
    }

    [Fact]
    public void IsMagicVideo_Should_IgnoreAStandingFooterFarDownTheDescription()
    {
        // A footer on every video must not tag the vlogs as Magic.
        var description = new string('.', 400) + "\nTambém jogo Magic Arena, se inscreva!";

        Assert.False(CreatorVideoRelevance.IsMagicVideo(Video("VLOG na praia", description), VideoDeckSource.None));
    }

    [Fact]
    public void Merge_Should_KeepOnlyMagicVideos_When_ChannelPostsOtherContent()
    {
        var mixed = new CreatorChannel("UMotivo", "UC-mixed", PostsOtherContent: true);
        var feed = new ChannelFeedResult(mixed,
        [
            Video("MTG ARENA - RANQUEADA COM MONO VERDE", id: "magic", creator: "UMotivo"),
            Video("VLOG: um dia na praia", id: "vlog", creator: "UMotivo")
        ]);

        var merged = CreatorVideoMerge.Merge([], [feed], [mixed]);

        Assert.Equal(["magic"], merged.Select(v => v.VideoId));
    }

    [Fact]
    public void Merge_Should_KeepEveryVideo_When_ChannelPostsOnlyMagic()
    {
        var magicOnly = new CreatorChannel("Crokeyz", "UC-crokeyz");
        var feed = new ChannelFeedResult(magicOnly, [Video("Just a chill stream", id: "chill", creator: "Crokeyz")]);

        var merged = CreatorVideoMerge.Merge([], [feed], [magicOnly]);

        Assert.Single(merged);
    }

    private static FeedVideo Video(string title, string description = "", string id = "v1", string creator = "Someone") =>
        new(creator, id, title, new DateTimeOffset(2026, 9, 22, 0, 0, 0, TimeSpan.Zero), description);
}
