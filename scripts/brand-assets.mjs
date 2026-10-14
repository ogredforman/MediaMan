// Generates the brand assets (PLAN 2.5.3b) from the logo source, so they can be regenerated after the logo changes.
//
//   npm run brand   →   assets/brand/* (committed) and dist/brand-preview.png (not committed)
//
// How they are made:
// 1. Source: assets/brand/mediaman-logo.png, a 1408×768 TV with "MM" on a transparent background. It carries
//    faint stray pixels (alpha 1–3) out to the corners, so pixels below ALPHA_THRESHOLD are cleared first;
//    the content box is then found from the alpha channel, not hard-coded.
// 2. Square master: the content box centred on a transparent square with MASTER_MARGIN px on the long side,
//    at the source's own resolution (no upscaling). Every square size below is a Lanczos downscale of it.
// 3. Wide logo: the content box alone, for the README.
// 4. .ico files: written by icoFile() below; sizes under 256 are 32-bit BGRA BMP entries (most compatible with
//    Windows shell, rcedit and NSIS), 256 is a PNG entry.
// 5. Touch icon: opaque on the theme background (#0E0B14, SPEC §11), since iOS fills transparency with black.
// 6. Preview: every square size at 1× and enlarged ×4 on the dark background (#0E0B14), the surface (#17121F)
//    and white. Look at it after regenerating to check the small sizes stay legible on the dark theme.
//    Checked 2026-10-14: the TV reads at 16 px but "MM" only from 24–32 px up; see PLAN Parked for a
//    screen-only crop at 16/24 px if that bites in use.
//
// Which file is used where: docs/CODEMAP.md (Brand assets).

