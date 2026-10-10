using System;

namespace dndhelper.Utils
{
    /// <summary>
    /// Identifies a file by its leading bytes; the client's file name and content type are never trusted.
    /// Allowlists only: SVG, HTML and anything scriptable stay out.
    /// </summary>
    public static class FileSniffer
    {
        public const int HeadLength = 12;

        public static (string ContentType, string Extension)? Image(ReadOnlySpan<byte> h)
        {
            if (h.Length < HeadLength) return null;
            if (h[0] == 0x89 && h[1] == 'P' && h[2] == 'N' && h[3] == 'G') return ("image/png", ".png");
            if (h[0] == 0xFF && h[1] == 0xD8 && h[2] == 0xFF) return ("image/jpeg", ".jpg");
            if (h[0] == 'G' && h[1] == 'I' && h[2] == 'F' && h[3] == '8') return ("image/gif", ".gif");
            if (Is(h, 0, "RIFF") && Is(h, 8, "WEBP")) return ("image/webp", ".webp");
            return null;
        }

        public static (string ContentType, string Extension)? Audio(ReadOnlySpan<byte> h)
        {
            if (h.Length < HeadLength) return null;
            // MP3: ID3 tag, or a bare MPEG frame sync with layer bits set (layer 00 is AAC ADTS, not MP3)
            if (Is(h, 0, "ID3") || (h[0] == 0xFF && (h[1] & 0xE0) == 0xE0 && (h[1] & 0x06) != 0)) return ("audio/mpeg", ".mp3");
            if (Is(h, 0, "OggS")) return ("audio/ogg", ".ogg");
            if (Is(h, 0, "fLaC")) return ("audio/flac", ".flac");
            if (Is(h, 0, "RIFF") && Is(h, 8, "WAVE")) return ("audio/wav", ".wav");
            if (Is(h, 4, "ftypM4A")) return ("audio/mp4", ".m4a");
            return null;
        }

        public static (string ContentType, string Extension)? Document(ReadOnlySpan<byte> h)
        {
            if (h.Length < HeadLength) return null;
            if (Is(h, 0, "%PDF-")) return ("application/pdf", ".pdf");
            return null;
        }

        private static bool Is(ReadOnlySpan<byte> h, int at, string ascii)
        {
            if (h.Length < at + ascii.Length) return false;
            for (var i = 0; i < ascii.Length; i++)
                if (h[at + i] != ascii[i]) return false;
            return true;
        }
    }
}
