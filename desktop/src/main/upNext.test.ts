import { describe, expect, it } from 'vitest';
import { isCreditsChapter, upNextAt } from './upNext.js';

describe('isCreditsChapter', () => {
  it.each(['Credits', 'End Credits', 'Closing credits', 'End Titles', 'Ending', 'Outro', 'ED', 'ED2', ' ED '])('counts %s', (title) => {
    expect(isCreditsChapter(title)).toBe(true);
  });

  it.each(['Post-Credits Scene', 'Mid-credits', 'After Credits', 'Stinger', 'Opening Credits', 'Intro', 'Preview', 'Chapter 12', 'Ed', 'Edward', undefined])(
    'does not count %s',
    (title) => {
      expect(isCreditsChapter(title)).toBe(false);
    },
  );
});

describe('upNextAt', () => {
  const episode = [
    { title: 'Opening', time: 0 },
    { title: 'Part A', time: 90 },
    { title: 'Part B', time: 700 },
  ];

  it('starts at the credits chapter that ends the file', () => {
    expect(upNextAt([...episode, { title: 'End Credits', time: 1290 }], 1380)).toBe(1290);
  });

  it('starts at the first of several credits chapters in a row', () => {
    expect(upNextAt([...episode, { title: 'ED', time: 1250 }, { title: 'Credits', time: 1320 }], 1380)).toBe(1250);
  });

  it('waits for the end of the file when a post-credits scene or a preview follows the credits', () => {
    expect(upNextAt([...episode, { title: 'Credits', time: 1250 }, { title: 'Post-Credits Scene', time: 1330 }], 1380)).toBeUndefined();
    expect(upNextAt([...episode, { title: 'ED', time: 1250 }, { title: 'Preview', time: 1340 }], 1380)).toBeUndefined();
  });

  it('starts at the credits after a post-credits scene when the file ends on them', () => {
    const chapters = [...episode, { title: 'Credits', time: 1200 }, { title: 'Stinger', time: 1260 }, { title: 'End Credits', time: 1300 }];
    expect(upNextAt(chapters, 1380)).toBe(1300);
  });

  it('ignores an empty chapter marker at the very end and sorts the chapters', () => {
    expect(upNextAt([{ title: 'Chapter', time: 1379 }, { title: 'Credits', time: 1290 }, ...episode], 1380)).toBe(1290);
  });

  it('waits for the end of the file without chapters, with generic names, or with credits in the first half', () => {
    expect(upNextAt([], 1380)).toBeUndefined();
    expect(upNextAt([{ title: 'Chapter 1', time: 0 }, { title: 'Chapter 2', time: 1290 }], 1380)).toBeUndefined();
    expect(upNextAt([{ title: 'Episode', time: 0 }, { title: 'Credits', time: 400 }], 1380)).toBeUndefined();
  });

  it('uses the credits chapter while the duration is unknown', () => {
    expect(upNextAt([...episode, { title: 'Credits', time: 1290 }], undefined)).toBe(1290);
  });
});