import { mkdir, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import sharp from 'sharp';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const brandDir = path.join(root, 'assets', 'brand');
const source = path.join(brandDir, 'mediaman-logo.png');
const previewFile = path.join(root, 'dist', 'brand-preview.png');

const ALPHA_THRESHOLD = 4;
const MASTER_MARGIN = 12;
const THEME_BACKGROUND = '#0E0B14';
const THEME_SURFACE = '#17121F';
const TRANSPARENT = { r: 0, g: 0, b: 0, alpha: 0 };

const appIconSizes = [16, 20, 24, 32, 40, 48, 64, 256];
const faviconSizes = [16, 32, 48];
const touchIcon = { size: 180, inset: 0.1 };

/** Loads the source with stray low-alpha pixels cleared, and finds the box around what is left. */
async function loadSource() {
  const { data, info } = await sharp(source).ensureAlpha().raw().toBuffer({ resolveWithObject: true });
  let left = info.width;
  let top = info.height;
  let right = -1;
  let bottom = -1;
  for (let y = 0; y < info.height; y++) {
    for (let x = 0; x < info.width; x++) {
      const alpha = (y * info.width + x) * 4 + 3;
      if (data[alpha] < ALPHA_THRESHOLD) {
        data.fill(0, alpha - 3, alpha + 1);
        continue;
      }
      left = Math.min(left, x);
      right = Math.max(right, x);
      top = Math.min(top, y);
      bottom = Math.max(bottom, y);
    }
  }
  if (right < 0) throw new Error(`${source} has no visible pixels.`);
  const box = { left, top, width: right - left + 1, height: bottom - top + 1 };
  const raw = { raw: { width: info.width, height: info.height, channels: /** @type {const} */ (4) } };
  return { data, raw, box };
}

const png = (image) => image.png({ compressionLevel: 9 }).toBuffer();

/** A square, transparent PNG of the master at the given size. */
const squareAt = (master, size) => png(sharp(master).resize(size, size, { kernel: 'lanczos3' }));

/** An .ico holding each size: BMP entries under 256 px, a PNG entry at 256. */
async function icoFile(master, sizes) {
  const images = [];
  for (const size of sizes) {
    if (size >= 256) {
      images.push({ size, bytes: await squareAt(master, size) });
      continue;
    }
    const rgba = await sharp(master).resize(size, size, { kernel: 'lanczos3' }).raw().toBuffer();
    const header = Buffer.alloc(40);
    header.writeUInt32LE(40, 0);
    header.writeInt32LE(size, 4);
    header.writeInt32LE(size * 2, 8); // XOR image plus AND mask
    header.writeUInt16LE(1, 12);
    header.writeUInt16LE(32, 14);
    const pixels = Buffer.alloc(size * size * 4);
    for (let y = 0; y < size; y++) {
      const row = (size - 1 - y) * size * 4; // BMP rows run bottom-up
      for (let x = 0; x < size; x++) {
        const from = (y * size + x) * 4;
        const to = row + x * 4;
        pixels[to] = rgba[from + 2];
        pixels[to + 1] = rgba[from + 1];
        pixels[to + 2] = rgba[from];
        pixels[to + 3] = rgba[from + 3];
      }
    }
    const mask = Buffer.alloc(Math.ceil(size / 32) * 4 * size); // all zero: alpha decides
    header.writeUInt32LE(pixels.length + mask.length, 20);
    images.push({ size, bytes: Buffer.concat([header, pixels, mask]) });
  }

  const directory = Buffer.alloc(6 + 16 * images.length);
  directory.writeUInt16LE(1, 2); // type: icon
  directory.writeUInt16LE(images.length, 4);
  let offset = directory.length;
  images.forEach(({ size, bytes }, i) => {
    const entry = 6 + 16 * i;
    directory.writeUInt8(size >= 256 ? 0 : size, entry);
    directory.writeUInt8(size >= 256 ? 0 : size, entry + 1);
    directory.writeUInt16LE(1, entry + 4);
    directory.writeUInt16LE(32, entry + 6);
    directory.writeUInt32LE(bytes.length, entry + 8);
    directory.writeUInt32LE(offset, entry + 12);
    offset += bytes.length;
  });
  return Buffer.concat([directory, ...images.map((image) => image.bytes)]);
}

async function previewSheet(master) {
  const rowHeight = 300;
  const backgrounds = [THEME_BACKGROUND, THEME_SURFACE, '#FFFFFF'];
  const layers = [];
  let width = 0;
  for (const [row, background] of backgrounds.entries()) {
    const top = row * rowHeight + 10;
    let x = 10;
    const tiles = [];
    for (const size of appIconSizes) {
      const icon = await squareAt(master, size);
      tiles.push({ input: icon, left: x, top });
      x += size + 10;
      if (size <= 64) {
        const enlarged = await png(sharp(icon).resize(size * 4, size * 4, { kernel: 'nearest' }));
        tiles.push({ input: enlarged, left: x, top });
        x += size * 4 + 30;
      }
    }
    width = Math.max(width, x);
    layers.push({ background, top: row * rowHeight, tiles });
  }
  const stripes = layers.map(({ background, top }) => ({
    input: { create: { width, height: rowHeight, channels: /** @type {const} */ (4), background } },
    left: 0,
    top,
  }));
  const canvas = sharp({
    create: { width, height: rowHeight * backgrounds.length, channels: 4, background: TRANSPARENT },
  });
  return png(canvas.composite([...stripes, ...layers.flatMap((layer) => layer.tiles)]));
}

async function main() {
  const { data, raw, box } = await loadSource();
  const content = await png(sharp(data, raw).extract(box));

  const side = Math.max(box.width, box.height) + 2 * MASTER_MARGIN;
  const padX = side - box.width;
  const padY = side - box.height;
  const master = await png(
    sharp(content).extend({
      left: Math.floor(padX / 2),
      right: Math.ceil(padX / 2),
      top: Math.floor(padY / 2),
      bottom: Math.ceil(padY / 2),
      background: TRANSPARENT,
    }),
  );

  const touchInner = Math.round(touchIcon.size * (1 - 2 * touchIcon.inset));
  const touchPad = touchIcon.size - touchInner;
  // Two pipelines: sharp runs flatten before extend within one, which would bring the alpha back.
  const touchPadded = await png(
    sharp(await squareAt(master, touchInner)).extend({
      left: Math.floor(touchPad / 2),
      right: Math.ceil(touchPad / 2),
      top: Math.floor(touchPad / 2),
      bottom: Math.ceil(touchPad / 2),
      background: TRANSPARENT,
    }),
  );
  const touch = await png(sharp(touchPadded).flatten({ background: THEME_BACKGROUND }));

  const outputs = {
    'mediaman-icon.png': master,
    'mediaman-wide.png': content,
    'mediaman.ico': await icoFile(master, appIconSizes),
    'icon-512.png': await squareAt(master, 512),
    'icon-256.png': await squareAt(master, 256),
    'favicon.ico': await icoFile(master, faviconSizes),
    'favicon-32.png': await squareAt(master, 32),
    'apple-touch-icon.png': touch,
  };
  for (const [name, bytes] of Object.entries(outputs)) {
    await writeFile(path.join(brandDir, name), bytes);
    console.log(`assets/brand/${name}`);
  }

  await mkdir(path.dirname(previewFile), { recursive: true });
  await writeFile(previewFile, await previewSheet(master));
  console.log(`dist/brand-preview.png (check the small sizes on the dark rows)`);
}

await main();
