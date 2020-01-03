using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;

namespace FilesEncodingScaner
{
    public static class EncodingManager
    {
        private static readonly EncoderExceptionFallback __EncoderExceptionFallback = new EncoderExceptionFallback();
        private static readonly DecoderExceptionFallback __DecoderExceptionFallback = new DecoderExceptionFallback();
        private static readonly EncoderReplacementFallback __EncoderReplacementFallback = new EncoderReplacementFallback("?");
        private static readonly DecoderReplacementFallback __DecoderReplacementFallback = new DecoderReplacementFallback("?");

        [SuppressMessage("ReSharper", "CommentTypo")]
        private static readonly Encoding[] __Encodings =
        {
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),        //UTF-8 без BOM
            //Encoding.UTF8,                                                                              //UTF-8 с BOM
            //Encoding.Unicode,                                                                           //UTF-16 LE
            //Encoding.BigEndianUnicode,                                                                  //UTF-16 BE
            //Encoding.UTF32,                                                                             //UTF-32 LE
            //new UTF32Encoding(bigEndian: true, byteOrderMark: true),                                    //UTF-32 BE
            //Encoding.GetEncoding("windows-1251", __EncoderExceptionFallback, __DecoderExceptionFallback),//CP1251 - Windows-1251
            Encoding.GetEncoding(1251, __EncoderExceptionFallback, __DecoderExceptionFallback),//CP1251 - Windows-1251
            Encoding.GetEncoding(20866, __EncoderExceptionFallback, __DecoderExceptionFallback),        //KOI8-R
            Encoding.GetEncoding(21866, __EncoderExceptionFallback, __DecoderExceptionFallback)         //KOI8-U
        };

        public static Encoding GetEncoding(this FileInfo File)
        {
            if (!File.Exists || File.Length == 0) return null;

            const int buffer_max_length = 1 << 20;
            var bytes = new byte[Math.Min(buffer_max_length, File.Length)];
            using (var file = File.OpenRead())
                if (file.Read(bytes, 0, bytes.Length) != bytes.Length)
                    return null;

            if (bytes.All(b => b < 128))
                return Encoding.ASCII;

            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return Encoding.UTF8;

            if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0 && bytes[3] == 0)
                return Encoding.UTF32;

            if (bytes.Length >= 4 && bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 0xFE && bytes[3] == 0xFF)
                return new UTF32Encoding(true, false);

            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return Encoding.Unicode;

            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return Encoding.BigEndianUnicode;

            foreach (var encoding in __Encodings)
                try
                {
                    encoding.GetString(bytes);
                    return Encoding.GetEncoding(encoding.CodePage, __EncoderReplacementFallback, __DecoderReplacementFallback);
                }
                catch
                {
                    // ignored
                }

            return null;
        }
    }
}
