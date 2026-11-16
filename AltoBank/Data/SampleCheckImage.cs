using System.IO.Compression;

namespace AltoBank.Data;

/// <summary>
/// Draws PLACEHOLDER check images for the demo data (no image library needed): rectangles, lines, a 5×7 pixel font
/// and a minimal PNG encoder. Every image says "SAMPLE - NOT A REAL CHECK" and has no real bank, routing or account
/// number on it.
/// </summary>
public static class SampleCheckImage
{
    private const int W = 1200, H = 540;

    public record Details(string Payer, string Payee, decimal Amount, string Number, DateOnly Date, string Memo);

    public static byte[] Front(Details d)
    {
        var c = new Canvas(W, H, Paper);
        c.Rect(0, 0, W, H, Border); c.Rect(6, 6, W - 12, H - 12, Paper);              // border
        c.Rect(6, 6, W - 12, 64, Band);                                                // header band
        c.Text(28, 24, "SAMPLE - NOT A REAL CHECK", 4, Warn);
        c.Text(W - 300, 24, $"NO. {d.Number}", 4, Ink);

        c.Text(36, 96, d.Payer.ToUpperInvariant(), 4, Ink);
        c.Text(36, 134, "123 EXAMPLE STREET, ANYTOWN", 3, Faint);
        c.Text(W - 440, 110, $"DATE {d.Date:MMM d yyyy}".ToUpperInvariant(), 4, Ink);

        c.Text(36, 214, "PAY TO THE", 3, Faint);
        c.Text(36, 240, "ORDER OF", 3, Faint);
        c.Text(240, 222, d.Payee.ToUpperInvariant(), 5, Ink);
        c.Line(240, 266, 840, Faint);
        c.Rect(860, 200, 300, 70, Border); c.Rect(864, 204, 292, 62, Paper);          // amount box
        c.Text(880, 220, $"${d.Amount:0.00}", 5, Ink);

        c.Text(36, 300, $"EXACTLY ${d.Amount:0.00} DOLLARS", 4, Ink);
        c.Line(36, 340, 1160, Faint);

        c.Text(36, 384, "NOT A REAL BANK - DEMO ONLY", 3, Faint);
        c.Text(36, 430, $"MEMO {d.Memo.ToUpperInvariant()}", 3, Ink);
        c.Line(36, 454, 520, Faint);
        c.Line(700, 454, 1160, Faint);
        c.Text(700, 466, "SIGNATURE", 2, Faint);
        c.Text(720, 414, d.Payer.ToUpperInvariant(), 3, Ink);

        c.Rect(6, H - 52, W - 12, 46, Band);
        c.Text(28, H - 40, "SAMPLE ONLY - NO ROUTING OR ACCOUNT NUMBER", 3, Warn);
        return c.ToPng();
    }

    /// <summary>The back. Endorsed = signed + "FOR MOBILE DEPOSIT ONLY AT ALTO BANK"; otherwise the area is empty.</summary>
    public static byte[] Back(Details d, bool endorsed)
    {
        var c = new Canvas(W, H, Paper);
        c.Rect(0, 0, W, H, Border); c.Rect(6, 6, W - 12, H - 12, Paper);
        c.Text(28, 24, "SAMPLE - NOT A REAL CHECK", 4, Warn);
        c.Text(W - 300, 24, $"NO. {d.Number}", 3, Faint);

        // Endorsement area on the left third, like a real check.
        c.Rect(36, 80, 420, 400, Faint); c.Rect(38, 82, 416, 396, Paper);
        c.Text(60, 100, "ENDORSE HERE", 3, Faint);
        foreach (var y in new[] { 182, 262, 340 }) c.Line(60, y, 430, Faint);
        if (endorsed)
        {
            c.Text(64, 146, d.Payee.ToUpperInvariant(), 4, Pen);
            c.Text(64, 206, "FOR MOBILE DEPOSIT", 3, Pen);
            c.Text(64, 232, "ONLY AT ALTO BANK", 3, Pen);
        }
        c.Text(60, 430, "DO NOT WRITE BELOW THIS LINE", 2, Faint);

        c.Text(560, 240, "SAMPLE", 8, Watermark);
        c.Text(560, 320, "DEMO ONLY", 5, Watermark);
        return c.ToPng();
    }

