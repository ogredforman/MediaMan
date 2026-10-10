import { http, HttpResponse } from 'msw';
import type { MediaVersion, PreferredVersionRequest, TitleDetail, VersionPreference } from '../api/mediaman.schemas';
import { resolutionLabel } from '../lib/mediaFormat';

const problem = (body: Record<string, unknown> & { status: number }) =>
  HttpResponse.json(body, { status: body.status, headers: { 'Content-Type': 'application/problem+json' } });

const titleNotFound = () =>
  problem({
    title: 'Title not found.',
    status: 404,
    code: 'title.not_found',
    detail: "This title isn't in the library. It may have been removed or re-matched.",
    action: 'Go back to Movies or Shows and pick it again.',
  });

const qualityOf = (version: MediaVersion): VersionPreference => ({
  mediaFileId: version.mediaFileId,
  resolution: resolutionLabel(version.width, version.height),
  hdrFormat: version.hdrFormat,
});

const sameQuality = (a: VersionPreference, b: VersionPreference) =>
  a.resolution?.toLowerCase() === b.resolution?.toLowerCase() && a.hdrFormat?.toLowerCase() === b.hdrFormat?.toLowerCase();

/** Highest picture first, then the larger file, as the server's `VersionPicker` ranks them. */
function best(versions: MediaVersion[]): MediaVersion | undefined {
  return [...versions].sort((a, b) => (b.height ?? 0) - (a.height ?? 0) || b.sizeBytes - a.sizeBytes)[0];
}

/**
 * The version Play uses (SPEC §7.4 step 2): a movie plays the picked file while it's there, an episode its best version
 * at the picked quality; otherwise the highest quality. Missing files never play.
 */
function select(versions: MediaVersion[], preference: VersionPreference | undefined, isMovie: boolean): string | undefined {
  const playable = versions.filter((v) => v.status !== 'missing');
  if (preference !== undefined) {
    const picked = isMovie ? playable.find((v) => v.mediaFileId === preference.mediaFileId) : undefined;
    const matching = picked ?? best(playable.filter((v) => sameQuality(qualityOf(v), preference)));
    if (matching !== undefined) return matching.mediaFileId;
  }
  return best(playable)?.mediaFileId;
}

/**
 * The session profile's preferred version per title over `details`, with the server's rules: only a matched version of
 * the title can be picked (`version.invalid`). `apply` fills `selectedMediaFileId` and `versionPreference`;
 * `reset` forgets every pick.
 */
export function createMockVersions(details: Record<string, TitleDetail>) {
  const preferences = new Map<string, VersionPreference>();

  const apply = (detail: TitleDetail): TitleDetail => {
    const preference = preferences.get(detail.id);
    return {
      ...detail,
      versionPreference: preference,
      playable: detail.playable && {
        ...detail.playable,
        selectedMediaFileId: select(detail.playable.versions, preference, true),
      },
      seasons: detail.seasons?.map((season) => ({
        ...season,
        episodes: season.episodes.map((episode) => ({
          ...episode,
          selectedMediaFileId: select(episode.versions, preference, false),
        })),
      })),
    };
  };

  /** The version Play uses for a Playable, or undefined for an unknown Playable or one with nothing to play. */
  const selectedFor = (playableId: string): MediaVersion | undefined => {
    for (const detail of Object.values(details).map(apply)) {
      if (detail.playable?.id === playableId) {
        return detail.playable.versions.find((v) => v.mediaFileId === detail.playable?.selectedMediaFileId);
      }
      for (const episode of (detail.seasons ?? []).flatMap((s) => s.episodes)) {
        if (episode.playableId === playableId) return episode.versions.find((v) => v.mediaFileId === episode.selectedMediaFileId);
      }
    }
    return undefined;
  };

  const handlers = [
    http.put('*/titles/:titleId/preferred-version', async ({ params, request }) => {
      const detail = details[String(params.titleId)];
      if (detail === undefined) return titleNotFound();
      const { mediaFileId } = (await request.json()) as PreferredVersionRequest;
      const versions = [...(detail.playable?.versions ?? []), ...(detail.seasons ?? []).flatMap((s) => s.episodes.flatMap((e) => e.versions))];
      const version = versions.find((v) => v.mediaFileId === mediaFileId && v.status === 'matched');
      if (version === undefined) {
        return problem({
          title: "That version can't be picked.",
          status: 400,
          code: 'version.invalid',
          detail: "The file isn't an available version of this title. It may be missing or have been re-matched.",
          action: 'Reload the page and pick one of the versions listed.',
        });
      }
      preferences.set(detail.id, qualityOf(version));
      return new HttpResponse(null, { status: 204 });
    }),
    http.delete('*/titles/:titleId/preferred-version', ({ params }) => {
      if (details[String(params.titleId)] === undefined) return titleNotFound();
      preferences.delete(String(params.titleId));
      return new HttpResponse(null, { status: 204 });
    }),
  ];

  return {
    handlers,
    apply,
    selectedFor,
    reset() {
      preferences.clear();
    },
  };
}
