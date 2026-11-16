// Test images for test_checkdeposit.sh:  node make_image.js <out> <kind> [seed]
//   png   a real (tiny) PNG; the seed changes one pixel, so every seed has a different SHA-256
//   jpg   bytes that start with the JPEG signature (FF D8 FF), which is all the server checks
//   gif   a GIF (not allowed)            text  a plain text file (save it as .jpg to fake the type)
//   big   a PNG signature followed by padding, 5 MB + 1 byte (over the limit)
const fs = require('fs');
const zlib = require('zlib');
const [out, kind, seedArg] = process.argv.slice(2);
const seed = parseInt(seedArg || '1', 10);

function crc32(buf) {
  let c, crc = 0xFFFFFFFF;
  for (const b of buf) { c = (crc ^ b) & 0xFF; for (let k = 0; k < 8; k++) c = c & 1 ? 0xEDB88320 ^ (c >>> 1) : c >>> 1; crc = (crc >>> 8) ^ c; }
  return (crc ^ 0xFFFFFFFF) >>> 0;
}
function chunk(type, data) {
  const len = Buffer.alloc(4); len.writeUInt32BE(data.length);
  const body = Buffer.concat([Buffer.from(type, 'ascii'), data]);
  const crc = Buffer.alloc(4); crc.writeUInt32BE(crc32(body));
  return Buffer.concat([len, body, crc]);
}
function png(w, h) {
  const raw = Buffer.alloc(h * (w * 3 + 1), 0xEE);
  for (let y = 0; y < h; y++) raw[y * (w * 3 + 1)] = 0;               // filter byte
  const i = 1 + (seed % (w * 3));                                       // one pixel channel depends on the seed
  raw[i] = seed % 256; raw[i + 1] = (seed >> 8) % 256;
  const ihdr = Buffer.alloc(13); ihdr.writeUInt32BE(w, 0); ihdr.writeUInt32BE(h, 4); ihdr[8] = 8; ihdr[9] = 2;
  return Buffer.concat([Buffer.from([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]),
    chunk('IHDR', ihdr), chunk('IDAT', zlib.deflateSync(raw)), chunk('IEND', Buffer.alloc(0))]);
}

let data;
switch (kind) {
  case 'png': data = png(24, 11); break;
  case 'jpg': data = Buffer.concat([Buffer.from([0xFF, 0xD8, 0xFF, 0xE0]), Buffer.from(`fake jpeg body ${seed}`)]); break;
  case 'gif': data = Buffer.from(`GIF89a not allowed ${seed}`); break;
  case 'text': data = Buffer.from(`This is a text file pretending to be a photo ${seed}\n`); break;
  case 'big': data = Buffer.concat([Buffer.from([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]), Buffer.alloc(5 * 1024 * 1024 + 1 - 8, 0x41)]); break;
  default: throw new Error('kind must be png, jpg, gif, text or big');
}
fs.writeFileSync(out, data);
