using MediaMan.Core;
using MediaMan.Core.Modules;

namespace MediaMan.Fakes;

/// <summary>Extra artwork per title from fixtures added by the test; unknown titles have none (SPEC §10.4).</summary>
public sealed class FakeArtworkProvider : IArtworkProvider
{
    private readonly Lock _gate = new();
    private readonly Dictionary<ExternalId, List<ArtworkRef>> _artwork = [];
    private readonly List<ExternalId> _requests = [];

    public IReadOnlyList<ExternalId> Requests
    {
        get
        {
            lock (_gate)
            {
                return [.. _requests];
            }
        }
    }

    public FakeArtworkProvider Add(ExternalId id, params ArtworkRef[] artwork)
    {
        lock (_gate)
        {
            if (!_artwork.TryGetValue(id, out var list))
            {
                _artwork[id] = list = [];
            }

            list.AddRange(artwork);
        }

        return this;
    }

    public Task<IReadOnlyList<ArtworkRef>> GetArtworkAsync(ExternalId id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _requests.Add(id);
            IReadOnlyList<ArtworkRef> result = _artwork.TryGetValue(id, out var list) ? [.. list] : [];
            return Task.FromResult(result);
        }
    }
}
