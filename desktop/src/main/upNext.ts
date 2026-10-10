// Where MPV offers "Up next" (SPEC §7.4 step 5): when the final credits start, never before a post-credits scene.
// Named chapters are the only credits marker a file carries; without one the offer waits for the end of the file.

/** An entry of MPV's `chapter-list` property. */
export interface Chapter {
  title?: string;
  time: number;
}

const creditsTitle = /\b(credits?|end ?titles|ending|outro)\b/i;
// Content after the credits that the viewer should still see, and opening credits.
const notCredits = /\b(post|mid|after)[- ]?credits?\b|\bstingers?\b|\bopening\b|\bintro\b/i;
// Anime releases name the ending theme `ED` or `ED2`; case-sensitive, so a word like "ed" doesn't count.
const animeEnding = /^ED\s*\d*$/;

// A chapter starting this close to the end is an empty marker some muxers add, not content.
const endSlackSeconds = 2;

export function isCreditsChapter(title: string | undefined): boolean {
  if (title === undefined) return false;
  const t = title.trim();
  return (creditsTitle.test(t) || animeEnding.test(t)) && !notCredits.test(t);
}

/**
 * The position where "Up next" starts: the start of the run of credits chapters that ends the file. Undefined (offer
 * at the end of the file) when the last chapter isn't credits, such as a post-credits scene or a preview, when no
 * chapter names the credits, or when the credits would start in the first half.
 */
export function upNextAt(chapters: readonly Chapter[], durationSeconds: number | undefined): number | undefined {
  const known = durationSeconds !== undefined && durationSeconds > 0;
  const ordered = chapters
    .filter((c) => Number.isFinite(c.time) && (!known || c.time < durationSeconds - endSlackSeconds))
    .sort((a, b) => a.time - b.time);
  let start: number | undefined;
  for (let i = ordered.length - 1; i >= 0; i--) {
    const chapter = ordered[i];
    if (!chapter || !isCreditsChapter(chapter.title)) break;
    start = chapter.time;
  }
  if (start === undefined || (known && start < durationSeconds / 2)) return undefined;
  return start;
}
