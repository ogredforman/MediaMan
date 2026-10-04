using MediaMan.Core.Domain;

namespace MediaMan.Core.Tests;

public class PlayableTests
{
    [Fact]
    public void ForMovie_sets_only_the_title_fk()
    {
        var titleId = Guid.NewGuid();

        var playable = Playable.ForMovie(Guid.NewGuid(), titleId);

        playable.Kind.Should().Be(PlayableKind.Movie);
        playable.TitleId.Should().Be(titleId);
        playable.EpisodeId.Should().BeNull();
    }

    [Fact]
    public void ForEpisode_sets_only_the_episode_fk()
    {
        var episodeId = Guid.NewGuid();

        var playable = Playable.ForEpisode(Guid.NewGuid(), episodeId);

        playable.Kind.Should().Be(PlayableKind.Episode);
        playable.EpisodeId.Should().Be(episodeId);
        playable.TitleId.Should().BeNull();
    }
}
