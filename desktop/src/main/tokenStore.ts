// Signed-in tokens per server, encrypted with Electron safeStorage so the desktop app stays signed in
// (SPEC §8 accounts [M], §9; .claude/rules/desktop.md). Never in plain files or localStorage.
import { mkdir, readFile, rename, writeFile } from 'node:fs/promises';
import path from 'node:path';

/** The contract's `AuthTokens`. */
export interface StoredTokens {
  accessToken: string;
  accessTokenExpiresAt: string;
  refreshToken: string;
  refreshTokenExpiresAt: string;
}

export interface SecretCipher {
  /** False when the OS offers no real encryption; tokens then live in memory only. */
  isAvailable(): boolean;
  encrypt(plain: string): Buffer;
  decrypt(data: Buffer): string;
}

/** The parts of Electron's `safeStorage` the cipher uses. */
export interface SafeStorageLike {
  isEncryptionAvailable(): boolean;
  getSelectedStorageBackend?(): string;
  encryptString(plain: string): Buffer;
  decryptString(data: Buffer): string;
}

/** Wraps `safeStorage`. Linux's `basic_text` backend uses a hard-coded key, so it counts as unavailable. */
export function safeStorageCipher(safeStorage: SafeStorageLike, platform: NodeJS.Platform = process.platform): SecretCipher {
  return {
    isAvailable: () =>
      safeStorage.isEncryptionAvailable() &&
      !(platform === 'linux' && safeStorage.getSelectedStorageBackend?.() === 'basic_text'),
    encrypt: (plain) => safeStorage.encryptString(plain),
    decrypt: (data) => safeStorage.decryptString(data),
  };
}

/** The key a server's tokens are stored under: its origin plus any path prefix, without a trailing slash. */
export function serverKey(baseUrl: string): string {
  const url = new URL(baseUrl);
  return `${url.origin}${url.pathname.replace(/\/+$/, '')}`;
}

interface TokenFile {
  version: 1;
  /** Server key → base64 ciphertext of the JSON tokens. */
  servers: Record<string, string>;
}

export class TokenStore {
  private cache: Map<string, StoredTokens> | undefined;
  private writes: Promise<void> = Promise.resolve();
  private warned = false;

  constructor(
    private readonly filePath: string,
    private readonly cipher: SecretCipher,
    private readonly log: (line: string) => void = (line) => {
      console.warn(line);
    },
  ) {}

  async get(baseUrl: string): Promise<StoredTokens | undefined> {
    return (await this.load()).get(serverKey(baseUrl));
  }

  async set(baseUrl: string, tokens: StoredTokens): Promise<void> {
    (await this.load()).set(serverKey(baseUrl), tokens);
    await this.save();
  }

  async clear(baseUrl: string): Promise<void> {
    if ((await this.load()).delete(serverKey(baseUrl))) await this.save();
  }

  private async load(): Promise<Map<string, StoredTokens>> {
    if (this.cache) return this.cache;
    const tokens = new Map<string, StoredTokens>();
    if (this.encryptionAvailable()) {
      for (const [key, sealed] of Object.entries((await this.readFile())?.servers ?? {})) {
        try {
          const parsed = parseStoredTokens(JSON.parse(this.cipher.decrypt(Buffer.from(sealed, 'base64'))));
          if (parsed) tokens.set(key, parsed);
        } catch {
          // Sealed by another OS account or a damaged file: the user signs in again.
        }
      }
    }
    this.cache ??= tokens;
    return this.cache;
  }

  private save(): Promise<void> {
    const write = this.writes.then(async () => {
      if (!this.cache || !this.encryptionAvailable()) return;
      const file: TokenFile = { version: 1, servers: {} };
      for (const [key, tokens] of this.cache) {
        file.servers[key] = this.cipher.encrypt(JSON.stringify(tokens)).toString('base64');
      }
      await mkdir(path.dirname(this.filePath), { recursive: true });
      const temp = `${this.filePath}.tmp`;
      await writeFile(temp, JSON.stringify(file), { mode: 0o600 });
      await rename(temp, this.filePath);
    });
    this.writes = write.catch(() => undefined);
    return write;
  }

  private async readFile(): Promise<TokenFile | undefined> {
    try {
      const parsed = JSON.parse(await readFile(this.filePath, 'utf8')) as { version?: unknown; servers?: unknown } | null;
      if (parsed?.version !== 1 || typeof parsed.servers !== 'object' || parsed.servers === null) return undefined;
      const servers = Object.entries(parsed.servers as Record<string, unknown>).filter(
        (entry): entry is [string, string] => typeof entry[1] === 'string',
      );
      return { version: 1, servers: Object.fromEntries(servers) };
    } catch {
      return undefined;
    }
  }

  private encryptionAvailable(): boolean {
    if (this.cipher.isAvailable()) return true;
    if (!this.warned) {
      this.warned = true;
      this.log('Secure storage is not available on this system, so you will need to sign in again after restarting MediaMan.');
    }
    return false;
  }
}

const maxTokenLength = 8_192;

/** Accepts exactly the contract's `AuthTokens` shape: non-empty strings within a sane length, valid expiry dates. */
export function parseStoredTokens(value: unknown): StoredTokens | undefined {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return undefined;
  const record = value as Record<string, unknown>;
  const fields = ['accessToken', 'accessTokenExpiresAt', 'refreshToken', 'refreshTokenExpiresAt'] as const;
  if (Object.keys(record).some((key) => !(fields as readonly string[]).includes(key))) return undefined;
  for (const field of fields) {
    const item = record[field];
    if (typeof item !== 'string' || item.length === 0 || item.length > maxTokenLength) return undefined;
  }
  const tokens = record as unknown as StoredTokens;
  if (Number.isNaN(Date.parse(tokens.accessTokenExpiresAt)) || Number.isNaN(Date.parse(tokens.refreshTokenExpiresAt))) {
    return undefined;
  }
  return {
    accessToken: tokens.accessToken,
    accessTokenExpiresAt: tokens.accessTokenExpiresAt,
    refreshToken: tokens.refreshToken,
    refreshTokenExpiresAt: tokens.refreshTokenExpiresAt,
  };
}
