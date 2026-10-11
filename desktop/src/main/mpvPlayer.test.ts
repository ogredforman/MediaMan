import { existsSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { contractPlay, describePlayerContract, settle } from '../fakes/playerContract.js';
import { ipcPathFor, mpvArgs, mpvAssetsIn, MpvPlayer, PlayerStartError, redactUrls, resolveMpvPath, upNextMessages, type MpvChild, type MpvConnection, type MpvPlayerOptions } from './mpvPlayer.js';
import type { PlayerEndReason, PlayerProgress } from './player.js';

/** A stand-in MPV process: answers IPC the way MPV does, and tests decide what it sends and when it exits. */
class FakeMpv {
  readonly spawned: { command: string; args: string[] }[] = [];
  readonly commands: unknown[][] = [];
  readonly signals: string[] = [];
  failSpawn: Error | undefined;
  /** Spawns that exited before MPV could start. */
  startFailures = 0;
  exitBeforeIpc = false;
  refuseConnects = 0;
  neverConnect = false;
  ignoreQuit = false;
  position = 0;
  private exitListener: ((code: number | null, error?: Error) => void) | undefined;
  private pendingExit: [number | null, Error | undefined] | undefined;
  private dataListener: ((chunk: string) => void) | undefined;
  closed = false;

  readonly options: Pick<MpvPlayerOptions, 'spawn' | 'connect' | 'ipcPath' | 'log'> = {
    spawn: (command, args) => {
      this.spawned.push({ command, args });
      this.exitListener = undefined;
      this.pendingExit = undefined;
      this.dataListener = undefined;
      this.closed = false;
      const child: MpvChild = {
        kill: (signal) => {
          this.signals.push(signal);
          this.exit(null);
        },
        onExit: (listener) => {
          this.exitListener = listener;
          if (this.pendingExit) this.exit(...this.pendingExit);
        },
      };
      if (this.failSpawn) {
        const error = this.failSpawn;
        this.failSpawn = undefined;
        this.startFailures++;
        this.exit(null, error);
      } else if (this.exitBeforeIpc) {
        this.exitBeforeIpc = false;
        this.startFailures++;
        this.exit(1);
      }
      return child;
    },
    connect: () => {
      if (this.neverConnect || this.refuseConnects-- > 0) return Promise.reject(new Error('ENOENT'));
      const connection: MpvConnection = {
        write: (data) => {
          for (const line of data.split('\n')) if (line) this.onCommand(JSON.parse(line) as { command: unknown[]; request_id?: number });
        },
        onData: (listener) => {
          this.dataListener = listener;
        },
        close: () => {
          this.closed = true;
        },
      };
      return Promise.resolve(connection);
    },
    ipcPath: (id) => `ipc-${id}`,
    log: () => undefined,
  };

  /** Sends raw IPC text, as MPV writes it. */
  sendRaw(text: string): void {
    this.dataListener?.(text);
  }

  send(message: object): void {
    this.sendRaw(`${JSON.stringify(message)}\n`);
  }

  /** Exits now, or as soon as the player listens for the exit. */
  exit(code: number | null, error?: Error): void {
    if (!this.exitListener) {
      this.pendingExit = [code, error];
      return;
    }
    const listener = this.exitListener;
    this.exitListener = undefined;
    this.pendingExit = undefined;
    listener(code, error);
  }

  private onCommand(message: { command: unknown[]; request_id?: number }): void {
    this.commands.push(message.command);
    const [name] = message.command;
    if (name === 'quit' && !this.ignoreQuit) {
      queueMicrotask(() => {
        this.send({ event: 'end-file', reason: 'quit' });
        this.exit(0);
      });
    } else if (name === 'get_property') {
      queueMicrotask(() => {
        this.send({ data: this.position, error: 'success', request_id: message.request_id });
      });
    }
  }
}

function setUp(options: Partial<MpvPlayerOptions> = {}) {
  const mpv = new FakeMpv();
  const player = new MpvPlayer({ mpvPath: 'mpv', connectRetryMs: 1, connectTimeoutMs: 50, stopGraceMs: 20, ...mpv.options, ...options });
  const progress: PlayerProgress[] = [];
  const ended: [PlayerEndReason, string | undefined][] = [];
  player.onProgress((p) => progress.push(p));
  player.onEnded((reason, detail) => ended.push([reason, detail]));
  return { mpv, player, progress, ended };
}

describePlayerContract('MpvPlayer', () => {
  const mpv = new FakeMpv();
  const player = new MpvPlayer({ mpvPath: 'mpv', connectRetryMs: 1, connectTimeoutMs: 50, stopGraceMs: 20, ...mpv.options });
  return {
    player,
    progress: async (p) => {
      mpv.send({ event: 'property-change', id: 1, name: 'time-pos', data: p.positionSeconds });
      mpv.send({ event: 'property-change', id: 2, name: 'duration', data: p.durationSeconds });
      mpv.send({ event: 'property-change', id: 3, name: 'pause', data: !p.paused });
      mpv.send({ event: 'property-change', id: 3, name: 'pause', data: p.paused });
      await settle();
    },
    end: async (reason, detail) => {
      mpv.send(reason === 'eof' ? { event: 'end-file', reason: 'eof' } : { event: 'end-file', reason: 'error', file_error: detail });
      mpv.exit(reason === 'eof' ? 0 : 2);
      await settle();
    },
    reachCredits: async () => {
      mpv.send({ event: 'property-change', id: 2, name: 'duration', data: 1380 });
      mpv.send({ event: 'property-change', id: 5, name: 'chapter-list', data: [{ title: 'Episode', time: 0 }, { title: 'Credits', time: 1290 }] });
      mpv.send({ event: 'property-change', id: 1, name: 'time-pos', data: 1291 });
      await settle();
    },
    reachEnd: async () => {
      mpv.send({ event: 'property-change', id: 4, name: 'eof-reached', data: true });
      await settle();
    },
    upNexts: () =>
      mpv.commands
        .filter(([name, message]) => name === 'script-message' && message === upNextMessages.show)
        .map(([, , seconds, title]) => ({ title: String(title), seconds: Number(seconds) })),
    chooseUpNext: async (choice) => {
      mpv.send({ event: 'client-message', args: [upNextMessages.choice, choice] });
      await settle();
    },
    failNextStart: () => {
      mpv.failSpawn = new Error('spawn mpv ENOENT');
    },
    launches: () => mpv.spawned.length - mpv.startFailures,
  };
});

describe('MpvPlayer', () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  it('launches MPV with its IPC server, the title, the start position and the URL last', async () => {
    const { mpv, player } = setUp();

    await player.play({ ...contractPlay, startSeconds: 95.5 });

    expect(mpv.spawned).toEqual([
      {
        command: 'mpv',
        args: [
          `--input-ipc-server=ipc-${String(process.pid)}-1`,
          '--force-window=yes',
          '--idle=no',
          '--keep-open=yes',
          '--force-media-title=Alien (1979)',
          '--start=95.5',
          '--',
          contractPlay.url,
        ],
      },
    ]);
    expect(mpv.commands).toEqual([
      ['observe_property', 1, 'time-pos'],
      ['observe_property', 2, 'duration'],
      ['observe_property', 3, 'pause'],
      ['observe_property', 4, 'eof-reached'],
      ['observe_property', 5, 'chapter-list'],
    ]);
  });

  it('adds the subtitles once the file has loaded, selecting the default one', async () => {
    const { mpv, player } = setUp();
    await player.play({
      ...contractPlay,
      subtitles: [
        { url: 'http://s/en.srt', language: 'en', title: 'English', isDefault: true },
        { url: 'http://s/fr.srt', language: 'fr' },
        { url: 'http://s/x.srt' },
      ],
    });

    mpv.send({ event: 'file-loaded' });

    expect(mpv.commands.filter(([name]) => name === 'sub-add')).toEqual([
      ['sub-add', 'http://s/en.srt', 'select', 'English', 'en'],
      ['sub-add', 'http://s/fr.srt', 'auto', 'fr', 'fr'],
      ['sub-add', 'http://s/x.srt', 'auto', 'External', ''],
    ]);
  });

  it('reports progress every 10 s', async () => {
    vi.useFakeTimers();
    const { mpv, player, progress } = setUp();
    await player.play(contractPlay);
    mpv.send({ event: 'property-change', id: 1, name: 'time-pos', data: 42 });
    mpv.send({ event: 'property-change', id: 2, name: 'duration', data: 600 });

    vi.advanceTimersByTime(9_999);
    expect(progress).toEqual([]);
    vi.advanceTimersByTime(1);
    mpv.send({ event: 'property-change', id: 1, name: 'time-pos', data: 52 });
    vi.advanceTimersByTime(10_000);

    expect(progress).toEqual([
      { positionSeconds: 42, durationSeconds: 600, paused: false },
      { positionSeconds: 52, durationSeconds: 600, paused: false },
    ]);
  });

  it('reports on pause and unpause, but not for an unchanged pause state', async () => {
    const { mpv, player, progress } = setUp();
    await player.play(contractPlay);
    mpv.send({ event: 'property-change', id: 1, name: 'time-pos', data: 10 });

    mpv.send({ event: 'property-change', id: 3, name: 'pause', data: false });
    mpv.send({ event: 'property-change', id: 3, name: 'pause', data: true });
    mpv.send({ event: 'property-change', id: 3, name: 'pause', data: false });

    expect(progress.map((p) => p.paused)).toEqual([true, false]);
  });

  it('reports where a seek landed once playback restarts', async () => {
    const { mpv, player, progress } = setUp();
    await player.play(contractPlay);
    mpv.send({ event: 'property-change', id: 1, name: 'time-pos', data: 10 });
    mpv.send({ event: 'playback-restart' }); // the first start isn't a seek

    mpv.position = 300;
    mpv.send({ event: 'seek' });
    mpv.send({ event: 'playback-restart' });
    await settle();

    expect(progress).toEqual([{ positionSeconds: 300, durationSeconds: 0, paused: false }]);
    expect(mpv.commands.filter(([name]) => name === 'get_property')).toEqual([['get_property', 'time-pos']]);
  });

  it('reads IPC lines split across chunks and several lines in one chunk, skipping replies and junk', async () => {
    const { mpv, player, progress } = setUp();
    await player.play(contractPlay);

    mpv.sendRaw('{"error":"success","request_id":0}\nnot json\n{"event":"property-change","id":1,"na');
    mpv.sendRaw('me":"time-pos","data":7}\n{"event":"property-change","id":3,"name":"pause","data":true}\n');

    expect(progress).toEqual([{ positionSeconds: 7, durationSeconds: 0, paused: true }]);
  });

  it('reports the last position when MPV quits, then ends', async () => {
    const { mpv, player, progress, ended } = setUp();
    await player.play(contractPlay);
    mpv.send({ event: 'property-change', id: 1, name: 'time-pos', data: 61 });
    mpv.send({ event: 'property-change', id: 2, name: 'duration', data: 600 });

    mpv.send({ event: 'end-file', reason: 'quit' });
    mpv.exit(0);
    await settle();

    expect(progress).toEqual([{ positionSeconds: 61, durationSeconds: 600, paused: false }]);
    expect(ended).toEqual([['quit', undefined]]);
    expect(mpv.closed).toBe(true);
  });

  it('ends with an error when MPV exits abnormally without ending the file', async () => {
    const { mpv, player, ended } = setUp();
    await player.play(contractPlay);

    mpv.exit(3);
    await settle();

    expect(ended).toEqual([['error', 'MPV exited with code 3.']]);
  });

  it('reports the credits once, where the chapters put them, and only after the position gets there', async () => {
    const { mpv, player, progress } = setUp();
    const credits: number[] = [];
    player.onCredits(() => credits.push(progress.at(-1)?.positionSeconds ?? -1));
    await player.play(contractPlay);

    mpv.send({ event: 'property-change', id: 1, name: 'time-pos', data: 1300 });
    mpv.send({ event: 'property-change', id: 5, name: 'chapter-list', data: [{ title: 'Credits', time: 1290 }, { title: 'Stinger', time: 1340 }] });
    expect(credits).toEqual([]);

    mpv.send({ event: 'property-change', id: 5, name: 'chapter-list', data: [{ title: 'Episode', time: 0 }, { title: 'ED', time: 1290 }, { time: 'x' }, 'junk'] });
    mpv.send({ event: 'property-change', id: 1, name: 'time-pos', data: 1310 });
    mpv.send({ event: 'property-change', id: 1, name: 'time-pos', data: 1320 });

    expect(credits).toEqual([1300]);
  });

  it('reports the end of the file each time playback gets back there, without ending', async () => {
    const { mpv, player, ended } = setUp();
    let reached = 0;
    player.onEndReached(() => reached++);
    await player.play(contractPlay);

    for (const data of [true, true, false, true]) mpv.send({ event: 'property-change', id: 4, name: 'eof-reached', data });

    expect(reached).toBe(2);
    expect(ended).toEqual([]);
  });

  it('sends Up next to the OSC as a script message and ignores other client messages', async () => {
    const { mpv, player } = setUp();
    const choices: string[] = [];
    player.onUpNextChoice((choice) => choices.push(choice));
    await player.play(contractPlay);

    player.showUpNext({ title: 'Show S01E02 · Two', seconds: 10 });
    mpv.send({ event: 'client-message', args: [upNextMessages.choice, 'later'] });
    mpv.send({ event: 'client-message', args: ['other-script', 'play'] });
    mpv.send({ event: 'client-message' });

    expect(mpv.commands.at(-1)).toEqual(['script-message', 'mediaman-up-next', '10', 'Show S01E02 · Two']);
    expect(choices).toEqual([]);
  });

  it('retries the IPC connection until MPV has opened it', async () => {
    const { mpv, player } = setUp();
    mpv.refuseConnects = 3;

    await player.play(contractPlay);

    expect(mpv.commands).toContainEqual(['observe_property', 1, 'time-pos']);
  });

  it("rejects with the MPV path when MPV can't be spawned", async () => {
    const { mpv, player, ended } = setUp({ mpvPath: '/opt/mpv/mpv' });
    mpv.failSpawn = new Error('spawn /opt/mpv/mpv ENOENT');

    const play = player.play(contractPlay);

    await expect(play).rejects.toBeInstanceOf(PlayerStartError);
    await expect(play).rejects.toThrow(
      "MPV couldn't be started (spawn /opt/mpv/mpv ENOENT). Check that MPV is installed and that the MPV path (/opt/mpv/mpv) points at it.",
    );
    expect(ended).toEqual([]);
  });

  it('rejects when MPV exits before its IPC answers', async () => {
    const { mpv, player } = setUp();
    mpv.exitBeforeIpc = true;

    await expect(player.play(contractPlay)).rejects.toThrow("MPV couldn't be started (it exited with code 1).");
  });

  it("kills MPV and rejects when its IPC doesn't answer in time", async () => {
    const { mpv, player, ended } = setUp();
    mpv.neverConnect = true;

    await expect(player.play(contractPlay)).rejects.toThrow(/didn't answer on ipc-.* within 0.05 s/);
    expect(mpv.signals).toEqual(['SIGKILL']);
    expect(ended).toEqual([]);
  });

  it('rejects as stopped, not as a start failure, when stopped while MPV is starting', async () => {
    const { mpv, player, ended } = setUp({ connectTimeoutMs: 1_000 });
    mpv.refuseConnects = 1_000;

    const play = player.play(contractPlay);
    await settle();
    await player.stop();

    await expect(play).rejects.toThrow('Playback was stopped before MPV started.');
    expect(ended).toEqual([]);
  });

  it("kills MPV when it doesn't quit within the grace period", async () => {
    const { mpv, player, ended } = setUp();
    await player.play(contractPlay);
    mpv.ignoreQuit = true;

    await player.stop();

    expect(mpv.commands).toContainEqual(['quit']);
    expect(mpv.signals).toEqual(['SIGKILL']);
    expect(ended).toEqual([['quit', undefined]]);
  });
});

describe('redactUrls', () => {
  it('drops the query string, where the stream signature is, from URLs MPV prints', () => {
    expect(redactUrls('Playing: http://127.0.0.1:8420/media-files/a/stream?exp=1&sig=SECRET')).toBe(
      'Playing: http://127.0.0.1:8420/media-files/a/stream?…',
    );
    expect(redactUrls('[sub] HTTPS://h/s.srt#x?sig=SECRET and http://h/b?sig=2 done')).toBe('[sub] HTTPS://h/s.srt?… and http://h/b?… done');
    expect(redactUrls('Exiting... (Quit)')).toBe('Exiting... (Quit)');
  });
});

describe('resolveMpvPath', () => {
  it('uses MEDIAMAN_MPV_PATH, or mpv from PATH', () => {
    expect(resolveMpvPath({ MEDIAMAN_MPV_PATH: ' /usr/local/bin/mpv ' })).toBe('/usr/local/bin/mpv');
    expect(resolveMpvPath({ MEDIAMAN_MPV_PATH: '  ' })).toBe('mpv');
    expect(resolveMpvPath({})).toBe('mpv');
  });
});

describe('ipcPathFor', () => {
  it('uses a named pipe on Windows and a socket in the temp directory elsewhere', () => {
    expect(ipcPathFor('win32', 'C:\\Temp', '12-1')).toBe('\\\\.\\pipe\\mediaman-mpv-12-1');
    expect(ipcPathFor('linux', '/tmp', '12-1')).toMatch(/^[/\\]tmp[/\\]mediaman-mpv-12-1\.sock$/);
  });
});

describe('mpvArgs', () => {
  it('leaves out the start position at 0, and keeps a URL starting with a dash after --', () => {
    expect(mpvArgs({ ...contractPlay, url: '-weird' }, 'p')).toEqual([
      '--input-ipc-server=p',
      '--force-window=yes',
      '--idle=no',
      '--keep-open=yes',
      '--force-media-title=Alien (1979)',
      '--',
      '-weird',
    ]);
  });

  it('passes the language preferences as --alang and --slang, and leaves out empty ones', () => {
    const args = mpvArgs({ ...contractPlay, audioLanguages: ['de', 'ger', 'deu', 'en', 'eng'], subtitleLanguages: [] }, 'p');
    expect(args.slice(args.indexOf('--force-media-title=Alien (1979)') + 1)).toEqual(['--alang=de,ger,deu,en,eng', '--', contractPlay.url]);
  });

  it('loads the MediaMan config and OSC after the launch options, with the built-in OSC off', () => {
    const args = mpvArgs(contractPlay, 'p', { config: 'conf', script: 'osc' });
    expect(args.slice(args.indexOf('--osc=no'), args.indexOf('--'))).toEqual(['--osc=no', '--include=conf', '--script=osc']);
  });
});

describe('mpvAssetsIn', () => {
  it('points at the config and OSC shipped in desktop/mpv', () => {
    const assets = mpvAssetsIn(fileURLToPath(new URL('../../', import.meta.url)));
    expect(existsSync(assets.config)).toBe(true);
    expect(existsSync(assets.script)).toBe(true);
  });
});
