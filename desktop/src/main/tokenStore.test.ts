import { mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { parseStoredTokens, safeStorageCipher, serverKey, TokenStore, type SecretCipher, type StoredTokens } from './tokenStore.js';

/** Reversible stand-in for safeStorage: tags and reverses the bytes so plaintext never appears in the file. */
const seal = Buffer.from('sealed:');

class FakeCipher implements SecretCipher {
  available = true;
  isAvailable(): boolean {
    return this.available;
  }
  encrypt(plain: string): Buffer {
    return Buffer.concat([seal, Buffer.from(plain).reverse()]);
  }
  decrypt(data: Buffer): string {
    if (!data.subarray(0, seal.length).equals(seal)) throw new Error('Error while decrypting the ciphertext provided to safeStorage.');
    return Buffer.from(data.subarray(seal.length)).reverse().toString();
  }
}

const tokens = (tag: string): StoredTokens => ({
  accessToken: `access-${tag}`,
  accessTokenExpiresAt: '2026-10-10T12:15:00Z',
  refreshToken: `refresh-${tag}`,
  refreshTokenExpiresAt: '2026-11-09T12:00:00Z',
});

let dir: string;
let file: string;

beforeEach(async () => {
  dir = await mkdtemp(path.join(tmpdir(), 'mediaman-tokens-'));
  file = path.join(dir, 'nested', 'servers.json');
});

afterEach(async () => {
  await rm(dir, { recursive: true, force: true });
});

describe('TokenStore', () => {
  it('keeps tokens across restarts, encrypted on disk', async () => {
    const cipher = new FakeCipher();
    await new TokenStore(file, cipher).set('http://127.0.0.1:8420', tokens('a'));

    const restarted = new TokenStore(file, cipher);

    await expect(restarted.get('http://127.0.0.1:8420/')).resolves.toEqual(tokens('a'));
    const raw = await readFile(file, 'utf8');
    expect(raw).not.toContain('access-a');
    expect(raw).not.toContain('refresh-a');
  });

  it('keeps each server separate', async () => {
    const store = new TokenStore(file, new FakeCipher());
    await store.set('http://127.0.0.1:8420', tokens('local'));
    await store.set('https://media.example/mediaman/', tokens('remote'));

    await store.clear('http://127.0.0.1:8420');

    const restarted = new TokenStore(file, new FakeCipher());
    await expect(restarted.get('http://127.0.0.1:8420')).resolves.toBeUndefined();
    await expect(restarted.get('https://media.example/mediaman')).resolves.toEqual(tokens('remote'));
  });

  it('keeps tokens in memory only when encryption is unavailable', async () => {
    const cipher = new FakeCipher();
    cipher.available = false;
    const warnings: string[] = [];
    const store = new TokenStore(file, cipher, (line) => warnings.push(line));

    await store.set('http://127.0.0.1:8420', tokens('a'));

    await expect(store.get('http://127.0.0.1:8420')).resolves.toEqual(tokens('a'));
    await expect(readFile(file, 'utf8')).rejects.toThrow();
    expect(warnings).toEqual([expect.stringMatching(/sign in again/)]);
  });

  it('drops entries it can not decrypt and starts empty on a damaged file', async () => {
    await new TokenStore(file, new FakeCipher()).set('http://good', tokens('good'));
    const saved = JSON.parse(await readFile(file, 'utf8')) as { servers: Record<string, string> };
    saved.servers['http://bad'] = Buffer.from('not sealed').toString('base64');
    await writeFile(file, JSON.stringify(saved));

    await expect(new TokenStore(file, new FakeCipher()).get('http://bad')).resolves.toBeUndefined();
    await expect(new TokenStore(file, new FakeCipher()).get('http://good')).resolves.toEqual(tokens('good'));

    await writeFile(file, '{ not json');
    await expect(new TokenStore(file, new FakeCipher()).get('http://good')).resolves.toBeUndefined();
  });
});

describe('serverKey', () => {
  it('normalises the origin and drops a trailing slash', () => {
    expect(serverKey('HTTP://LocalHost:8420/')).toBe('http://localhost:8420');
    expect(serverKey('https://media.example/mediaman/')).toBe('https://media.example/mediaman');
  });
});

describe('safeStorageCipher', () => {
  const safeStorage = (backend: string) => ({
    isEncryptionAvailable: () => true,
    getSelectedStorageBackend: () => backend,
    encryptString: (plain: string) => Buffer.from(plain),
    decryptString: (data: Buffer) => data.toString(),
  });

  it("treats Linux's basic_text backend as unavailable", () => {
    expect(safeStorageCipher(safeStorage('basic_text'), 'linux').isAvailable()).toBe(false);
    expect(safeStorageCipher(safeStorage('gnome_libsecret'), 'linux').isAvailable()).toBe(true);
    expect(safeStorageCipher(safeStorage('unknown'), 'win32').isAvailable()).toBe(true);
  });
});

describe('parseStoredTokens', () => {
  it('accepts the AuthTokens shape', () => {
    expect(parseStoredTokens(tokens('a'))).toEqual(tokens('a'));
  });

  it.each([
    ['null', null],
    ['an array', []],
    ['a missing field', { ...tokens('a'), refreshToken: undefined }],
    ['an empty token', { ...tokens('a'), accessToken: '' }],
    ['an extra field', { ...tokens('a'), extra: 'x' }],
    ['a bad date', { ...tokens('a'), refreshTokenExpiresAt: 'soon' }],
    ['an oversized token', { ...tokens('a'), accessToken: 'x'.repeat(8_193) }],
  ])('rejects %s', (_, value) => {
    expect(parseStoredTokens(value)).toBeUndefined();
  });
});
