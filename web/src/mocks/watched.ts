import { http, HttpResponse } from 'msw';
import type { Home, HomeItem, TitleDetail, TitleSummary, TitleWatchedRequest, WatchedRequest } from '../api/mediaman.schemas';

const problem = (body: Record<string, unknown> & { status: number }) =>
  HttpResponse.json(body, { status: body.status, headers: { 'Content-Type': 'application/problem+json' } });

const watchedInvalid = () =>
  problem({
    title: "That watched state isn't valid.",
    status: 400,
    code: 'watched.invalid',
    detail: 'Say whether it is watched, and give a season number of 0 or more.',
    action: 'Try again from the title page.',
  });

const notFound = (code: string, title: string) =>
  problem({ title, status: 404, code, detail: "It isn't in the library any more.", action: 'Go back to Movies or Shows and pick it again.' });

const episodesOf = (detail: TitleDetail) => (detail.seasons ?? []).flatMap((s) => s.episodes.map((e) => ({ season: s.number, e })));

/** Every Playable of a title, with its season (undefined for a movie) and whether a file holds it. */
function playablesOf(detail: TitleDetail): { id: string; season?: number; hasFile: boolean; watched: boolean }[] {
  if (detail.playable) {
    return [{ id: detail.playable.id, hasFile: detail.playable.versions.length > 0, watched: detail.playable.watched }];
  }
  return episodesOf(detail).flatMap(({ season, e }) =>
    e.playableId === undefined ? [] : [{ id: e.playableId, season, hasFile: e.versions.length > 0, watched: e.watched }],
  );
}

/**
 * The session profile's watched marks over `details`, with the server's rules: marking watched drops the resume point,
 * unwatched clears progress, and a show is watched when every episode with a file outside specials is (every special
 * when it has only specials). Titles nobody marked keep their fixture state; `reset` forgets every mark.
 */
export function createMockWatched(details: Record<string, TitleDetail>) {
  const marks = new Map<string, boolean>();

  const findPlayable = (playableId: string) =>
    Object.values(details).find((detail) => playablesOf(detail).some((p) => p.id === playableId));

  const applyDetail = (detail: TitleDetail): TitleDetail => {
    const mark = <T extends { watched: boolean; resumeSeconds?: number }>(playable: T, id: string | undefined): T => {
      const watched = id === undefined ? undefined : marks.get(id);
      if (watched === undefined) return playable;
      const { resumeSeconds: _dropped, ...rest } = playable;
      return { ...rest, watched } as T;
    };
    return {
      ...detail,
      playable: detail.playable && mark(detail.playable, detail.playable.id),
      seasons: detail.seasons?.map((season) => ({
        ...season,
        episodes: season.episodes.map((episode) => mark(episode, episode.playableId)),
      })),
      // A marked episode loses its resume point; the mock doesn't re-pick the show's episode.
      showPlay:
        detail.showPlay && marks.has(detail.showPlay.playableId)
          ? { playableId: detail.showPlay.playableId, episode: detail.showPlay.episode }
          : detail.showPlay,
    };
  };

  const applySummary = (summary: TitleSummary): TitleSummary => {
    const detail = details[summary.id];
    if (detail === undefined) return summary;
    const playables = playablesOf(detail);
    if (!playables.some((p) => marks.has(p.id))) return summary;
    const withFiles = playables
      .filter((p) => p.hasFile)
      .map((p) => ({ ...p, watched: marks.get(p.id) ?? p.watched }));
    const regular = withFiles.filter((p) => p.season !== 0);
    const counted = regular.length > 0 ? regular : withFiles;
    return { ...summary, watched: counted.length > 0 && counted.every((p) => p.watched) };
  };

  // Marking a Continue Watching or Next Up item takes it off the row, as the server's rows would.
  const keepItem = (item: HomeItem) => item.playableId === undefined || !marks.has(item.playableId);

  const applyHome = (home: Home): Home => ({
    hero: home.hero && { ...home.hero, title: applySummary(home.hero.title) },
    rows: home.rows
      .map((row) => ({ ...row, items: row.items.filter(keepItem).map((item) => ({ ...item, title: applySummary(item.title) })) }))
      .filter((row) => row.items.length > 0),
  });

  const handlers = [
    http.put('*/playables/:playableId/watched', async ({ params, request }) => {
      const body = (await request.json()) as Partial<WatchedRequest>;
      if (typeof body.watched !== 'boolean') return watchedInvalid();
      const playableId = String(params.playableId);
      if (findPlayable(playableId) === undefined) return notFound('playable.not_found', 'Nothing to mark here.');
      marks.set(playableId, body.watched);
      return new HttpResponse(null, { status: 204 });
    }),
    http.put('*/titles/:titleId/watched', async ({ params, request }) => {
      const body = (await request.json()) as Partial<TitleWatchedRequest>;
      const { watched, seasonNumber } = body;
      if (typeof watched !== 'boolean' || (seasonNumber !== undefined && !(Number.isInteger(seasonNumber) && seasonNumber >= 0))) {
        return watchedInvalid();
      }
      const detail = details[String(params.titleId)];
      if (detail === undefined) return notFound('title.not_found', 'Title not found.');
      if (seasonNumber !== undefined && !(detail.seasons ?? []).some((s) => s.number === seasonNumber)) {
        return notFound('season.not_found', 'Season not found.');
      }
      for (const playable of playablesOf(detail)) {
        if (seasonNumber === undefined || playable.season === seasonNumber) marks.set(playable.id, watched);
      }
      return new HttpResponse(null, { status: 204 });
    }),
  ];

  return {
    handlers,
    applyDetail,
    applySummary,
    applyHome,
    reset: () => {
      marks.clear();
    },
  };
}