    // ---------- colors ----------
    private static readonly (byte R, byte G, byte B) Paper = (0xF3, 0xF5, 0xF0), Band = (0xE2, 0xE9, 0xF2),
        Border = (0x5A, 0x6B, 0x86), Ink = (0x1E, 0x2A, 0x3E), Faint = (0x8A, 0x96, 0xA8), Warn = (0xB3, 0x26, 0x1E),
        Pen = (0x1F, 0x3F, 0x9A), Watermark = (0xD6, 0xDC, 0xE6);

    // ---------- tiny raster + PNG ----------
    private sealed class Canvas
    {
        private readonly int _w, _h;
        private readonly byte[] _px;   // RGB

        public Canvas(int w, int h, (byte R, byte G, byte B) fill)
        {
            _w = w; _h = h; _px = new byte[w * h * 3];
            Rect(0, 0, w, h, fill);
        }

        public void Rect(int x, int y, int w, int h, (byte R, byte G, byte B) c)
        {
            for (var yy = Math.Max(0, y); yy < Math.Min(_h, y + h); yy++)
                for (var xx = Math.Max(0, x); xx < Math.Min(_w, x + w); xx++)
                {
                    var i = (yy * _w + xx) * 3;
                    _px[i] = c.R; _px[i + 1] = c.G; _px[i + 2] = c.B;
                }
        }

        public void Line(int x1, int y, int x2, (byte R, byte G, byte B) c) => Rect(x1, y, x2 - x1, 2, c);

        public void Text(int x, int y, string text, int scale, (byte R, byte G, byte B) c)
        {
            foreach (var ch in text)
            {
                if (Font.TryGetValue(ch, out var rows))
                    for (var r = 0; r < 7; r++)
                        for (var col = 0; col < 5; col++)
                            if (rows[r][col] == '1') Rect(x + col * scale, y + r * scale, scale, scale, c);
                x += 6 * scale;
            }
        }

        public byte[] ToPng()
        {
            // Raw scanlines: filter byte 0, then RGB.
            var raw = new byte[_h * (_w * 3 + 1)];
            for (var y = 0; y < _h; y++)
                Buffer.BlockCopy(_px, y * _w * 3, raw, y * (_w * 3 + 1) + 1, _w * 3);
            using var z = new MemoryStream();
            using (var zs = new ZLibStream(z, CompressionLevel.Optimal, leaveOpen: true)) zs.Write(raw);

            using var png = new MemoryStream();
            png.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
            var ihdr = new byte[13];
            WriteInt(ihdr, 0, _w); WriteInt(ihdr, 4, _h);
            ihdr[8] = 8; ihdr[9] = 2;   // 8-bit, RGB
            Chunk(png, "IHDR", ihdr);
            Chunk(png, "IDAT", z.ToArray());
            Chunk(png, "IEND", Array.Empty<byte>());
            return png.ToArray();
        }

        private static void Chunk(Stream s, string type, byte[] data)
        {
            var len = new byte[4]; WriteInt(len, 0, data.Length); s.Write(len);
            var body = new byte[4 + data.Length];
            System.Text.Encoding.ASCII.GetBytes(type).CopyTo(body, 0);
            data.CopyTo(body, 4);
            s.Write(body);
            var crc = new byte[4]; WriteInt(crc, 0, (int)Crc32(body)); s.Write(crc);
        }

        private static void WriteInt(byte[] b, int o, int v)
        {
            b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v;
        }

        private static uint Crc32(byte[] data)
        {
            var crc = 0xFFFFFFFFu;
            foreach (var b in data)
            {
                crc ^= b;
                for (var k = 0; k < 8; k++) crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
            }
            return ~crc;
        }
    }

