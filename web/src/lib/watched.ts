import { useQueryClient } from '@tanstack/react-query';
import { useSetPlayableWatched, useSetTitleWatched } from '../api/mediaman';
import { invalidateBrowseQueries } from './browseQueries';
import type { ApiError } from './http';

/** What a Mark watched action applies to: one movie or episode, or a whole title or one of a show's seasons. */
export type WatchedTarget = { playableId: string } | { titleId: string; seasonNumber?: number | undefined };

export interface MarkWatched {
  mark: (target: WatchedTarget, watched: boolean) => void;
  isPending: boolean;
  error: ApiError | null;
}

/** `PUT /playables/{id}/watched` or `PUT /titles/{id}/watched`, then refreshes what shows watched state. */
export function useMarkWatched(): MarkWatched {
  const queryClient = useQueryClient();
  const mutation = { onSuccess: () => invalidateBrowseQueries(queryClient) };
  const playable = useSetPlayableWatched({ mutation });
  const title = useSetTitleWatched({ mutation });

  return {
    mark: (target, watched) => {
      if ('playableId' in target) {
        title.reset();
        playable.mutate({ playableId: target.playableId, data: { watched } });
      } else {
        playable.reset();
        const season = target.seasonNumber === undefined ? {} : { seasonNumber: target.seasonNumber };
        title.mutate({ titleId: target.titleId, data: { watched, ...season } });
      }
    },
    isPending: playable.isPending || title.isPending,
    error: playable.error ?? title.error,
  };
}