    // 5×7 pixel font (rows top to bottom).
    private static readonly Dictionary<char, string[]> Font = new()
    {
        ['A'] = new[] { "01110", "10001", "10001", "11111", "10001", "10001", "10001" },
        ['B'] = new[] { "11110", "10001", "10001", "11110", "10001", "10001", "11110" },
        ['C'] = new[] { "01110", "10001", "10000", "10000", "10000", "10001", "01110" },
        ['D'] = new[] { "11110", "10001", "10001", "10001", "10001", "10001", "11110" },
        ['E'] = new[] { "11111", "10000", "10000", "11110", "10000", "10000", "11111" },
        ['F'] = new[] { "11111", "10000", "10000", "11110", "10000", "10000", "10000" },
        ['G'] = new[] { "01110", "10001", "10000", "10111", "10001", "10001", "01111" },
        ['H'] = new[] { "10001", "10001", "10001", "11111", "10001", "10001", "10001" },
        ['I'] = new[] { "01110", "00100", "00100", "00100", "00100", "00100", "01110" },
        ['J'] = new[] { "00111", "00010", "00010", "00010", "00010", "10010", "01100" },
        ['K'] = new[] { "10001", "10010", "10100", "11000", "10100", "10010", "10001" },
        ['L'] = new[] { "10000", "10000", "10000", "10000", "10000", "10000", "11111" },
        ['M'] = new[] { "10001", "11011", "10101", "10101", "10001", "10001", "10001" },
        ['N'] = new[] { "10001", "10001", "11001", "10101", "10011", "10001", "10001" },
        ['O'] = new[] { "01110", "10001", "10001", "10001", "10001", "10001", "01110" },
        ['P'] = new[] { "11110", "10001", "10001", "11110", "10000", "10000", "10000" },
        ['Q'] = new[] { "01110", "10001", "10001", "10001", "10101", "10010", "01101" },
        ['R'] = new[] { "11110", "10001", "10001", "11110", "10100", "10010", "10001" },
        ['S'] = new[] { "01111", "10000", "10000", "01110", "00001", "00001", "11110" },
        ['T'] = new[] { "11111", "00100", "00100", "00100", "00100", "00100", "00100" },
        ['U'] = new[] { "10001", "10001", "10001", "10001", "10001", "10001", "01110" },
        ['V'] = new[] { "10001", "10001", "10001", "10001", "10001", "01010", "00100" },
        ['W'] = new[] { "10001", "10001", "10001", "10101", "10101", "10101", "01010" },
        ['X'] = new[] { "10001", "10001", "01010", "00100", "01010", "10001", "10001" },
        ['Y'] = new[] { "10001", "10001", "01010", "00100", "00100", "00100", "00100" },
        ['Z'] = new[] { "11111", "00001", "00010", "00100", "01000", "10000", "11111" },
        ['0'] = new[] { "01110", "10001", "10011", "10101", "11001", "10001", "01110" },
        ['1'] = new[] { "00100", "01100", "00100", "00100", "00100", "00100", "01110" },
        ['2'] = new[] { "01110", "10001", "00001", "00010", "00100", "01000", "11111" },
        ['3'] = new[] { "11111", "00010", "00100", "00010", "00001", "10001", "01110" },
        ['4'] = new[] { "00010", "00110", "01010", "10010", "11111", "00010", "00010" },
        ['5'] = new[] { "11111", "10000", "11110", "00001", "00001", "10001", "01110" },
        ['6'] = new[] { "00110", "01000", "10000", "11110", "10001", "10001", "01110" },
        ['7'] = new[] { "11111", "00001", "00010", "00100", "01000", "01000", "01000" },
        ['8'] = new[] { "01110", "10001", "10001", "01110", "10001", "10001", "01110" },
        ['9'] = new[] { "01110", "10001", "10001", "01111", "00001", "00010", "01100" },
        ['-'] = new[] { "00000", "00000", "00000", "11111", "00000", "00000", "00000" },
        ['.'] = new[] { "00000", "00000", "00000", "00000", "00000", "01100", "01100" },
        [','] = new[] { "00000", "00000", "00000", "00000", "01100", "00100", "01000" },
        ['$'] = new[] { "00100", "01111", "10100", "01110", "00101", "11110", "00100" },
        [':'] = new[] { "00000", "01100", "01100", "00000", "01100", "01100", "00000" },
        ['/'] = new[] { "00001", "00010", "00010", "00100", "01000", "01000", "10000" },
        ['#'] = new[] { "01010", "01010", "11111", "01010", "11111", "01010", "01010" },
        ['&'] = new[] { "01100", "10010", "10100", "01000", "10101", "10010", "01101" },
        ['\''] = new[] { "00100", "00100", "01000", "00000", "00000", "00000", "00000" },
        [' '] = new[] { "00000", "00000", "00000", "00000", "00000", "00000", "00000" },
    };
}
