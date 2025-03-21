// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Buffers;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.XUnitExtensions;
using Newtonsoft.Json;
using Xunit;

namespace System.Text.Json.Tests
{
    public partial class Utf8JsonWriterTests
    {
        private const int MaxExpansionFactorWhileEscaping = 6;
        private const int MaxEscapedTokenSize = 1_000_000_000;   // Max size for already escaped value.
        private const int MaxUnescapedTokenSize = MaxEscapedTokenSize / MaxExpansionFactorWhileEscaping;  // 166_666_666 bytes

        private const string ExpectedIndentedCommentJsonOfArray = """
/*Comment at start of doc*/
/*Multiple comment line*/
[
  /*Comment as first array item*/
  /*Multiple comment line*/
  []
  /*Comment in the middle of array*/,
  {}
  /*Comment as the last array item*/
]
/*Comment at end of doc*/
""";

        private const string ExpectedNonIndentedCommentJsonOfArray = "/*Comment at start of doc*//*Multiple comment line*/[/*Comment as first array item*//*Multiple comment line*/[]/*Comment in the middle of array*/,{}/*Comment as the last array item*/]/*Comment at end of doc*/";

        private const string ExpectedIndentedCommentJsonOfObject = """
/*Comment at start of doc*/
/*Multiple comment line*/
{
  /*Comment before first object property*/
  /*Multiple comment line*/
  "property1": 
  /*Comment of string property value*/"stringValue",
  "property2": 
  /*Comment of object property value*/
  {}
  /*Comment in the middle of object*/,
  "property3": 
  /*Comment of array property value*/
  []
  /*Comment after the last property*/
}
/*Comment at end of doc*/
""";

        private const string ExpectedNonIndentedCommentJsonOfObject = """
/*Comment at start of doc*//*Multiple comment line*/{/*Comment before first object property*//*Multiple comment line*/"property1":/*Comment of string property value*/"stringValue","property2":/*Comment of object property value*/{}/*Comment in the middle of object*/,"property3":/*Comment of array property value*/[]/*Comment after the last property*/}/*Comment at end of doc*/
""";

        public static IEnumerable<object[]> JsonOptions_TestData() =>
            from options in JsonOptions()
            select new object[] { options };

        [Theory]
        [MemberData(nameof(JsonOptions_TestData))]
        public void NullCtor(JsonWriterOptions options)
        {
            Assert.Throws<ArgumentNullException>(() => new Utf8JsonWriter((Stream)null));
            Assert.Throws<ArgumentNullException>(() => new Utf8JsonWriter((IBufferWriter<byte>)null));
            Assert.Throws<ArgumentNullException>(() => new Utf8JsonWriter((Stream)null, options));
            Assert.Throws<ArgumentNullException>(() => new Utf8JsonWriter((IBufferWriter<byte>)null, options));
        }

        [Theory]
        [MemberData(nameof(JsonOptions_TestData))]
        public void CantWriteToNonWritableStream(JsonWriterOptions options)
        {
            var stream = new MemoryStream();
            stream.Dispose();

            Assert.Throws<ArgumentException>(() => new Utf8JsonWriter(stream));
            Assert.Throws<ArgumentException>(() => new Utf8JsonWriter(stream, options));
        }

        [Fact]
        public static void WritingNullStringsWithCustomEscaping()
        {
            var writerOptions = new JsonWriterOptions();
            WriteNullStringsHelper(writerOptions);

            writerOptions = new JsonWriterOptions { Encoder = JavaScriptEncoder.Default };
            WriteNullStringsHelper(writerOptions);

            writerOptions = new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
            WriteNullStringsHelper(writerOptions);
        }

        [Fact]
        public static void WritingNullStringsWithBuggyJavascriptEncoder()
        {
            var writerOptions = new JsonWriterOptions { Encoder = new BuggyJavaScriptEncoder() };
            WriteNullStringsHelper(writerOptions);
        }

        private static void WriteNullStringsHelper(JsonWriterOptions writerOptions)
        {
            var output = new ArrayBufferWriter<byte>();
            string str = null;

            using (var writer = new Utf8JsonWriter(output, writerOptions))
            {
                writer.WriteStringValue(str);
            }
            JsonTestHelper.AssertContents("null", output);

            output.Clear();
            using (var writer = new Utf8JsonWriter(output, writerOptions))
            {
                writer.WriteStringValue(str.AsSpan());
            }
            JsonTestHelper.AssertContents("\"\"", output);

            byte[] utf8Str = null;
            output.Clear();
            using (var writer = new Utf8JsonWriter(output, writerOptions))
            {
                writer.WriteStringValue(utf8Str.AsSpan());
            }
            JsonTestHelper.AssertContents("\"\"", output);

            JsonEncodedText jsonText = JsonEncodedText.Encode(utf8Str.AsSpan());
            output.Clear();
            using (var writer = new Utf8JsonWriter(output, writerOptions))
            {
                writer.WriteStringValue(jsonText);
            }
            JsonTestHelper.AssertContents("\"\"", output);
        }

        public class BuggyJavaScriptEncoder : JavaScriptEncoder
        {
            public override int MaxOutputCharactersPerInputCharacter => throw new NotImplementedException();

            public override unsafe int FindFirstCharacterToEncode(char* text, int textLength)
            {
                // Access the text pointer even though it might be null and text length is 0.
                return *text;
            }

            public override unsafe bool TryEncodeUnicodeScalar(int unicodeScalar, char* buffer, int bufferLength, out int numberOfCharactersWritten)
            {
                numberOfCharactersWritten = 0;
                return false;
            }

            public override bool WillEncode(int unicodeScalar) => false;
        }

        [Fact]
        public static void WritingStringsWithCustomEscaping()
        {
            var output = new ArrayBufferWriter<byte>();
            var writerOptions = new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

            using (var writer = new Utf8JsonWriter(output))
            {
                writer.WriteStringValue("\u6D4B\u8A6611");
            }
            JsonTestHelper.AssertContents("\"\\u6D4B\\u8A6611\"", output);

            output.Clear();
            using (var writer = new Utf8JsonWriter(output, writerOptions))
            {
                writer.WriteStringValue("\u6D4B\u8A6611");
            }
            JsonTestHelper.AssertContents("\"\u6D4B\u8A6611\"", output);

            output.Clear();
            using (var writer = new Utf8JsonWriter(output))
            {
                writer.WriteStringValue("\u00E9\"");
            }
            JsonTestHelper.AssertContents("\"\\u00E9\\u0022\"", output);

            output.Clear();
            using (var writer = new Utf8JsonWriter(output, writerOptions))
            {
                writer.WriteStringValue("\u00E9\"");
            }
            JsonTestHelper.AssertContents("\"\u00E9\\\"\"", output);

            output.Clear();
            using (var writer = new Utf8JsonWriter(output))
            {
                writer.WriteStringValue("\u2020\"");
            }
            JsonTestHelper.AssertContents("\"\\u2020\\u0022\"", output);

            output.Clear();
            using (var writer = new Utf8JsonWriter(output, writerOptions))
            {
                writer.WriteStringValue("\u2020\"");
            }
            JsonTestHelper.AssertContents("\"\u2020\\\"\"", output);
        }

        [Theory]
        [MemberData(nameof(EscapingTestData))]
        public void EscapingTestWhileWriting(char replacementChar, JavaScriptEncoder encoder, bool requiresEscaping)
        {
            var writerOptions = new JsonWriterOptions { Encoder = encoder };

            {
                ReadOnlyMemory<byte> written = WriteStringHelper(writerOptions, null);
                Assert.Equal(-1, written.Span.IndexOf((byte)'\\'));

                written = WriteUtf8StringHelper(writerOptions, null);
                Assert.Equal(-1, written.Span.IndexOf((byte)'\\'));

                written = WriteStringHelper(writerOptions, string.Empty);
                Assert.Equal(-1, written.Span.IndexOf((byte)'\\'));

                written = WriteUtf8StringHelper(writerOptions, Array.Empty<byte>());
                Assert.Equal(-1, written.Span.IndexOf((byte)'\\'));

                written = WriteStringSegmentHelper(writerOptions, Array.Empty<char>());
                Assert.Equal(-1, written.Span.IndexOf((byte)'\\'));

                written = WriteUtf8StringSegmentHelper(writerOptions, Array.Empty<byte>());
                Assert.Equal(-1, written.Span.IndexOf((byte)'\\'));
            }

            var random = new Random(42);
            for (int dataLength = 0; dataLength < 50; dataLength++)
            {
                char[] str = new char[dataLength];
                for (int i = 0; i < dataLength; i++)
                {
                    str[i] = (char)random.Next(97, 123);
                }
                string baseStr = new string(str);
                byte[] sourceUtf8 = Encoding.UTF8.GetBytes(baseStr);

                ReadOnlyMemory<byte> written = WriteStringHelper(writerOptions, baseStr);
                Assert.Equal(-1, written.Span.IndexOf((byte)'\\'));

                written = WriteUtf8StringHelper(writerOptions, sourceUtf8);
                Assert.Equal(-1, written.Span.IndexOf((byte)'\\'));

                for (int i = 0; i < dataLength; i++)
                {
                    char[] changed = baseStr.ToCharArray();
                    changed[i] = replacementChar;
                    string newStr = new string(changed);
                    sourceUtf8 = Encoding.UTF8.GetBytes(newStr);

                    written = WriteStringHelper(writerOptions, newStr);
                    int escapedIndex = written.Span.IndexOf((byte)'\\');
                    Assert.Equal(requiresEscaping ? (i + 1) : -1, escapedIndex);  // Account for the start quote

                    written = WriteUtf8StringHelper(writerOptions, sourceUtf8);
                    escapedIndex = written.Span.IndexOf((byte)'\\');
                    Assert.Equal(requiresEscaping ? (i + 1) : -1, escapedIndex);  // Account for the start quote

                    if (dataLength < 10)
                    {
                        SplitStringDataHelper(newStr.AsSpan(), writerOptions, output =>
                        {
                            escapedIndex = output.WrittenSpan.IndexOf((byte)'\\');
                            Assert.Equal(requiresEscaping ? (i + 1) : -1, escapedIndex);  // Account for the start quote
                        }, StringValueEncodingType.Utf16);

                        SplitStringDataHelper(sourceUtf8, writerOptions, output =>
                        {
                            escapedIndex = output.WrittenSpan.IndexOf((byte)'\\');
                            Assert.Equal(requiresEscaping ? (i + 1) : -1, escapedIndex);  // Account for the start quote
                        }, StringValueEncodingType.Utf8);
                    }
                }

                if (dataLength != 0)
                {
                    char[] changed = baseStr.ToCharArray();
                    changed.AsSpan().Fill(replacementChar);
                    string newStr = new string(changed);
                    sourceUtf8 = Encoding.UTF8.GetBytes(newStr);

                    written = WriteStringHelper(writerOptions, newStr);
                    int escapedIndex = written.Span.IndexOf((byte)'\\');
                    Assert.Equal(requiresEscaping ? 1 : -1, escapedIndex);  // Account for the start quote

                    written = WriteUtf8StringHelper(writerOptions, sourceUtf8);
                    escapedIndex = written.Span.IndexOf((byte)'\\');
                    Assert.Equal(requiresEscaping ? 1 : -1, escapedIndex);  // Account for the start quote

                    if (dataLength < 10)
                    {
                        SplitStringDataHelper(changed, writerOptions, output =>
                        {
                            escapedIndex = output.WrittenSpan.IndexOf((byte)'\\');
                            Assert.Equal(requiresEscaping ? 1 : -1, escapedIndex);  // Account for the start quote
                        }, StringValueEncodingType.Utf16);

                        SplitStringDataHelper(sourceUtf8, writerOptions, output =>
                        {
                            escapedIndex = output.WrittenSpan.IndexOf((byte)'\\');
                            Assert.Equal(requiresEscaping ? 1 : -1, escapedIndex);  // Account for the start quote
                        }, StringValueEncodingType.Utf8);
                    }
                }
            }
        }

        public static IEnumerable<object[]> EscapingTestData
        {
            get
            {
                return new List<object[]>
            {
                new object[] { 'a', null, false },              // ASCII not escaped
                new object[] { '\u001F', null, true },          // control character within single byte range
                new object[] { '\u2000', null, true },          // space character outside single byte range
                new object[] { '\u00A2', null, true },          // non-ASCII but < 255
                new object[] { '\uA686', null, true },          // non-ASCII above short.MaxValue
                new object[] { '\u6C49', null, true },          // non-ASCII from chinese alphabet - multibyte
                new object[] { '"', null, true },               // ASCII but must always be escaped in JSON
                new object[] { '\\', null, true },              // ASCII but must always be escaped in JSON
                new object[] { '<', null, true },               // ASCII but escaped by default
                new object[] { '>', null, true },               // ASCII but escaped by default
                new object[] { '&', null, true },               // ASCII but escaped by default
                new object[] { '`', null, true },               // ASCII but escaped by default
                new object[] { '\'', null, true },              // ASCII but escaped by default
                new object[] { '+', null, true },               // ASCII but escaped by default

                new object[] { 'a', JavaScriptEncoder.Default, false },
                new object[] { '\u001F', JavaScriptEncoder.Default, true },
                new object[] { '\u2000', JavaScriptEncoder.Default, true },
                new object[] { '\u00A2', JavaScriptEncoder.Default, true },
                new object[] { '\uA686', JavaScriptEncoder.Default, true },
                new object[] { '\u6C49', JavaScriptEncoder.Default, true },
                new object[] { '"', JavaScriptEncoder.Default, true },
                new object[] { '\\', JavaScriptEncoder.Default, true },
                new object[] { '<', JavaScriptEncoder.Default, true },
                new object[] { '>', JavaScriptEncoder.Default, true },
                new object[] { '&', JavaScriptEncoder.Default, true },
                new object[] { '`', JavaScriptEncoder.Default, true },
                new object[] { '\'', JavaScriptEncoder.Default, true },
                new object[] { '+', JavaScriptEncoder.Default, true },

                new object[] { 'a', JavaScriptEncoder.Create(UnicodeRanges.BasicLatin), false },
                new object[] { '\u001F', JavaScriptEncoder.Create(UnicodeRanges.BasicLatin), true },
                new object[] { '\u2000', JavaScriptEncoder.Create(UnicodeRanges.BasicLatin), true },
                new object[] { '\u00A2', JavaScriptEncoder.Create(UnicodeRanges.BasicLatin), true },
                new object[] { '\uA686', JavaScriptEncoder.Create(UnicodeRanges.BasicLatin), true },
                new object[] { '\u6C49', JavaScriptEncoder.Create(UnicodeRanges.BasicLatin), true },
                new object[] { '"', JavaScriptEncoder.Create(UnicodeRanges.BasicLatin), true },
                new object[] { '\\', JavaScriptEncoder.Create(UnicodeRanges.BasicLatin), true },
                new object[] { '<', JavaScriptEncoder.Create(UnicodeRanges.BasicLatin), true },
                new object[] { '>', JavaScriptEncoder.Create(UnicodeRanges.BasicLatin), true },
                new object[] { '&', JavaScriptEncoder.Create(UnicodeRanges.BasicLatin), true },
                new object[] { '`', JavaScriptEncoder.Create(UnicodeRanges.BasicLatin), true },
                new object[] { '\'', JavaScriptEncoder.Create(UnicodeRanges.BasicLatin), true },
                new object[] { '+', JavaScriptEncoder.Create(UnicodeRanges.BasicLatin), true },

                new object[] { 'a', JavaScriptEncoder.Create(UnicodeRanges.All), false },
                new object[] { '\u001F', JavaScriptEncoder.Create(UnicodeRanges.All), true },
                new object[] { '\u2000', JavaScriptEncoder.Create(UnicodeRanges.All), true },
                new object[] { '\u00A2', JavaScriptEncoder.Create(UnicodeRanges.All), false },
                new object[] { '\uA686', JavaScriptEncoder.Create(UnicodeRanges.All), false },
                new object[] { '\u6C49', JavaScriptEncoder.Create(UnicodeRanges.All), false },
                new object[] { '"', JavaScriptEncoder.Create(UnicodeRanges.All), true },
                new object[] { '\\', JavaScriptEncoder.Create(UnicodeRanges.All), true },
                new object[] { '<', JavaScriptEncoder.Create(UnicodeRanges.All), true },
                new object[] { '>', JavaScriptEncoder.Create(UnicodeRanges.All), true },
                new object[] { '&', JavaScriptEncoder.Create(UnicodeRanges.All), true },
                new object[] { '`', JavaScriptEncoder.Create(UnicodeRanges.All), true },
                new object[] { '\'', JavaScriptEncoder.Create(UnicodeRanges.All), true },
                new object[] { '+', JavaScriptEncoder.Create(UnicodeRanges.All), true },

                new object[] { 'a', JavaScriptEncoder.UnsafeRelaxedJsonEscaping, false },
                new object[] { '\u001F', JavaScriptEncoder.UnsafeRelaxedJsonEscaping, true },
                new object[] { '\u2000', JavaScriptEncoder.UnsafeRelaxedJsonEscaping, true },
                new object[] { '\u00A2', JavaScriptEncoder.UnsafeRelaxedJsonEscaping, false },
                new object[] { '\uA686', JavaScriptEncoder.UnsafeRelaxedJsonEscaping, false },
                new object[] { '\u6C49', JavaScriptEncoder.UnsafeRelaxedJsonEscaping, false },
                new object[] { '"', JavaScriptEncoder.UnsafeRelaxedJsonEscaping, true },
                new object[] { '\\', JavaScriptEncoder.UnsafeRelaxedJsonEscaping, true },
                new object[] { '<', JavaScriptEncoder.UnsafeRelaxedJsonEscaping, false },
                new object[] { '>', JavaScriptEncoder.UnsafeRelaxedJsonEscaping, false },
                new object[] { '&', JavaScriptEncoder.UnsafeRelaxedJsonEscaping, false },
                new object[] { '`', JavaScriptEncoder.UnsafeRelaxedJsonEscaping, false },
                new object[] { '\'', JavaScriptEncoder.UnsafeRelaxedJsonEscaping, false },
                new object[] { '+', JavaScriptEncoder.UnsafeRelaxedJsonEscaping, false },
            };
            }
        }

        [Theory]
        [MemberData(nameof(EscapingTestData_NonAscii))]
        public unsafe void WriteString_NonAscii(char replacementChar, JavaScriptEncoder encoder, bool requiresEscaping)
        {
            var writerOptions = new JsonWriterOptions { Encoder = encoder };
            var random = new Random(42);
            for (int dataLength = 1; dataLength < 50; dataLength++)
            {
                char[] str = new char[dataLength];
                for (int i = 0; i < dataLength; i++)
                {
                    str[i] = (char)random.Next(0x2E9B, 0x2EF4); // CJK Radicals Supplement characters
                }
                string baseStr = new string(str);
                byte[] sourceUtf8 = Encoding.UTF8.GetBytes(baseStr);

                ReadOnlyMemory<byte> written = WriteStringHelper(writerOptions, baseStr);
                Assert.Equal(-1, written.Span.IndexOf((byte)'\\'));

                written = WriteUtf8StringHelper(writerOptions, sourceUtf8);
                Assert.Equal(-1, written.Span.IndexOf((byte)'\\'));

                if (dataLength < 10)
                {
                    SplitStringDataHelper(str, writerOptions, output =>
                    {
                        Assert.Equal(-1, output.WrittenSpan.IndexOf((byte)'\\'));
                    }, StringValueEncodingType.Utf16);

                    SplitStringDataHelper(sourceUtf8, writerOptions, output =>
                    {
                        Assert.Equal(-1, output.WrittenSpan.IndexOf((byte)'\\'));
                    }, StringValueEncodingType.Utf8);
                }

                for (int i = 0; i < dataLength; i++)
                {
                    string source = baseStr.Insert(i, new string(replacementChar, 1));
                    sourceUtf8 = Encoding.UTF8.GetBytes(source);

                    written = WriteStringHelper(writerOptions, source);
                    int escapedIndex = written.Span.IndexOf((byte)'\\');
                    // Each CJK character expands to 3 utf-8 bytes.
                    Assert.Equal(requiresEscaping ? ((i * 3) + 1) : -1, escapedIndex);  // Account for the start quote

                    written = WriteUtf8StringHelper(writerOptions, sourceUtf8);
                    escapedIndex = written.Span.IndexOf((byte)'\\');
                    // Each CJK character expands to 3 utf-8 bytes.
                    Assert.Equal(requiresEscaping ? ((i * 3) + 1) : -1, escapedIndex);  // Account for the start quote

                    if (dataLength < 10)
                    {
                        SplitStringDataHelper(source.AsSpan(), writerOptions, output =>
                        {
                            escapedIndex = output.WrittenSpan.IndexOf((byte)'\\');
                            // Each CJK character expands to 3 utf-8 bytes.
                            Assert.Equal(requiresEscaping ? ((i * 3) + 1) : -1, escapedIndex);  // Account for the start quote
                        }, StringValueEncodingType.Utf16);

                        SplitStringDataHelper(sourceUtf8, writerOptions, output =>
                        {
                            escapedIndex = output.WrittenSpan.IndexOf((byte)'\\');
                            // Each CJK character expands to 3 utf-8 bytes.
                            Assert.Equal(requiresEscaping ? ((i * 3) + 1) : -1, escapedIndex);  // Account for the start quote
                        }, StringValueEncodingType.Utf8);
                    }
                }
            }
        }

        public static IEnumerable<object[]> EscapingTestData_NonAscii
        {
            get
            {
                return new List<object[]>
            {
                new object[] { 'a', JavaScriptEncoder.Create(UnicodeRanges.All), false },
                new object[] { '\u001F', JavaScriptEncoder.Create(UnicodeRanges.All), true },
                new object[] { '\u2000', JavaScriptEncoder.Create(UnicodeRanges.All), true },
                new object[] { '\u00A2', JavaScriptEncoder.Create(UnicodeRanges.All), false },
                new object[] { '\uA686', JavaScriptEncoder.Create(UnicodeRanges.All), false },
                new object[] { '\u6C49', JavaScriptEncoder.Create(UnicodeRanges.All), false },
                new object[] { '"', JavaScriptEncoder.Create(UnicodeRanges.All), true },
                new object[] { '\\', JavaScriptEncoder.Create(UnicodeRanges.All), true },
                new object[] { '<', JavaScriptEncoder.Create(UnicodeRanges.All), true },
                new object[] { '>', JavaScriptEncoder.Create(UnicodeRanges.All), true },
                new object[] { '&', JavaScriptEncoder.Create(UnicodeRanges.All), true },
                new object[] { '`', JavaScriptEncoder.Create(UnicodeRanges.All), true },
                new object[] { '\'', JavaScriptEncoder.Create(UnicodeRanges.All), true },
                new object[] { '+', JavaScriptEncoder.Create(UnicodeRanges.All), true },

                new object[] { 'a', JavaScriptEncoder.UnsafeRelaxedJsonEscaping, false },
                new object[] { '\u001F', JavaScriptEncoder.UnsafeRelaxedJsonEscaping, true },
                new object[] { '\u2000', JavaScriptEncoder.UnsafeRelaxedJsonEscaping, true },
                new object[] { '\u00A2', JavaScriptEncoder.UnsafeRelaxedJsonEscaping, false },
                new object[] { '\uA686', JavaScriptEncoder.UnsafeRelaxedJsonEscaping, false },
                new object[] { '\u6C49', JavaScriptEncoder.UnsafeRelaxedJsonEscaping, false },
                new object[] { '"', JavaScriptEncoder.UnsafeRelaxedJsonEscaping, true },
                new object[] { '\\', JavaScriptEncoder.UnsafeRelaxedJsonEscaping, true },
                new object[] { '<', JavaScriptEncoder.UnsafeRelaxedJsonEscaping, false },
                new object[] { '>', JavaScriptEncoder.UnsafeRelaxedJsonEscaping, false },
                new object[] { '&', JavaScriptEncoder.UnsafeRelaxedJsonEscaping, false },
                new object[] { '`', JavaScriptEncoder.UnsafeRelaxedJsonEscaping, false },
                new object[] { '\'', JavaScriptEncoder.UnsafeRelaxedJsonEscaping, false },
                new object[] { '+', JavaScriptEncoder.UnsafeRelaxedJsonEscaping, false },
            };
            }
        }

        [Theory]
        [MemberData(nameof(JavaScriptEncoders))]
        public void EscapingTestWhileWritingSurrogate(JavaScriptEncoder encoder)
        {
            char highSurrogate = '\uD801';
            char lowSurrogate = '\uDC37';
            var writerOptions = new JsonWriterOptions { Encoder = encoder };
            var random = new Random(42);
            for (int dataLength = 2; dataLength < 50; dataLength++)
            {
                char[] str = new char[dataLength];
                for (int i = 0; i < dataLength; i++)
                {
                    str[i] = (char)random.Next(97, 123);
                }
                string baseStr = new string(str);
                byte[] sourceUtf8 = Encoding.UTF8.GetBytes(baseStr);

                ReadOnlyMemory<byte> written = WriteStringHelper(writerOptions, baseStr);
                Assert.Equal(-1, written.Span.IndexOf((byte)'\\'));

                written = WriteUtf8StringHelper(writerOptions, sourceUtf8);
                Assert.Equal(-1, written.Span.IndexOf((byte)'\\'));

                if (dataLength < 10)
                {
                    SplitStringDataHelper(str, writerOptions, output =>
                    {
                        Assert.Equal(-1, output.WrittenSpan.IndexOf((byte)'\\'));
                    }, StringValueEncodingType.Utf16);

                    SplitStringDataHelper(sourceUtf8, writerOptions, output =>
                    {
                        Assert.Equal(-1, output.WrittenSpan.IndexOf((byte)'\\'));
                    }, StringValueEncodingType.Utf8);
                }

                for (int i = 0; i < dataLength - 1; i++)
                {
                    char[] changed = baseStr.ToCharArray();
                    changed[i] = highSurrogate;
                    changed[i + 1] = lowSurrogate;
                    string newStr = new string(changed);
                    sourceUtf8 = Encoding.UTF8.GetBytes(newStr);

                    written = WriteStringHelper(writerOptions, newStr);
                    int escapedIndex = written.Span.IndexOf((byte)'\\');
                    Assert.Equal(i + 1, escapedIndex);  // Account for the start quote

                    written = WriteUtf8StringHelper(writerOptions, sourceUtf8);
                    escapedIndex = written.Span.IndexOf((byte)'\\');
                    Assert.Equal(i + 1, escapedIndex);  // Account for the start quote

                    if (dataLength < 10)
                    {
                        SplitStringDataHelper(changed, writerOptions, output =>
                        {
                            escapedIndex = output.WrittenSpan.IndexOf((byte)'\\');
                            Assert.Equal(i + 1, escapedIndex);  // Account for the start quote
                        }, StringValueEncodingType.Utf16);

                        SplitStringDataHelper(sourceUtf8, writerOptions, output =>
                        {
                            escapedIndex = output.WrittenSpan.IndexOf((byte)'\\');
                            Assert.Equal(i + 1, escapedIndex);  // Account for the start quote
                        }, StringValueEncodingType.Utf8);
                    }
                }

                {
                    char[] changed = baseStr.ToCharArray();

                    for (int i = 0; i < changed.Length - 1; i += 2)
                    {
                        changed[i] = highSurrogate;
                        changed[i + 1] = lowSurrogate;
                    }

                    string newStr = new string(changed);
                    sourceUtf8 = Encoding.UTF8.GetBytes(newStr);

                    written = WriteStringHelper(writerOptions, newStr);
                    int escapedIndex = written.Span.IndexOf((byte)'\\');
                    Assert.Equal(1, escapedIndex);  // Account for the start quote

                    written = WriteUtf8StringHelper(writerOptions, sourceUtf8);
                    escapedIndex = written.Span.IndexOf((byte)'\\');
                    Assert.Equal(1, escapedIndex);  // Account for the start quote

                    if (dataLength < 10)
                    {
                        SplitStringDataHelper(changed, writerOptions, output =>
                        {
                            escapedIndex = output.WrittenSpan.IndexOf((byte)'\\');
                            Assert.Equal(1, escapedIndex);  // Account for the start quote
                        }, StringValueEncodingType.Utf16);

                        SplitStringDataHelper(sourceUtf8, writerOptions, output =>
                        {
                            escapedIndex = output.WrittenSpan.IndexOf((byte)'\\');
                            Assert.Equal(1, escapedIndex);  // Account for the start quote
                        }, StringValueEncodingType.Utf8);
                    }
                }
            }
        }

        public static IEnumerable<object[]> JavaScriptEncoders
        {
            get
            {
                return new List<object[]>
            {
                new object[] { null },
                new object[] { JavaScriptEncoder.Default },
                new object[] { JavaScriptEncoder.Create(UnicodeRanges.BasicLatin) },
                new object[] { JavaScriptEncoder.Create(UnicodeRanges.All) },
                new object[] { JavaScriptEncoder.UnsafeRelaxedJsonEscaping },
            };
            }
        }

        [Theory]
        [MemberData(nameof(InvalidEscapingTestData))]
        public unsafe void WriteStringInvalidCharacter(char replacementChar, JavaScriptEncoder encoder)
        {
            var writerOptions = new JsonWriterOptions { Encoder = encoder };
            var random = new Random(42);
            for (int dataLength = 0; dataLength < 47; dataLength++)
            {
                char[] str = new char[dataLength];
                for (int i = 0; i < dataLength; i++)
                {
                    str[i] = (char)random.Next(97, 123);
                }
                string baseStr = new string(str);
                byte[] baseStrUtf8 = Encoding.UTF8.GetBytes(baseStr);

                for (int i = 0; i < dataLength; i++)
                {
                    char[] changed = baseStr.ToCharArray();
                    changed[i] = replacementChar;
                    string source = new string(changed);
                    byte[] sourceUtf8 = new byte[baseStrUtf8.Length];
                    baseStrUtf8.AsSpan().CopyTo(sourceUtf8);
                    sourceUtf8[i] = 0xC3;   // Invalid, first byte of a 2-byte utf-8 character

                    ReadOnlyMemory<byte> written = WriteStringHelper(writerOptions, source);
                    Assert.True(BeginsWithReplacementCharacter(written.Span.Slice(i + 1))); // +1 to account for starting quote

                    written = WriteUtf8StringHelper(writerOptions, sourceUtf8);
                    Assert.True(BeginsWithReplacementCharacter(written.Span.Slice(i + 1))); // +1 to account for starting quote

                    if (dataLength < 10)
                    {
                        SplitStringDataHelper(changed, writerOptions, output =>
                        {
                            Assert.True(BeginsWithReplacementCharacter(output.WrittenSpan.Slice(i + 1))); // +1 to account for starting quote
                        }, StringValueEncodingType.Utf16);

                        SplitStringDataHelper(sourceUtf8, writerOptions, output =>
                        {
                            Assert.True(BeginsWithReplacementCharacter(output.WrittenSpan.Slice(i + 1))); // +1 to account for starting quote
                        }, StringValueEncodingType.Utf8);
                    }
                }
            }

            static bool BeginsWithReplacementCharacter(ReadOnlySpan<byte> span)
            {
                // Returns true if the span contains the literal UTF-8 representation of the U+FFFD replacement
                // character or the "\uFFFD" JSON-escaped representation of the replacement character.
                // Account for the fact that an encoder might write a literal replacement character or its
                // escaped representation, and both forms are equally valid.

                if (span.StartsWith("\uFFFD"u8)) { return true; }
                if (span.Length >= 6)
                {
                    if (span[0] == (byte)'\\' && span[1] == (byte)'u'
                        && (span[2] == 'F' || span[2] == 'f')
                        && (span[3] == 'F' || span[3] == 'f')
                        && (span[4] == 'F' || span[4] == 'f')
                        && (span[5] == 'D' || span[5] == 'd'))
                    {
                        return true; // "\uFFFD" representation
                    }
                }
                return false; // unknown
            }
        }

        public static IEnumerable<object[]> InvalidEscapingTestData
        {
            get
            {
                return new List<object[]>
            {
                new object[] { '\uD801', JavaScriptEncoder.Default },         // Invalid, high surrogate alone
                new object[] { '\uDC01', JavaScriptEncoder.Default },         // Invalid, low surrogate alone

                new object[] { '\uD801', JavaScriptEncoder.UnsafeRelaxedJsonEscaping },
                new object[] { '\uDC01', JavaScriptEncoder.UnsafeRelaxedJsonEscaping },

                new object[] { '\uD801', JavaScriptEncoder.Create(UnicodeRanges.All) },
                new object[] { '\uDC01', JavaScriptEncoder.Create(UnicodeRanges.All) },

                new object[] { '\uD801', JavaScriptEncoder.Create(UnicodeRanges.BasicLatin) },
                new object[] { '\uDC01', JavaScriptEncoder.Create(UnicodeRanges.BasicLatin) },
            };
            }
        }

        private static ReadOnlyMemory<byte> WriteStringHelper(JsonWriterOptions writerOptions, string str)
        {
            var output = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(output, writerOptions))
            {
                writer.WriteStringValue(str);
            }
            return output.WrittenMemory;
        }

        private static ReadOnlyMemory<byte> WriteUtf8StringHelper(JsonWriterOptions writerOptions, byte[] utf8str)
        {
            var output = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(output, writerOptions))
            {
                writer.WriteStringValue(utf8str);
            }
            return output.WrittenMemory;
        }

        private static ReadOnlyMemory<byte> WriteStringSegmentHelper(JsonWriterOptions writerOptions, ReadOnlySpan<char> str)
        {
            var output = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(output, writerOptions))
            {
                writer.WriteStringValueSegment(str, true);
            }
            return output.WrittenMemory;
        }

        private static ReadOnlyMemory<byte> WriteUtf8StringSegmentHelper(JsonWriterOptions writerOptions, ReadOnlySpan<byte> utf8str)
        {
            var output = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(output, writerOptions))
            {
                writer.WriteStringValueSegment(utf8str, true);
            }
            return output.WrittenMemory;
        }

        [Fact]
        public void WriteJsonWritesToIBWOnDemand_Dispose()
        {
            var output = new ArrayBufferWriter<byte>();
            var writer = new Utf8JsonWriter(output);
            WriteLargeArrayOfStrings(writer, 1_000);
            Assert.Equal(17347, writer.BytesCommitted);
            Assert.Equal(5544, writer.BytesPending);
            Assert.Equal(0, writer.CurrentDepth);
            Assert.Equal(17347, output.WrittenCount);

            writer.Dispose();

            Assert.Equal(0, writer.BytesPending);
            Assert.Equal(22891, output.WrittenCount);
            JsonTestHelper.AssertContents(GetExpectedLargeArrayOfStrings(1_000), output);
        }

        [Fact]
        public void WriteJsonOnlyWritesToStreamOnDemand_Dispose()
        {
            string expectedString = GetExpectedLargeArrayOfStrings(1_000);
            Assert.Equal(22891, expectedString.Length);
            using (var stream = new MemoryStream())
            {
                var writer = new Utf8JsonWriter(stream);
                WriteLargeArrayOfStrings(writer, 1_000);
                Assert.Equal(0, writer.BytesCommitted);
                Assert.Equal(expectedString.Length, writer.BytesPending);
                Assert.Equal(0, writer.CurrentDepth);
                // Unless user disposes the writer or calls flush/flushasync explicitly, nothing gets written to the stream
                Assert.Equal(0, stream.Position);

                writer.Dispose();

                Assert.Equal(0, writer.BytesPending);
                Assert.Equal(expectedString.Length, stream.Position);
                JsonTestHelper.AssertContents(expectedString, stream);
            }
        }

        [Fact]
        public async Task WriteJsonOnlyWritesToStreamOnDemand_DisposeAsync()
        {
            string expectedString = GetExpectedLargeArrayOfStrings(1_000);
            Assert.Equal(22891, expectedString.Length);
            using (var stream = new MemoryStream())
            {
                var writer = new Utf8JsonWriter(stream);
                WriteLargeArrayOfStrings(writer, 1_000);
                Assert.Equal(0, writer.BytesCommitted);
                Assert.Equal(expectedString.Length, writer.BytesPending);
                Assert.Equal(0, writer.CurrentDepth);
                // Unless user disposes the writer or calls flush/flushasync explicitly, nothing gets written to the stream
                Assert.Equal(0, stream.Position);

                await writer.DisposeAsync();

                Assert.Equal(0, writer.BytesPending);
                Assert.Equal(expectedString.Length, stream.Position);
                JsonTestHelper.AssertContents(expectedString, stream);
            }
        }

        [Fact]
        public void WriteJsonOnlyWritesToStreamOnDemand_Flush()
        {
            string expectedString = GetExpectedLargeArrayOfStrings(1_000);
            Assert.Equal(22891, expectedString.Length);
            using (var stream = new MemoryStream())
            {
                var writer = new Utf8JsonWriter(stream);
                WriteLargeArrayOfStrings(writer, 1_000);
                Assert.Equal(0, writer.BytesCommitted);
                Assert.Equal(expectedString.Length, writer.BytesPending);
                Assert.Equal(0, writer.CurrentDepth);
                // Unless user disposes the writer or calls flush/flushasync explicitly, nothing gets written to the stream
                Assert.Equal(0, stream.Position);

                writer.Flush();

                Assert.Equal(0, writer.BytesPending);
                Assert.Equal(expectedString.Length, stream.Position);

                writer.Dispose();

                Assert.Equal(0, writer.BytesPending);
                Assert.Equal(expectedString.Length, stream.Position);
                JsonTestHelper.AssertContents(expectedString, stream);
            }
        }

        [Fact]
        public async Task WriteJsonOnlyWritesToStreamOnDemand_FlushAsync()
        {
            string expectedString = GetExpectedLargeArrayOfStrings(1_000);
            Assert.Equal(22891, expectedString.Length);
            using (var stream = new MemoryStream())
            {
                var writer = new Utf8JsonWriter(stream);
                WriteLargeArrayOfStrings(writer, 1_000);
                Assert.Equal(0, writer.BytesCommitted);
                Assert.Equal(expectedString.Length, writer.BytesPending);
                Assert.Equal(0, writer.CurrentDepth);
                // Unless user disposes the writer or calls flush/flushasync explicitly, nothing gets written to the stream
                Assert.Equal(0, stream.Position);

                await writer.FlushAsync();

                Assert.Equal(0, writer.BytesPending);
                Assert.Equal(expectedString.Length, stream.Position);

                await writer.DisposeAsync();

                Assert.Equal(0, writer.BytesPending);
                Assert.Equal(expectedString.Length, stream.Position);
                JsonTestHelper.AssertContents(expectedString, stream);
            }
        }

        private static void WriteLargeArrayOfStrings(Utf8JsonWriter writer, int length)
        {
            writer.WriteStartArray();
            for (int i = 0; i < length; i++)
            {
                writer.WriteStringValue($"some array value {i}");
            }
            writer.WriteEndArray();
        }

        private static string GetExpectedLargeArrayOfStrings(int length)
        {
            var stringBuilder = new StringBuilder();
            using (TextWriter stringWriter = new StringWriter(stringBuilder))
            using (var json = new JsonTextWriter(stringWriter))
            {
                json.Formatting = Formatting.None;
                json.WriteStartArray();
                for (int i = 0; i < length; i++)
                {
                    json.WriteValue($"some array value {i}");
                }
                json.WriteEnd();
            }
            return stringBuilder.ToString();
        }

        /// <summary>
        /// This test is constrained to run on Windows and MacOSX because it causes
        /// problems on Linux due to the way deferred memory allocation works. On Linux, the allocation can
        /// succeed even if there is not enough memory but then the test may get killed by the OOM killer at the
        /// time the memory is accessed which triggers the full memory allocation.
        /// Also see <see cref="WriteRawLargeJsonToStreamWithoutFlushing"/>
        /// </summary>
        [PlatformSpecific(TestPlatforms.Windows | TestPlatforms.OSX)]
        [ConditionalFact(typeof(Environment), nameof(Environment.Is64BitProcess))]
        [OuterLoop]
        public void WriteLargeJsonToStreamWithoutFlushing()
        {
            try
            {
                var largeArray = new char[150_000_000];
                largeArray.AsSpan().Fill('a');

                // Text size chosen so that after several doublings of the underlying buffer we reach ~2 GB (but don't go over)
                JsonEncodedText text1 = JsonEncodedText.Encode(largeArray.AsSpan(0, 7_500));
                JsonEncodedText text2 = JsonEncodedText.Encode(largeArray.AsSpan(0, 5_000));
                JsonEncodedText text3 = JsonEncodedText.Encode(largeArray.AsSpan(0, 150_000_000));

                using (var output = new MemoryStream())
                using (var writer = new Utf8JsonWriter(output))
                {
                    writer.WriteStartArray();
                    writer.WriteStringValue(text1);
                    Assert.Equal(7_503, writer.BytesPending);

                    for (int i = 0; i < 30_000; i++)
                    {
                        writer.WriteStringValue(text2);
                    }
                    Assert.Equal(150_097_503, writer.BytesPending);

                    for (int i = 0; i < 13; i++)
                    {
                        writer.WriteStringValue(text3);
                    }
                    Assert.Equal(2_100_097_542, writer.BytesPending);

                    // Next write forces a grow beyond max array length

                    Assert.Throws<OutOfMemoryException>(() => writer.WriteStringValue(text3));

                    Assert.Equal(2_100_097_542, writer.BytesPending);

                    var text4 = JsonEncodedText.Encode(largeArray.AsSpan(0, 1));
                    for (int i = 0; i < 10_000_000; i++)
                    {
                        writer.WriteStringValue(text4);
                    }

                    Assert.Equal(2_100_097_542 + (4 * 10_000_000), writer.BytesPending);
                }
            }
            catch (OutOfMemoryException)
            {
                throw new SkipTestException("Out of memory allocating large objects");
            }
        }

        [Theory]
        [MemberData(nameof(JsonOptions_TestData))]
        public void InitialState(JsonWriterOptions options)
        {
            var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, options))
            {
                Assert.Equal(0, writer.BytesCommitted);
                Assert.Equal(0, writer.BytesPending);
                Assert.Equal(0, writer.CurrentDepth);
                Assert.Null(writer.Options.Encoder);
                Assert.Equal(options.Indented, writer.Options.Indented);
                Assert.Equal(options.SkipValidation, writer.Options.SkipValidation);
                Assert.Equal(0, stream.Position);
            }

            var output = new FixedSizedBufferWriter(0);
            using (var writer = new Utf8JsonWriter(output, options))
            {
                Assert.Equal(0, writer.BytesCommitted);
                Assert.Equal(0, writer.BytesPending);
                Assert.Equal(0, writer.CurrentDepth);
                Assert.Null(writer.Options.Encoder);
                Assert.Equal(options.Indented, writer.Options.Indented);
                Assert.Equal(options.SkipValidation, writer.Options.SkipValidation);
                Assert.Equal(0, output.FormattedCount);
            }
        }

        [Theory]
        [MemberData(nameof(JsonOptions_TestData))]
        public void Reset(JsonWriterOptions options)
        {
            var stream = new MemoryStream();
            using var writeToStream = new Utf8JsonWriter(stream, options);
            writeToStream.WriteNumberValue(1);
            writeToStream.Flush();

            Assert.True(writeToStream.BytesCommitted != 0);

            writeToStream.Reset();
            Assert.Equal(0, writeToStream.BytesCommitted);
            Assert.Equal(0, writeToStream.BytesPending);
            Assert.Equal(0, writeToStream.CurrentDepth);
            Assert.Null(writeToStream.Options.Encoder);
            Assert.Equal(options.Indented, writeToStream.Options.Indented);
            Assert.Equal(options.SkipValidation, writeToStream.Options.SkipValidation);
            Assert.True(stream.Position != 0);

            long previousWritten = stream.Position;
            writeToStream.Flush();
            Assert.Equal(previousWritten, stream.Position);

            var output = new FixedSizedBufferWriter(256);
            using var writeToIBW = new Utf8JsonWriter(output, options);
            writeToIBW.WriteNumberValue(1);
            writeToIBW.Flush();

            Assert.True(writeToIBW.BytesCommitted != 0);

            writeToIBW.Reset();
            Assert.Equal(0, writeToIBW.BytesCommitted);
            Assert.Equal(0, writeToIBW.BytesPending);
            Assert.Equal(0, writeToIBW.CurrentDepth);
            Assert.Null(writeToIBW.Options.Encoder);
            Assert.Equal(options.Indented, writeToIBW.Options.Indented);
            Assert.Equal(options.SkipValidation, writeToIBW.Options.SkipValidation);
            Assert.True(output.FormattedCount != 0);

            previousWritten = output.FormattedCount;
            writeToIBW.Flush();
            Assert.Equal(previousWritten, output.FormattedCount);
        }

        [Theory]
        [MemberData(nameof(JsonOptions_TestData))]
        public void ResetWithSameOutput(JsonWriterOptions options)
        {
            var stream = new MemoryStream();
            using var writeToStream = new Utf8JsonWriter(stream, options);
            writeToStream.WriteNumberValue(1);
            writeToStream.Flush();

            Assert.True(writeToStream.BytesCommitted != 0);

            writeToStream.Reset(stream);
            Assert.Equal(0, writeToStream.BytesCommitted);
            Assert.Equal(0, writeToStream.BytesPending);
            Assert.Equal(0, writeToStream.CurrentDepth);
            Assert.Null(writeToStream.Options.Encoder);
            Assert.Equal(options.Indented, writeToStream.Options.Indented);
            Assert.Equal(options.SkipValidation, writeToStream.Options.SkipValidation);
            Assert.True(stream.Position != 0);

            long previousWritten = stream.Position;
            writeToStream.Flush();
            Assert.Equal(previousWritten, stream.Position);

            writeToStream.WriteNumberValue(1);
            writeToStream.Flush();

            Assert.NotEqual(previousWritten, stream.Position);
            Assert.Equal("11", Encoding.UTF8.GetString(stream.ToArray()));

            var output = new FixedSizedBufferWriter(257);
            using var writeToIBW = new Utf8JsonWriter(output, options);
            writeToIBW.WriteNumberValue(1);
            writeToIBW.Flush();

            Assert.True(writeToIBW.BytesCommitted != 0);

            writeToIBW.Reset(output);
            Assert.Equal(0, writeToIBW.BytesCommitted);
            Assert.Equal(0, writeToIBW.BytesPending);
            Assert.Equal(0, writeToIBW.CurrentDepth);
            Assert.Null(writeToIBW.Options.Encoder);
            Assert.Equal(options.Indented, writeToIBW.Options.Indented);
            Assert.Equal(options.SkipValidation, writeToIBW.Options.SkipValidation);
            Assert.True(output.FormattedCount != 0);

            previousWritten = output.FormattedCount;
            writeToIBW.Flush();
            Assert.Equal(previousWritten, output.FormattedCount);

            writeToIBW.WriteNumberValue(1);
            writeToIBW.Flush();

            Assert.NotEqual(previousWritten, output.FormattedCount);
            Assert.Equal("11", Encoding.UTF8.GetString(output.Formatted));
        }

        [Theory]
        [MemberData(nameof(JsonOptions_TestData))]
        public void ResetChangeOutputMode(JsonWriterOptions options)
        {
            var stream = new MemoryStream();
            using var writeToStream = new Utf8JsonWriter(stream, options);
            writeToStream.WriteNumberValue(1);
            writeToStream.Flush();

            Assert.True(writeToStream.BytesCommitted != 0);

            var output = new FixedSizedBufferWriter(256);
            writeToStream.Reset(output);
            Assert.Equal(0, writeToStream.BytesCommitted);
            Assert.Equal(0, writeToStream.BytesPending);
            Assert.Equal(0, writeToStream.CurrentDepth);
            Assert.Null(writeToStream.Options.Encoder);
            Assert.Equal(options.Indented, writeToStream.Options.Indented);
            Assert.Equal(options.SkipValidation, writeToStream.Options.SkipValidation);
            Assert.True(stream.Position != 0);

            long previousWrittenStream = stream.Position;
            long previousWrittenIBW = output.FormattedCount;
            Assert.Equal(0, previousWrittenIBW);
            writeToStream.Flush();
            Assert.Equal(previousWrittenStream, stream.Position);
            Assert.Equal(previousWrittenIBW, output.FormattedCount);

            writeToStream.WriteNumberValue(1);
            writeToStream.Flush();

            Assert.True(writeToStream.BytesCommitted != 0);
            Assert.Equal(previousWrittenStream, stream.Position);
            Assert.True(output.FormattedCount != 0);

            output = new FixedSizedBufferWriter(256);
            using var writeToIBW = new Utf8JsonWriter(output, options);
            writeToIBW.WriteNumberValue(1);
            writeToIBW.Flush();

            Assert.True(writeToIBW.BytesCommitted != 0);

            stream = new MemoryStream();
            writeToIBW.Reset(stream);
            Assert.Equal(0, writeToIBW.BytesCommitted);
            Assert.Equal(0, writeToIBW.BytesPending);
            Assert.Equal(0, writeToIBW.CurrentDepth);
            Assert.Null(writeToIBW.Options.Encoder);
            Assert.Equal(options.Indented, writeToIBW.Options.Indented);
            Assert.Equal(options.SkipValidation, writeToIBW.Options.SkipValidation);
            Assert.True(output.FormattedCount != 0);

            previousWrittenStream = stream.Position;
            previousWrittenIBW = output.FormattedCount;
            Assert.Equal(0, previousWrittenStream);
            writeToIBW.Flush();
            Assert.Equal(previousWrittenStream, stream.Position);
            Assert.Equal(previousWrittenIBW, output.FormattedCount);

            writeToIBW.WriteNumberValue(1);
            writeToIBW.Flush();

            Assert.True(writeToIBW.BytesCommitted != 0);
            Assert.Equal(previousWrittenIBW, output.FormattedCount);
            Assert.True(stream.Position != 0);
        }

        [Theory]
        [MemberData(nameof(JsonOptions_TestData))]
        public void InvalidReset(JsonWriterOptions options)
        {
            var stream = new MemoryStream();
            using var writeToStream = new Utf8JsonWriter(stream, options);

            Assert.Throws<ArgumentNullException>(() => writeToStream.Reset((Stream)null));
            Assert.Throws<ArgumentNullException>(() => writeToStream.Reset((IBufferWriter<byte>)null));

            stream.Dispose();

            Assert.Throws<ArgumentException>(() => writeToStream.Reset(stream));

            var output = new FixedSizedBufferWriter(256);
            using var writeToIBW = new Utf8JsonWriter(output, options);

            Assert.Throws<ArgumentNullException>(() => writeToIBW.Reset((Stream)null));
            Assert.Throws<ArgumentNullException>(() => writeToIBW.Reset((IBufferWriter<byte>)null));

            Assert.Throws<ArgumentException>(() => writeToIBW.Reset(stream));
        }

        [Theory]
        [MemberData(nameof(JsonOptions_TestData))]
        public void FlushEmpty(JsonWriterOptions options)
        {
            var output = new FixedSizedBufferWriter(0);

            using var jsonUtf8 = new Utf8JsonWriter(output, options);
            jsonUtf8.Flush();
            Assert.Equal(0, jsonUtf8.BytesCommitted);
            Assert.Equal(0, output.FormattedCount);

            var stream = new MemoryStream();
            using var writeToStream = new Utf8JsonWriter(stream, options);
            writeToStream.Flush();
            Assert.Equal(0, jsonUtf8.BytesCommitted);
            Assert.Equal(0, stream.Position);
        }

        [Theory]
        [MemberData(nameof(JsonOptions_TestData))]
        public async Task FlushEmptyAsync(JsonWriterOptions options)
        {
            var output = new FixedSizedBufferWriter(0);

            using var jsonUtf8 = new Utf8JsonWriter(output, options);
            await jsonUtf8.FlushAsync();
            Assert.Equal(0, jsonUtf8.BytesCommitted);
            Assert.Equal(0, output.FormattedCount);

            var stream = new MemoryStream();
            using var writeToStream = new Utf8JsonWriter(stream, options);
            await writeToStream.FlushAsync();
            Assert.Equal(0, jsonUtf8.BytesCommitted);
            Assert.Equal(0, stream.Position);
        }

        [Theory]
        [MemberData(nameof(JsonOptions_TestData))]
        public void FlushMultipleTimes(JsonWriterOptions options)
        {
            var output = new FixedSizedBufferWriter(256);

            using var jsonUtf8 = new Utf8JsonWriter(output, options);
            jsonUtf8.WriteStartObject();
            jsonUtf8.WriteEndObject();
            Assert.Equal(0, jsonUtf8.BytesCommitted);
            Assert.Equal(2, jsonUtf8.BytesPending);
            Assert.Equal(0, output.FormattedCount);
            jsonUtf8.Flush();
            Assert.Equal(2, jsonUtf8.BytesCommitted);
            Assert.Equal(0, jsonUtf8.BytesPending);
            Assert.Equal(2, output.FormattedCount);
            jsonUtf8.Flush();
            Assert.Equal(2, jsonUtf8.BytesCommitted);
            Assert.Equal(0, jsonUtf8.BytesPending);
            Assert.Equal(2, output.FormattedCount);

            var stream = new MemoryStream();
            using var writeToStream = new Utf8JsonWriter(stream, options);
            writeToStream.WriteStartObject();
            writeToStream.WriteEndObject();
            Assert.Equal(0, writeToStream.BytesCommitted);
            Assert.Equal(2, writeToStream.BytesPending);
            Assert.Equal(0, stream.Position);
            writeToStream.Flush();
            Assert.Equal(2, writeToStream.BytesCommitted);
            Assert.Equal(0, writeToStream.BytesPending);
            Assert.Equal(2, stream.Position);
            writeToStream.Flush();
            Assert.Equal(2, writeToStream.BytesCommitted);
            Assert.Equal(0, writeToStream.BytesPending);
            Assert.Equal(2, stream.Position);
        }

        [Theory]
        [MemberData(nameof(JsonOptions_TestData))]
        public async Task FlushMultipleTimesAsync(JsonWriterOptions options)
        {
            var output = new FixedSizedBufferWriter(256);

            using var jsonUtf8 = new Utf8JsonWriter(output, options);
            jsonUtf8.WriteStartObject();
            jsonUtf8.WriteEndObject();
            Assert.Equal(0, jsonUtf8.BytesCommitted);
            Assert.Equal(2, jsonUtf8.BytesPending);
            Assert.Equal(0, output.FormattedCount);
            await jsonUtf8.FlushAsync();
            Assert.Equal(2, jsonUtf8.BytesCommitted);
            Assert.Equal(0, jsonUtf8.BytesPending);
            Assert.Equal(2, output.FormattedCount);
            await jsonUtf8.FlushAsync();
            Assert.Equal(2, jsonUtf8.BytesCommitted);
            Assert.Equal(0, jsonUtf8.BytesPending);
            Assert.Equal(2, output.FormattedCount);

            var stream = new MemoryStream();
            using var writeToStream = new Utf8JsonWriter(stream, options);
            writeToStream.WriteStartObject();
            writeToStream.WriteEndObject();
            Assert.Equal(0, writeToStream.BytesCommitted);
            Assert.Equal(2, writeToStream.BytesPending);
            Assert.Equal(0, stream.Position);
            await writeToStream.FlushAsync();
            Assert.Equal(2, writeToStream.BytesCommitted);
            Assert.Equal(0, writeToStream.BytesPending);
            Assert.Equal(2, stream.Position);
            await writeToStream.FlushAsync();
            Assert.Equal(2, writeToStream.BytesCommitted);
            Assert.Equal(0, writeToStream.BytesPending);
            Assert.Equal(2, stream.Position);
        }

        [Theory]
        [MemberData(nameof(JsonOptions_TestData))]
        public void DisposeAutoFlushes(JsonWriterOptions options)
        {
            var output = new FixedSizedBufferWriter(256);

            using var jsonUtf8 = new Utf8JsonWriter(output, options);
            jsonUtf8.WriteStartObject();
            jsonUtf8.WriteEndObject();
            Assert.Equal(0, jsonUtf8.BytesCommitted);
            Assert.Equal(0, output.FormattedCount);
            jsonUtf8.Dispose();
            Assert.Equal(0, jsonUtf8.BytesCommitted);
            Assert.Equal(2, output.FormattedCount);

            var stream = new MemoryStream();
            using var writeToStream = new Utf8JsonWriter(stream, options);
            writeToStream.WriteStartObject();
            writeToStream.WriteEndObject();
            Assert.Equal(0, writeToStream.BytesCommitted);
            Assert.Equal(0, stream.Position);
            writeToStream.Dispose();
            Assert.Equal(0, writeToStream.BytesCommitted);
            Assert.Equal(2, stream.Position);
        }

        [Theory]
        [MemberData(nameof(JsonOptions_TestData))]
        public async Task DisposeAutoFlushesAsync(JsonWriterOptions options)
        {
            var output = new FixedSizedBufferWriter(256);

            using var jsonUtf8 = new Utf8JsonWriter(output, options);
            jsonUtf8.WriteStartObject();
            jsonUtf8.WriteEndObject();
            Assert.Equal(0, jsonUtf8.BytesCommitted);
            Assert.Equal(0, output.FormattedCount);
            await jsonUtf8.DisposeAsync();
            Assert.Equal(0, jsonUtf8.BytesCommitted);
            Assert.Equal(2, output.FormattedCount);

            var stream = new MemoryStream();
            using var writeToStream = new Utf8JsonWriter(stream, options);
            writeToStream.WriteStartObject();
            writeToStream.WriteEndObject();
            Assert.Equal(0, writeToStream.BytesCommitted);
            Assert.Equal(0, stream.Position);
            await writeToStream.DisposeAsync();
            Assert.Equal(0, writeToStream.BytesCommitted);
            Assert.Equal(2, stream.Position);
        }

        [Theory]
        [MemberData(nameof(JsonOptions_TestData))]
        public void UseAfterDisposeInvalid(JsonWriterOptions options)
        {
            var output = new FixedSizedBufferWriter(256);

            using var jsonUtf8 = new Utf8JsonWriter(output, options);
            jsonUtf8.WriteStartObject();
            Assert.Equal(0, jsonUtf8.BytesCommitted);
            Assert.Equal(1, jsonUtf8.BytesPending);
            Assert.Equal(0, output.FormattedCount);
            jsonUtf8.Dispose();
            Assert.Equal(0, jsonUtf8.BytesCommitted);
            Assert.Equal(0, jsonUtf8.BytesPending);
            Assert.Equal(1, output.FormattedCount);
            Assert.Throws<ObjectDisposedException>(() => jsonUtf8.Flush());
            jsonUtf8.Dispose();
            Assert.Throws<ObjectDisposedException>(() => jsonUtf8.Flush());

            Assert.Throws<ObjectDisposedException>(() => jsonUtf8.Reset());

            var stream = new MemoryStream();
            Assert.Throws<ObjectDisposedException>(() => jsonUtf8.Reset(stream));

            using var writeToStream = new Utf8JsonWriter(stream, options);
            writeToStream.WriteStartObject();
            Assert.Equal(0, writeToStream.BytesCommitted);
            Assert.Equal(1, writeToStream.BytesPending);
            Assert.Equal(0, stream.Position);
            writeToStream.Dispose();
            Assert.Equal(0, writeToStream.BytesCommitted);
            Assert.Equal(0, writeToStream.BytesPending);
            Assert.Equal(1, stream.Position);
            Assert.Throws<ObjectDisposedException>(() => writeToStream.Flush());
            writeToStream.Dispose();
            Assert.Throws<ObjectDisposedException>(() => writeToStream.Flush());

            Assert.Throws<ObjectDisposedException>(() => writeToStream.Reset());

            Assert.Throws<ObjectDisposedException>(() => jsonUtf8.Reset(output));
        }

        [Theory]
        [MemberData(nameof(JsonOptions_TestData))]
        public async Task UseAfterDisposeInvalidAsync(JsonWriterOptions options)
        {
            var output = new FixedSizedBufferWriter(256);

            using var jsonUtf8 = new Utf8JsonWriter(output, options);
            jsonUtf8.WriteStartObject();
            Assert.Equal(0, jsonUtf8.BytesCommitted);
            Assert.Equal(1, jsonUtf8.BytesPending);
            Assert.Equal(0, output.FormattedCount);
            await jsonUtf8.DisposeAsync();
            Assert.Equal(0, jsonUtf8.BytesCommitted);
            Assert.Equal(0, jsonUtf8.BytesPending);
            Assert.Equal(1, output.FormattedCount);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => jsonUtf8.FlushAsync());
            await jsonUtf8.DisposeAsync();
            await Assert.ThrowsAsync<ObjectDisposedException>(() => jsonUtf8.FlushAsync());

            Assert.Throws<ObjectDisposedException>(() => jsonUtf8.Reset());

            var stream = new MemoryStream();
            Assert.Throws<ObjectDisposedException>(() => jsonUtf8.Reset(stream));

            using var writeToStream = new Utf8JsonWriter(stream, options);
            writeToStream.WriteStartObject();
            Assert.Equal(0, writeToStream.BytesCommitted);
            Assert.Equal(1, writeToStream.BytesPending);
            Assert.Equal(0, stream.Position);
            await writeToStream.DisposeAsync();
            Assert.Equal(0, writeToStream.BytesCommitted);
            Assert.Equal(0, writeToStream.BytesPending);
            Assert.Equal(1, stream.Position);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => writeToStream.FlushAsync());
            await writeToStream.DisposeAsync();
            await Assert.ThrowsAsync<ObjectDisposedException>(() => writeToStream.FlushAsync());

            Assert.Throws<ObjectDisposedException>(() => writeToStream.Reset());

            Assert.Throws<ObjectDisposedException>(() => jsonUtf8.Reset(output));
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public async Task FlushToStreamThrows_WriterRemainsInConsistentState(bool useAsync, bool throwFromDispose)
        {
            var stream = new ThrowingFromWriteMemoryStream();
            var jsonUtf8 = new Utf8JsonWriter(stream);

            // Write and flush some of an object.
            jsonUtf8.WriteStartObject();
            jsonUtf8.WriteString("someProp1", "someValue1");
            await jsonUtf8.FlushAsync(useAsync);

            // Write some more, but fail while flushing to write to the underlying stream.
            stream.ExceptionToThrow = new FormatException("uh oh");
            jsonUtf8.WriteString("someProp2", "someValue2");
            Assert.Same(stream.ExceptionToThrow, await Assert.ThrowsAsync<FormatException>(() => jsonUtf8.FlushAsync(useAsync)));

            // Write some more.
            jsonUtf8.WriteEndObject();

            // Dispose, potentially throwing from the subsequent attempt to flush.
            if (throwFromDispose)
            {
                // Disposing should propagate the new exception
                stream.ExceptionToThrow = new FormatException("uh oh again");
                Assert.Same(stream.ExceptionToThrow, await Assert.ThrowsAsync<FormatException>(() => jsonUtf8.DisposeAsync(useAsync)));
                Assert.Equal("{\"someProp1\":\"someValue1\"", Encoding.UTF8.GetString(stream.ToArray()));
            }
            else
            {
                // Disposing should not fail.
                stream.ExceptionToThrow = null;
                await jsonUtf8.DisposeAsync(useAsync);
                Assert.Equal("{\"someProp1\":\"someValue1\",\"someProp2\":\"someValue2\"}", Encoding.UTF8.GetString(stream.ToArray()));
            }
        }

        private sealed class ThrowingFromWriteMemoryStream : MemoryStream
        {
            public Exception ExceptionToThrow { get; set; }

            public override void Write(byte[] buffer, int offset, int count)
            {
                if (ExceptionToThrow != null) throw ExceptionToThrow;
                base.Write(buffer, offset, count);
            }

            public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                if (ExceptionToThrow != null) throw ExceptionToThrow;
                await base.WriteAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
            }
        }

        [Theory]
        [MemberData(nameof(JsonOptions_TestData))]
        public void InvalidBufferWriter(JsonWriterOptions options)
        {
            var output = new InvalidBufferWriter();

            using var jsonUtf8 = new Utf8JsonWriter(output, options);

            Assert.Throws<InvalidOperationException>(() => jsonUtf8.WriteNumberValue((ulong)12345678901));
        }

        [Theory]
        [MemberData(nameof(JsonOptions_TestData))]
        public async Task WriteLargeToStream(JsonWriterOptions options)
        {
            var stream = new MemoryStream();
            
            await WriteLargeToStreamHelper(stream, options);

            string expectedString = GetExpectedLargeString(options);
            string actualString = Encoding.UTF8.GetString(stream.ToArray());

            Assert.Equal(expectedString, actualString);
        }

        [Theory]
        [MemberData(nameof(JsonOptions_TestData))]
        public void GrowBeyondBufferSize(JsonWriterOptions options)
        {
            const int InitialGrowthSize = 256;
            var output = new FixedSizedBufferWriter(InitialGrowthSize);
            
            byte[] utf8String = "this is a string long enough to overflow the buffer and cause an exception to be thrown."u8.ToArray();

            using var jsonUtf8 = new Utf8JsonWriter(output, options);

            jsonUtf8.WriteStartArray();

            while (jsonUtf8.BytesPending < InitialGrowthSize - utf8String.Length)
            {
                jsonUtf8.WriteStringValue(utf8String);
            }

            Assert.Throws<InvalidOperationException>(() => jsonUtf8.WriteStringValue(utf8String));
        }

        private static async Task WriteLargeToStreamHelper(Stream stream, JsonWriterOptions options)
        {
            const int SyncWriteThreshold = 25_000;

            await using var jsonUtf8 = new Utf8JsonWriter(stream, options);

            byte[] utf8String = "some string 1234"u8.ToArray();

            jsonUtf8.WriteStartArray();
            for (int i = 0; i < 10_000; i++)
            {
                jsonUtf8.WriteStringValue(utf8String);
                if (jsonUtf8.BytesPending > SyncWriteThreshold)
                {
                    await jsonUtf8.FlushAsync();
                }
            }
            jsonUtf8.WriteEndArray();
        }

        private static string GetExpectedLargeString(JsonWriterOptions options)
        {
            var ms = new MemoryStream();
            TextWriter streamWriter = new StreamWriter(ms, new UTF8Encoding(false), 1024, true);

            var json = new JsonTextWriter(streamWriter)
            {
                Formatting = options.Indented ? Formatting.Indented : Formatting.None
            };

            json.WriteStartArray();
            for (int i = 0; i < 10_000; i++)
            {
                json.WriteValue("some string 1234");
            }
            json.WriteEnd();

            json.Flush();

            return HandleFormatting(Encoding.UTF8.GetString(ms.ToArray()), options);
        }

        [Theory]
        [MemberData(nameof(JsonOptions_TestData))]
        public void FixedSizeBufferWriter_Guid(JsonWriterOptions options)
        {
            int sizeTooSmall = options.Indented ? options.IndentSize + 229 : 225;
            sizeTooSmall = Math.Max(sizeTooSmall, 256);
            var output = new FixedSizedBufferWriter(sizeTooSmall);

            byte[] utf8String = Encoding.UTF8.GetBytes(new string('a', 215));

            Guid guid = Guid.NewGuid();
            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteStartArray();
                jsonUtf8.WriteStringValue(utf8String);
                jsonUtf8.Flush();
                Assert.Throws<InvalidOperationException>(() => jsonUtf8.WriteStringValue(guid));
            }

            sizeTooSmall += options.Indented ? options.IndentSize + 40 : 38;
            output = new FixedSizedBufferWriter(sizeTooSmall);
            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteStartArray();
                jsonUtf8.WriteStringValue(utf8String);
                jsonUtf8.WriteStringValue(guid);
                jsonUtf8.Flush();
            }
            string actualStr = Encoding.UTF8.GetString(output.Formatted);

            if (!options.Indented)
            {
                Assert.Equal(257, output.Formatted.Length);
            }
            Assert.Equal($"\"{guid.ToString()}\"", actualStr.Substring(actualStr.Length - 38));
        }

        [Theory]
        [MemberData(nameof(JsonOptions_TestData))]
        public void FixedSizeBufferWriter_DateTime(JsonWriterOptions options)
        {
            int sizeTooSmall = options.Indented ? options.IndentSize + 240 : 236;
            sizeTooSmall = Math.Max(sizeTooSmall, 256);
            var output = new FixedSizedBufferWriter(sizeTooSmall);

            byte[] utf8String = Encoding.UTF8.GetBytes(new string('a', 232));

            var date = new DateTime(2019, 1, 1);
            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteStartArray();
                jsonUtf8.WriteStringValue(utf8String);
                jsonUtf8.Flush();
                Assert.Throws<InvalidOperationException>(() => jsonUtf8.WriteStringValue(date));
            }

            sizeTooSmall += options.Indented ? options.IndentSize + 37 : 35;
            output = new FixedSizedBufferWriter(sizeTooSmall);
            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteStartArray();
                jsonUtf8.WriteStringValue(utf8String);
                jsonUtf8.WriteStringValue(date);
                jsonUtf8.Flush();
            }
            string actualStr = Encoding.UTF8.GetString(output.Formatted);

            if (!options.Indented)
            {
                Assert.Equal(257, output.Formatted.Length);
            }
            Assert.Equal($"\"{date.ToString("yyyy-MM-ddTHH:mm:ss")}\"", actualStr.Substring(actualStr.Length - 21));
        }

        [Theory]
        [MemberData(nameof(JsonOptions_TestData))]
        public void FixedSizeBufferWriter_DateTimeOffset(JsonWriterOptions options)
        {
            int sizeTooSmall = options.Indented ? options.IndentSize + 240 : 236;
            sizeTooSmall = Math.Max(sizeTooSmall, 256);
            var output = new FixedSizedBufferWriter(sizeTooSmall);

            byte[] utf8String = Encoding.UTF8.GetBytes(new string('a', 226));

            DateTimeOffset date = new DateTime(2019, 1, 1);
            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteStartArray();
                jsonUtf8.WriteStringValue(utf8String);
                jsonUtf8.Flush();
                Assert.Throws<InvalidOperationException>(() => jsonUtf8.WriteStringValue(date));
            }

            sizeTooSmall += options.Indented ? options.IndentSize + 37 : 35;
            output = new FixedSizedBufferWriter(sizeTooSmall);
            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteStartArray();
                jsonUtf8.WriteStringValue(utf8String);
                jsonUtf8.WriteStringValue(date);
                jsonUtf8.Flush();
            }
            string actualStr = Encoding.UTF8.GetString(output.Formatted);

            if (!options.Indented)
            {
                Assert.Equal(257, output.Formatted.Length);
            }
            Assert.Equal($"\"{date.ToString("yyyy-MM-ddTHH:mm:ssK")}\"", actualStr.Substring(actualStr.Length - 27));
        }

        [Theory]
        [MemberData(nameof(JsonOptions_TestData))]
        public void FixedSizeBufferWriter_Decimal(JsonWriterOptions options)
        {
            var random = new Random(42);

            for (int i = 0; i < 1_000; i++)
            {
                var output = new FixedSizedBufferWriter(256);
                decimal value = JsonTestHelper.NextDecimal(random, 78E14, -78E14);

                using var jsonUtf8 = new Utf8JsonWriter(output, options);
                jsonUtf8.WriteNumberValue(value);

                jsonUtf8.Flush();
                string actualStr = Encoding.UTF8.GetString(output.Formatted);

                Assert.True(output.Formatted.Length <= 31);
                Assert.Equal(decimal.Parse(actualStr, CultureInfo.InvariantCulture), value);
            }

            for (int i = 0; i < 1_000; i++)
            {
                var output = new FixedSizedBufferWriter(256);
                decimal value = JsonTestHelper.NextDecimal(random, 1_000_000, -1_000_000);
                using var jsonUtf8 = new Utf8JsonWriter(output, options);
                jsonUtf8.WriteNumberValue(value);

                jsonUtf8.Flush();
                string actualStr = Encoding.UTF8.GetString(output.Formatted);

                Assert.True(output.Formatted.Length <= 31);
                Assert.Equal(decimal.Parse(actualStr, CultureInfo.InvariantCulture), value);
            }

            {
                var output = new FixedSizedBufferWriter(256);
                decimal value = 9999999999999999999999999999m;
                using var jsonUtf8 = new Utf8JsonWriter(output, options);
                jsonUtf8.WriteNumberValue(value);

                jsonUtf8.Flush();
                string actualStr = Encoding.UTF8.GetString(output.Formatted);

                Assert.Equal(value.ToString().Length, output.Formatted.Length);
                Assert.Equal(decimal.Parse(actualStr, CultureInfo.InvariantCulture), value);
            }

            {
                var output = new FixedSizedBufferWriter(256);
                decimal value = -9999999999999999999999999999m;
                using var jsonUtf8 = new Utf8JsonWriter(output, options);
                jsonUtf8.WriteNumberValue(value);

                jsonUtf8.Flush();
                string actualStr = Encoding.UTF8.GetString(output.Formatted);

                Assert.Equal(value.ToString().Length, output.Formatted.Length);
                Assert.Equal(decimal.Parse(actualStr, CultureInfo.InvariantCulture), value);
            }

            {
                int sizeTooSmall = options.Indented ? options.IndentSize + 230 : 226;
                sizeTooSmall = Math.Max(sizeTooSmall, 256);
                var output = new FixedSizedBufferWriter(sizeTooSmall);

                byte[] utf8String = Encoding.UTF8.GetBytes(new string('a', 222));

                decimal value = -0.9999999999999999999999999999m;
                using (var jsonUtf8 = new Utf8JsonWriter(output, options))
                {
                    jsonUtf8.WriteStartArray();
                    jsonUtf8.WriteStringValue(utf8String);
                    jsonUtf8.Flush();
                    Assert.Throws<InvalidOperationException>(() => jsonUtf8.WriteNumberValue(value));
                }

                sizeTooSmall += options.Indented ? options.IndentSize + 33 : 31;
                output = new FixedSizedBufferWriter(sizeTooSmall);
                using (var jsonUtf8 = new Utf8JsonWriter(output, options))
                {
                    jsonUtf8.WriteStartArray();
                    jsonUtf8.WriteStringValue(utf8String);
                    jsonUtf8.WriteNumberValue(value);

                    jsonUtf8.Flush();
                }
                string actualStr = Encoding.UTF8.GetString(output.Formatted);

                if (!options.Indented)
                {
                    Assert.Equal(257, output.Formatted.Length);
                }
                Assert.Equal(decimal.Parse(actualStr.Substring(actualStr.Length - 31), CultureInfo.InvariantCulture), value);
            }
        }

        private const JsonValueKind JsonValueKindStringSegment = (JsonValueKind)(1 << 7);
        public static IEnumerable<object[]> InvalidJsonDueToWritingMultipleValues_TestData() =>
            JsonOptionsWith([
                JsonValueKind.Array,
                JsonValueKind.Object,
                JsonValueKind.String,
                JsonValueKind.Number,
                JsonValueKind.True,
                JsonValueKind.False,
                JsonValueKind.Null,
                JsonValueKindStringSegment
            ]);

        [Theory]
        [MemberData(nameof(InvalidJsonDueToWritingMultipleValues_TestData))]
        public void InvalidJsonDueToWritingMultipleValues(JsonWriterOptions options, JsonValueKind kind)
        {
            var output = new ArrayBufferWriter<byte>(1024);

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                WritePreamble(jsonUtf8, kind);
                ValidateAction(jsonUtf8, () => jsonUtf8.WriteStartObject(), options.SkipValidation);
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                WritePreamble(jsonUtf8, kind);
                ValidateAction(jsonUtf8, () => jsonUtf8.WriteStartObject("foo"), options.SkipValidation);
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                WritePreamble(jsonUtf8, kind);
                ValidateAction(jsonUtf8, () => jsonUtf8.WriteStartArray(), options.SkipValidation);
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                WritePreamble(jsonUtf8, kind);
                ValidateAction(jsonUtf8, () => jsonUtf8.WriteEndObject(), options.SkipValidation);
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                WritePreamble(jsonUtf8, kind);
                ValidateAction(jsonUtf8, () => jsonUtf8.WriteEndArray(), options.SkipValidation);
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                WritePreamble(jsonUtf8, kind);
                ValidateAction(jsonUtf8, () => jsonUtf8.WritePropertyName("foo"), options.SkipValidation);
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                WritePreamble(jsonUtf8, kind);
                ValidateAction(jsonUtf8, () => jsonUtf8.WriteString("key", "foo"), options.SkipValidation);
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                WritePreamble(jsonUtf8, kind);
                ValidateAction(jsonUtf8, () => jsonUtf8.WriteStringValue("foo"), options.SkipValidation);
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                WritePreamble(jsonUtf8, kind);
                ValidateAction(jsonUtf8, () => jsonUtf8.WriteStringValueSegment("foo".AsSpan(), true), options.SkipValidation);
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                WritePreamble(jsonUtf8, kind);
                ValidateAction(jsonUtf8, () => jsonUtf8.WriteStringValueSegment("foo".AsSpan(), false), options.SkipValidation);
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                WritePreamble(jsonUtf8, kind);
                ValidateAction(jsonUtf8, () => jsonUtf8.WriteStringValueSegment("foo"u8, true), options.SkipValidation);
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                WritePreamble(jsonUtf8, kind);
                ValidateAction(jsonUtf8, () => jsonUtf8.WriteStringValueSegment("foo"u8, false), options.SkipValidation);
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                WritePreamble(jsonUtf8, kind);
                ValidateAction(jsonUtf8, () => jsonUtf8.WriteBase64StringSegment("foo"u8, true), options.SkipValidation);
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                WritePreamble(jsonUtf8, kind);
                ValidateAction(jsonUtf8, () => jsonUtf8.WriteBase64StringSegment("foo"u8, false), options.SkipValidation);
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                WritePreamble(jsonUtf8, kind);
                ValidateAction(jsonUtf8, () => jsonUtf8.WriteNumber("key", 123), options.SkipValidation);
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                WritePreamble(jsonUtf8, kind);
                ValidateAction(jsonUtf8, () => jsonUtf8.WriteNumberValue(123), options.SkipValidation);
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                WritePreamble(jsonUtf8, kind);
                ValidateAction(jsonUtf8, () => jsonUtf8.WriteBoolean("key", true), options.SkipValidation);
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                WritePreamble(jsonUtf8, kind);
                ValidateAction(jsonUtf8, () => jsonUtf8.WriteBooleanValue(true), options.SkipValidation);
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                WritePreamble(jsonUtf8, kind);
                ValidateAction(jsonUtf8, () => jsonUtf8.WriteBoolean("key", false), options.SkipValidation);
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                WritePreamble(jsonUtf8, kind);
                ValidateAction(jsonUtf8, () => jsonUtf8.WriteBooleanValue(false), options.SkipValidation);
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                WritePreamble(jsonUtf8, kind);
                ValidateAction(jsonUtf8, () => jsonUtf8.WriteNull("key"), options.SkipValidation);
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                WritePreamble(jsonUtf8, kind);
                ValidateAction(jsonUtf8, () => jsonUtf8.WriteNullValue(), options.SkipValidation);
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                WritePreamble(jsonUtf8, kind);
                // Writing a comment after any preamable is valid (even when skipValidation is false)
                ValidateAction(jsonUtf8, () => jsonUtf8.WriteCommentValue("some comment"), skipValidation: true);
            }
        }

        [Theory]
        [MemberData(nameof(InvalidJsonDueToWritingMultipleValues_TestData))]
        public void InvalidJsonDueToWritingMultipleValuesWithComments(JsonWriterOptions options, JsonValueKind kind)
        {
            var output = new ArrayBufferWriter<byte>(1024);

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                WritePreamble(jsonUtf8, kind, addComments: true);
                ValidateAction(jsonUtf8, () => jsonUtf8.WriteStartObject(), options.SkipValidation);
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                WritePreamble(jsonUtf8, kind, addComments: true);
                ValidateAction(jsonUtf8, () => jsonUtf8.WriteStartObject("foo"), options.SkipValidation);
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                WritePreamble(jsonUtf8, kind, addComments: true);
                ValidateAction(jsonUtf8, () => jsonUtf8.WriteStartArray(), options.SkipValidation);
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                WritePreamble(jsonUtf8, kind, addComments: true);
                ValidateAction(jsonUtf8, () => jsonUtf8.WriteEndObject(), options.SkipValidation);
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                WritePreamble(jsonUtf8, kind, addComments: true);
                ValidateAction(jsonUtf8, () => jsonUtf8.WriteEndArray(), options.SkipValidation);
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                WritePreamble(jsonUtf8, kind, addComments: true);
                ValidateAction(jsonUtf8, () => jsonUtf8.WritePropertyName("foo"), options.SkipValidation);
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                WritePreamble(jsonUtf8, kind, addComments: true);
                ValidateAction(jsonUtf8, () => jsonUtf8.WriteString("key", "foo"), options.SkipValidation);
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                WritePreamble(jsonUtf8, kind, addComments: true);
                ValidateAction(jsonUtf8, () => jsonUtf8.WriteStringValue("foo"), options.SkipValidation);
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                WritePreamble(jsonUtf8, kind, addComments: true);
                ValidateAction(jsonUtf8, () => jsonUtf8.WriteStringValueSegment("foo".AsSpan(), true), options.SkipValidation);
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                WritePreamble(jsonUtf8, kind, addComments: true);
                ValidateAction(jsonUtf8, () => jsonUtf8.WriteStringValueSegment("foo".AsSpan(), false), options.SkipValidation);
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                WritePreamble(jsonUtf8, kind, addComments: true);
                ValidateAction(jsonUtf8, () => jsonUtf8.WriteStringValueSegment("foo"u8, true), options.SkipValidation);
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                WritePreamble(jsonUtf8, kind, addComments: true);
                ValidateAction(jsonUtf8, () => jsonUtf8.WriteStringValueSegment("foo"u8, false), options.SkipValidation);
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                WritePreamble(jsonUtf8, kind, addComments: true);
                ValidateAction(jsonUtf8, () => jsonUtf8.WriteBase64StringSegment("foo"u8, true), options.SkipValidation);
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                WritePreamble(jsonUtf8, kind, addComments: true);
                ValidateAction(jsonUtf8, () => jsonUtf8.WriteBase64StringSegment("foo"u8, false), options.SkipValidation);
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                WritePreamble(jsonUtf8, kind, addComments: true);
                ValidateAction(jsonUtf8, () => jsonUtf8.WriteNumber("key", 123), options.SkipValidation);
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                WritePreamble(jsonUtf8, kind, addComments: true);
                ValidateAction(jsonUtf8, () => jsonUtf8.WriteNumberValue(123), options.SkipValidation);
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                WritePreamble(jsonUtf8, kind, addComments: true);
                ValidateAction(jsonUtf8, () => jsonUtf8.WriteBoolean("key", true), options.SkipValidation);
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                WritePreamble(jsonUtf8, kind, addComments: true);
                ValidateAction(jsonUtf8, () => jsonUtf8.WriteBooleanValue(true), options.SkipValidation);
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                WritePreamble(jsonUtf8, kind, addComments: true);
                ValidateAction(jsonUtf8, () => jsonUtf8.WriteBoolean("key", false), options.SkipValidation);
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                WritePreamble(jsonUtf8, kind, addComments: true);
                ValidateAction(jsonUtf8, () => jsonUtf8.WriteBooleanValue(false), options.SkipValidation);
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                WritePreamble(jsonUtf8, kind, addComments: true);
                ValidateAction(jsonUtf8, () => jsonUtf8.WriteNull("key"), options.SkipValidation);
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                WritePreamble(jsonUtf8, kind, addComments: true);
                ValidateAction(jsonUtf8, () => jsonUtf8.WriteNullValue(), options.SkipValidation);
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                WritePreamble(jsonUtf8, kind, addComments: true);
                // Writing a comment after any preamable is valid (even when skipValidation is false)
                ValidateAction(jsonUtf8, () => jsonUtf8.WriteCommentValue("some comment"), skipValidation: true);
            }
        }

        private void WritePreamble(Utf8JsonWriter writer, JsonValueKind kind, bool addComments = false)
        {
            Debug.Assert(writer.BytesCommitted == 0 && writer.BytesPending == 0 && writer.CurrentDepth == 0 && kind != JsonValueKind.Undefined);

            if (addComments)
            {
                writer.WriteCommentValue(" comment value before ");
            }

            switch (kind)
            {
                case JsonValueKind.Object:
                    writer.WriteStartObject();
                    writer.WriteEndObject();
                    break;
                case JsonValueKind.Array:
                    writer.WriteStartArray();
                    writer.WriteEndArray();
                    break;
                case JsonValueKind.String:
                    writer.WriteStringValue("foo");
                    break;
                case JsonValueKind.Number:
                    writer.WriteNumberValue(1);
                    break;
                case JsonValueKind.True:
                    writer.WriteBooleanValue(true);
                    break;
                case JsonValueKind.False:
                    writer.WriteBooleanValue(false);
                    break;
                case JsonValueKind.Null:
                    writer.WriteNullValue();
                    break;
                case JsonValueKindStringSegment:
                    writer.WriteStringValueSegment("foo".ToCharArray(), false);
                    writer.WriteStringValueSegment("bar".ToCharArray(), true);
                    break;
                default:
                    Debug.Fail($"Invalid JsonValueKind passed in '{kind}'.");
                    break;
            }

            if (addComments)
            {
                writer.WriteCommentValue(" comment value after ");
            }
        }

        private void ValidateAction(Utf8JsonWriter writer, Action action, bool skipValidation)
        {
            int originalBytesPending = writer.BytesPending;
            if (skipValidation)
            {
                action.Invoke();
                Assert.NotEqual(originalBytesPending, writer.BytesPending);
            }
            else
            {
                Assert.Throws<InvalidOperationException>(action);
                Assert.Equal(originalBytesPending, writer.BytesPending);
            }
        }

        [Theory]
        [MemberData(nameof(JsonOptions_TestData))]
        public void InvalidJsonMismatch(JsonWriterOptions options)
        {
            var output = new ArrayBufferWriter<byte>(1024);

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                if (options.SkipValidation)
                {
                    jsonUtf8.WriteEndArray();
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => jsonUtf8.WriteEndArray());
                }
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                if (options.SkipValidation)
                {
                    jsonUtf8.WriteEndObject();
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => jsonUtf8.WriteEndObject());
                }
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                if (options.SkipValidation)
                {
                    jsonUtf8.WriteStartArray("property at start");
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => jsonUtf8.WriteStartArray("property at start"));
                }
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                if (options.SkipValidation)
                {
                    jsonUtf8.WriteStartObject("property at start");
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => jsonUtf8.WriteStartObject("property at start"));
                }
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteStartArray();
                if (options.SkipValidation)
                {
                    jsonUtf8.WriteStartArray("property inside array");
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => jsonUtf8.WriteStartArray("property inside array"));
                }
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteStartObject();
                if (options.SkipValidation)
                {
                    jsonUtf8.WriteStartObject();
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => jsonUtf8.WriteStartObject());
                }
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteStartArray();
                if (options.SkipValidation)
                {
                    jsonUtf8.WriteEndObject();
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => jsonUtf8.WriteEndObject());
                }
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteStartObject();
                if (options.SkipValidation)
                {
                    jsonUtf8.WriteStringValue("value");
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => jsonUtf8.WriteStringValue("key"));
                }
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteStartObject();
                if (options.SkipValidation)
                {
                    jsonUtf8.WriteStringValueSegment(['a', 'b'], true);
                    jsonUtf8.WriteStringValueSegment(['a', 'b'], false);
                    jsonUtf8.WriteStringValueSegment(['a', 'b'], true);

                    jsonUtf8.WriteStringValueSegment([65, 66], true);
                    jsonUtf8.WriteStringValueSegment([65, 66], false);
                    jsonUtf8.WriteStringValueSegment([65, 66], true);

                    jsonUtf8.WriteBase64StringSegment([65, 66], true);
                    jsonUtf8.WriteBase64StringSegment([65, 66], false);
                    jsonUtf8.WriteBase64StringSegment([65, 66], true);
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => jsonUtf8.WriteStringValueSegment(['a', 'b'], true));
                    Assert.Throws<InvalidOperationException>(() => jsonUtf8.WriteStringValueSegment(['a', 'b'], false));
                    Assert.Throws<InvalidOperationException>(() => jsonUtf8.WriteStringValueSegment([65, 66], true));
                    Assert.Throws<InvalidOperationException>(() => jsonUtf8.WriteStringValueSegment([65, 66], false));
                    Assert.Throws<InvalidOperationException>(() => jsonUtf8.WriteBase64StringSegment([65, 66], true));
                    Assert.Throws<InvalidOperationException>(() => jsonUtf8.WriteBase64StringSegment([65, 66], false));
                }
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteStartArray();
                if (options.SkipValidation)
                {
                    jsonUtf8.WriteString("key", "value");
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => jsonUtf8.WriteString("key", "value"));
                }
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteStartArray();
                if (options.SkipValidation)
                {
                    jsonUtf8.WriteString(JsonEncodedText.Encode("key"), JsonEncodedText.Encode("value"));
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => jsonUtf8.WriteString(JsonEncodedText.Encode("key"), JsonEncodedText.Encode("value")));
                }
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteStartObject();
                if (options.SkipValidation)
                {
                    jsonUtf8.WriteEndArray();
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => jsonUtf8.WriteEndArray());
                }
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteStartObject();
                if (options.SkipValidation)
                {
                    jsonUtf8.WriteStartArray();
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => jsonUtf8.WriteStartArray());
                }
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteStartArray();
                jsonUtf8.WriteStartArray();
                jsonUtf8.WriteEndArray();
                if (options.SkipValidation)
                {
                    jsonUtf8.WriteEndObject();
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => jsonUtf8.WriteEndObject());
                }
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteStartObject();
                jsonUtf8.WriteStartObject("some object");
                jsonUtf8.WriteEndObject();
                if (options.SkipValidation)
                {
                    jsonUtf8.WriteEndArray();
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => jsonUtf8.WriteEndArray());
                }
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteStartArray();
                if (options.SkipValidation)
                {
                    jsonUtf8.WriteStartObject("some object");
                    jsonUtf8.WriteEndObject();
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => jsonUtf8.WriteStartObject("some object"));
                    Assert.Throws<InvalidOperationException>(() => jsonUtf8.WriteEndObject());
                }
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteStartObject();
                jsonUtf8.WriteStartArray("test array");
                jsonUtf8.WriteEndArray();
                if (options.SkipValidation)
                {
                    jsonUtf8.WriteEndArray();
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => jsonUtf8.WriteEndArray());
                }
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteStartArray();
                jsonUtf8.WriteEndArray();
                if (options.SkipValidation)
                {
                    jsonUtf8.WriteEndArray();
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => jsonUtf8.WriteEndArray());
                }
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteStartObject();
                jsonUtf8.WriteEndObject();
                if (options.SkipValidation)
                {
                    jsonUtf8.WriteEndObject();
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => jsonUtf8.WriteEndObject());
                }
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteStartArray();
                jsonUtf8.WriteStartArray();
                if (options.SkipValidation)
                {
                    jsonUtf8.WriteEndObject();
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => jsonUtf8.WriteEndObject());
                }
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteStartObject();
                jsonUtf8.WriteStartObject("test object");
                if (options.SkipValidation)
                {
                    jsonUtf8.WriteEndArray();
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => jsonUtf8.WriteEndArray());
                }
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                if (options.SkipValidation)
                {
                    jsonUtf8.WritePropertyName("test name");
                    jsonUtf8.WritePropertyName(JsonEncodedText.Encode("test name"));
                    jsonUtf8.WritePropertyName("test name".AsSpan());
                    jsonUtf8.WritePropertyName("test name"u8.ToArray());
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => jsonUtf8.WritePropertyName("test name"));
                    Assert.Throws<InvalidOperationException>(() => jsonUtf8.WritePropertyName(JsonEncodedText.Encode("test name")));
                    Assert.Throws<InvalidOperationException>(() => jsonUtf8.WritePropertyName("test name".AsSpan()));
                    Assert.Throws<InvalidOperationException>(() => jsonUtf8.WritePropertyName("test name"u8.ToArray()));
                }
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteStartArray();
                if (options.SkipValidation)
                {
                    jsonUtf8.WritePropertyName("test name");
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => jsonUtf8.WritePropertyName("test name"));
                }
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteStartObject();
                jsonUtf8.WritePropertyName("first name");
                if (options.SkipValidation)
                {
                    jsonUtf8.WritePropertyName("test name");
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => jsonUtf8.WritePropertyName("test name"));
                }
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteStartObject();
                jsonUtf8.WritePropertyName("first name");
                if (options.SkipValidation)
                {
                    jsonUtf8.WriteStartArray("test name");
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => jsonUtf8.WriteStartArray("test name"));
                }
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteStartObject();
                jsonUtf8.WritePropertyName("first name");
                if (options.SkipValidation)
                {
                    jsonUtf8.WriteStartObject("test name");
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => jsonUtf8.WriteStartObject("test name"));
                }
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteStartObject();
                jsonUtf8.WritePropertyName("first name");
                if (options.SkipValidation)
                {
                    jsonUtf8.WriteEndObject();
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => jsonUtf8.WriteEndObject());
                }
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteStartObject();
                jsonUtf8.WritePropertyName("first name");
                if (options.SkipValidation)
                {
                    jsonUtf8.WriteString("another property name", "some value");
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => jsonUtf8.WriteString("another property name", "some value"));
                }
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteStartObject();
                jsonUtf8.WritePropertyName("first name");
                if (options.SkipValidation)
                {
                    jsonUtf8.WriteNumber("another property name", 12345);
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => jsonUtf8.WriteNumber("another property name", 12345));
                }
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteStartObject();
                jsonUtf8.WritePropertyName("first name");
                if (options.SkipValidation)
                {
                    jsonUtf8.WriteNull("another property name");
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => jsonUtf8.WriteNull("another property name"));
                }
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteStartObject();
                jsonUtf8.WritePropertyName("first name");
                if (options.SkipValidation)
                {
                    jsonUtf8.WriteBoolean("another property name", true);
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => jsonUtf8.WriteBoolean("another property name", true));
                }
            }
        }

        [Theory]
        [MemberData(nameof(JsonOptions_TestData))]
        public void InvalidJsonIncomplete(JsonWriterOptions options)
        {
            var output = new ArrayBufferWriter<byte>(1024);

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteStartArray();
                Assert.True(jsonUtf8.CurrentDepth != 0);
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteStartObject();
                Assert.True(jsonUtf8.CurrentDepth != 0);
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteStartArray();
                jsonUtf8.WriteStartArray();
                jsonUtf8.WriteEndArray();
                Assert.True(jsonUtf8.CurrentDepth != 0);
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteStartObject();
                jsonUtf8.WriteStartObject("some object");
                Assert.True(jsonUtf8.CurrentDepth != 0);
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteStartArray();
                jsonUtf8.WriteStartObject();
                jsonUtf8.WriteEndObject();
                Assert.True(jsonUtf8.CurrentDepth != 0);
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteStartObject();
                jsonUtf8.WriteStartArray("test array");
                jsonUtf8.WriteEndArray();
                Assert.True(jsonUtf8.CurrentDepth != 0);
            }
        }

        [Theory]
        [MemberData(nameof(JsonOptions_TestData))]
        public void InvalidJsonPrimitive(JsonWriterOptions options)
        {
            var output = new ArrayBufferWriter<byte>(1024);

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteNumberValue(12345);
                if (options.SkipValidation)
                {
                    jsonUtf8.WriteNumberValue(12345);
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => jsonUtf8.WriteNumberValue(12345));
                }
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteNumberValue(12345);
                if (options.SkipValidation)
                {
                    jsonUtf8.WriteStartArray();
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => jsonUtf8.WriteStartArray());
                }
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteNumberValue(12345);
                if (options.SkipValidation)
                {
                    jsonUtf8.WriteStartObject();
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => jsonUtf8.WriteStartObject());
                }
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteNumberValue(12345);
                if (options.SkipValidation)
                {
                    jsonUtf8.WriteStartArray("property name");
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => jsonUtf8.WriteStartArray("property name"));
                }
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteNumberValue(12345);
                if (options.SkipValidation)
                {
                    jsonUtf8.WriteStartObject("property name");
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => jsonUtf8.WriteStartObject("property name"));
                }
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteNumberValue(12345);
                if (options.SkipValidation)
                {
                    jsonUtf8.WriteString("property name", "value");
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => jsonUtf8.WriteString("property name", "value"));
                }
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteNumberValue(12345);
                if (options.SkipValidation)
                {
                    jsonUtf8.WriteString(JsonEncodedText.Encode("property name"), JsonEncodedText.Encode("value"));
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => jsonUtf8.WriteString(JsonEncodedText.Encode("property name"), JsonEncodedText.Encode("value")));
                }
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteNumberValue(12345);
                if (options.SkipValidation)
                {
                    jsonUtf8.WriteEndArray();
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => jsonUtf8.WriteEndArray());
                }
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteNumberValue(12345);
                if (options.SkipValidation)
                {
                    jsonUtf8.WriteEndObject();
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => jsonUtf8.WriteEndObject());
                }
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteNumberValue(12345);
                if (options.SkipValidation)
                {
                    jsonUtf8.WritePropertyName("test name");
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => jsonUtf8.WritePropertyName("test name"));
                }
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteBooleanValue(true);
                if (options.SkipValidation)
                {
                    jsonUtf8.WritePropertyName("test name");
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => jsonUtf8.WritePropertyName("test name"));
                }
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteNullValue();
                if (options.SkipValidation)
                {
                    jsonUtf8.WritePropertyName("test name");
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => jsonUtf8.WritePropertyName("test name"));
                }
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteStringValue("some string");
                if (options.SkipValidation)
                {
                    jsonUtf8.WritePropertyName("test name");
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => jsonUtf8.WritePropertyName("test name"));
                }
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteStringValueSegment("a".AsSpan(), true);
                if (options.SkipValidation)
                {
                    jsonUtf8.WritePropertyName("test name");
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => jsonUtf8.WritePropertyName("test name"));
                }
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteStringValueSegment("a"u8, true);
                if (options.SkipValidation)
                {
                    jsonUtf8.WritePropertyName("test name");
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => jsonUtf8.WritePropertyName("test name"));
                }
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteBase64StringSegment("a"u8, true);
                if (options.SkipValidation)
                {
                    jsonUtf8.WritePropertyName("test name");
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => jsonUtf8.WritePropertyName("test name"));
                }
            }
        }

        // Name is present in the test data to make it easier to identify the test case
        public static IEnumerable<object[]> InvalidJsonStringValueSegment_TestData =>
            from write in new (string methodName, Action<Utf8JsonWriter> method)[] {
                (nameof(Utf8JsonWriter.WriteStartObject), writer => writer.WriteStartObject()),
                (nameof(Utf8JsonWriter.WriteEndObject), writer => writer.WriteEndObject()),
                (nameof(Utf8JsonWriter.WriteStartArray), writer => writer.WriteStartArray()),
                (nameof(Utf8JsonWriter.WriteEndArray), writer => writer.WriteEndArray()),
                (nameof(Utf8JsonWriter.WriteBooleanValue), writer => writer.WriteBooleanValue(true)),
                (nameof(Utf8JsonWriter.WriteBoolean), writer => writer.WriteBoolean("foo", true)),
                (nameof(Utf8JsonWriter.WriteCommentValue), writer => writer.WriteCommentValue("comment")),
                (nameof(Utf8JsonWriter.WriteNullValue), writer => writer.WriteNullValue()),
                (nameof(Utf8JsonWriter.WriteStringValue), writer => writer.WriteStringValue("foo")),
                (nameof(Utf8JsonWriter.WritePropertyName), writer => writer.WritePropertyName("foo")),
            }
            from option in new [] { new JsonWriterOptions { SkipValidation = true }, new JsonWriterOptions { SkipValidation = false } }
            select new object[] { write.methodName, write.method, option };

        [Theory]
        [MemberData(nameof(InvalidJsonStringValueSegment_TestData))]
        public void InvalidJsonStringValueSegment(string _, Action<Utf8JsonWriter> write, JsonWriterOptions options)
        {
            var output = new ArrayBufferWriter<byte>(1024);
            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteStringValueSegment("foo"u8, isFinalSegment: false);
                if (options.SkipValidation)
                {
                    write(jsonUtf8);
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => write(jsonUtf8));
                }
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteBase64StringSegment("foo"u8, isFinalSegment: false);
                if (options.SkipValidation)
                {
                    write(jsonUtf8);
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => write(jsonUtf8));
                }
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteStringValueSegment("foo".ToCharArray(), isFinalSegment: false);
                if (options.SkipValidation)
                {
                    write(jsonUtf8);
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => write(jsonUtf8));
                }
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteStartArray();
                jsonUtf8.WriteStringValueSegment("foo"u8, isFinalSegment: false);
                if (options.SkipValidation)
                {
                    write(jsonUtf8);
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => write(jsonUtf8));
                }
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteStartArray();
                jsonUtf8.WriteStringValueSegment("foo".ToCharArray(), isFinalSegment: false);
                if (options.SkipValidation)
                {
                    write(jsonUtf8);
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => write(jsonUtf8));
                }
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteStartObject();
                jsonUtf8.WritePropertyName("prop");
                jsonUtf8.WriteStringValueSegment("foo"u8, isFinalSegment: false);
                if (options.SkipValidation)
                {
                    write(jsonUtf8);
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => write(jsonUtf8));
                }
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteStartObject();
                jsonUtf8.WritePropertyName("prop");
                jsonUtf8.WriteStringValueSegment("foo".ToCharArray(), isFinalSegment: false);
                if (options.SkipValidation)
                {
                    write(jsonUtf8);
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => write(jsonUtf8));
                }
            }
        }

        [Theory]
        [MemberData(nameof(JsonOptions_TestData))]
        public void InvalidNumbersJson(JsonWriterOptions options)
        {
            var output = new ArrayBufferWriter<byte>(1024);

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                Assert.Throws<ArgumentException>(() => jsonUtf8.WriteNumberValue(double.NegativeInfinity));
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                Assert.Throws<ArgumentException>(() => jsonUtf8.WriteNumberValue(double.PositiveInfinity));
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                Assert.Throws<ArgumentException>(() => jsonUtf8.WriteNumberValue(double.NaN));
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                Assert.Throws<ArgumentException>(() => jsonUtf8.WriteNumberValue(float.PositiveInfinity));
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                Assert.Throws<ArgumentException>(() => jsonUtf8.WriteNumberValue(float.NegativeInfinity));
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                Assert.Throws<ArgumentException>(() => jsonUtf8.WriteNumberValue(float.NaN));
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteStartObject();
                Assert.Throws<ArgumentException>(() => jsonUtf8.WriteNumber("name", double.NegativeInfinity));
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteStartObject();
                Assert.Throws<ArgumentException>(() => jsonUtf8.WriteNumber("name", double.PositiveInfinity));
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteStartObject();
                Assert.Throws<ArgumentException>(() => jsonUtf8.WriteNumber("name", double.NaN));
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteStartObject();
                Assert.Throws<ArgumentException>(() => jsonUtf8.WriteNumber("name", float.PositiveInfinity));
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteStartObject();
                Assert.Throws<ArgumentException>(() => jsonUtf8.WriteNumber("name", float.NegativeInfinity));
            }

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteStartObject();
                Assert.Throws<ArgumentException>(() => jsonUtf8.WriteNumber("name", float.NaN));
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void InvalidJsonContinueShouldSucceed(bool formatted)
        {
            var options = new JsonWriterOptions { Indented = formatted, SkipValidation = true };
            var output = new ArrayBufferWriter<byte>(1024);

            using var jsonUtf8 = new Utf8JsonWriter(output, options);

            for (int i = 0; i < 100; i++)
            {
                jsonUtf8.WriteEndArray();
            }
            jsonUtf8.WriteStartArray();
            jsonUtf8.WriteEndArray();
            jsonUtf8.Flush();

            var sb = new StringBuilder();
            for (int i = 0; i < 100; i++)
            {
                if (options.Indented)
                    sb.Append(options.NewLine);
                sb.Append("]");
            }
            sb.Append(",");
            if (options.Indented)
                sb.Append(options.NewLine);
            sb.Append("[]");

            JsonTestHelper.AssertContents(sb.ToString(), output);
        }

        [Theory]
        [MemberData(nameof(JsonOptions_TestData))]
        public void WriteSeparateProperties(JsonWriterOptions options)
        {
            var output = new ArrayBufferWriter<byte>(1024);

            var stringWriter = new StringWriter();
            var json = new JsonTextWriter(stringWriter)
            {
                Formatting = options.Indented ? Formatting.Indented : Formatting.None,
            };

            json.WriteStartObject();
            json.WritePropertyName("foo1");
            json.WriteValue("bar1");
            json.WritePropertyName("foo2");
            json.WriteValue("bar2");
            json.WritePropertyName("foo3");
            json.WriteValue("bar3");
            json.WritePropertyName("foo4");
            json.WriteValue("bar4");
            json.WritePropertyName("array");
            json.WriteStartArray();
            json.WriteEndArray();
            json.WriteEnd();

            json.Flush();

            string expectedStr = HandleFormatting(stringWriter.ToString(), options);

            using var jsonUtf8 = new Utf8JsonWriter(output, options);
            jsonUtf8.WriteStartObject();
            jsonUtf8.WritePropertyName("foo1"u8);
            jsonUtf8.WriteStringValue("bar1");
            jsonUtf8.WritePropertyName("foo2");
            jsonUtf8.WriteStringValue("bar2");
            jsonUtf8.WritePropertyName(JsonEncodedText.Encode("foo3"));
            jsonUtf8.WriteStringValue("bar3");
            jsonUtf8.WritePropertyName("foo4".AsSpan());
            jsonUtf8.WriteStringValue("bar4");
            jsonUtf8.WritePropertyName("array");
            jsonUtf8.WriteStartArray();
            jsonUtf8.WriteEndArray();
            jsonUtf8.WriteEndObject();
            jsonUtf8.Flush();
            Assert.Equal(expectedStr, Encoding.UTF8.GetString(output.WrittenMemory.ToArray()));
        }

        [Theory]
        [MemberData(nameof(JsonOptions_TestData))]
        public void WritingTooDeep(JsonWriterOptions options)
        {
            var output = new ArrayBufferWriter<byte>(1024);

            using var jsonUtf8 = new Utf8JsonWriter(output, options);

            for (int i = 0; i < 1000; i++)
            {
                jsonUtf8.WriteStartArray();
            }
            Assert.Equal(1000, jsonUtf8.CurrentDepth);
            Assert.Throws<InvalidOperationException>(() => jsonUtf8.WriteStartArray());
        }

        [Theory]
        [MemberData(nameof(JsonOptions_TestData))]
        public void WritingTooDeepProperty(JsonWriterOptions options)
        {
            var capacity = 3 + 1000 * (11 + 1001 * options.IndentSize / 2);
            var output = new ArrayBufferWriter<byte>(capacity);
            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteStartObject();
                for (int i = 0; i < 999; i++)
                {
                    jsonUtf8.WriteStartObject("name");
                }
                Assert.Equal(1000, jsonUtf8.CurrentDepth);
                Assert.Throws<InvalidOperationException>(() => jsonUtf8.WriteStartArray("name"));
            }

            output = new ArrayBufferWriter<byte>(capacity);
            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteStartObject();
                for (int i = 0; i < 999; i++)
                {
                    jsonUtf8.WriteStartObject("name"u8);
                }
                Assert.Throws<InvalidOperationException>(() => jsonUtf8.WriteStartArray("name"u8));
            }

            output = new ArrayBufferWriter<byte>(capacity);
            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteStartObject();
                for (int i = 0; i < 999; i++)
                {
                    jsonUtf8.WriteStartObject(JsonEncodedText.Encode("name"));
                }
                Assert.Throws<InvalidOperationException>(() => jsonUtf8.WriteStartArray(JsonEncodedText.Encode("name")));
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(10)]
        [InlineData(2048)]
        [InlineData(1024 * 1024)]
        public static void CustomMaxDepth_DepthWithinLimit_ShouldSucceed(int maxDepth)
        {
            var options = new JsonWriterOptions { MaxDepth = maxDepth };
            int effectiveMaxDepth = maxDepth == 0 ? 1000 : maxDepth;

            var output = new ArrayBufferWriter<byte>();
            using var writer = new Utf8JsonWriter(output, options);

            for (int i = 0; i < effectiveMaxDepth; i++)
            {
                writer.WriteStartArray();
            }

            Assert.Equal(effectiveMaxDepth, writer.CurrentDepth);

            for (int i = 0; i < effectiveMaxDepth; i++)
            {
                writer.WriteEndArray();
            }

            Assert.Equal(0, writer.CurrentDepth);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(10)]
        [InlineData(2048)]
        [InlineData(1024 * 1024)]
        public static void CustomMaxDepth_DepthExceedingLimit_ShouldFail(int maxDepth)
        {
            var options = new JsonWriterOptions { MaxDepth = maxDepth };
            int effectiveMaxDepth = maxDepth == 0 ? 1000 : maxDepth;

            var output = new ArrayBufferWriter<byte>();
            using var writer = new Utf8JsonWriter(output, options);

            for (int i = 0; i < effectiveMaxDepth; i++)
            {
                writer.WriteStartArray();
            }

            Assert.Equal(effectiveMaxDepth, writer.CurrentDepth);

            Assert.Throws<InvalidOperationException>(() => writer.WriteStartArray());
        }

        // NOTE: WritingTooLargeProperty test is constrained to run on Windows and MacOSX because it causes
        //       problems on Linux due to the way deferred memory allocation works. On Linux, the allocation can
        //       succeed even if there is not enough memory but then the test may get killed by the OOM killer at the
        //       time the memory is accessed which triggers the full memory allocation.
        [PlatformSpecific(TestPlatforms.Windows | TestPlatforms.OSX)]
        [ConditionalTheory(typeof(Environment), nameof(Environment.Is64BitProcess))]
        [OuterLoop]
        [MemberData(nameof(JsonOptions_TestData))]
        public void WritingTooLargeProperty(JsonWriterOptions options)
        {
            try
            {
                byte[] key;
                char[] keyChars;

                key = new byte[MaxUnescapedTokenSize + 1];
                keyChars = new char[MaxUnescapedTokenSize + 1];

                key.AsSpan().Fill((byte)'a');
                keyChars.AsSpan().Fill('a');

                var output = new ArrayBufferWriter<byte>(1024);

                using (var jsonUtf8 = new Utf8JsonWriter(output, options))
                {
                    jsonUtf8.WriteStartObject();
                    Assert.Throws<ArgumentException>(() => jsonUtf8.WriteStartArray(keyChars));
                }

                using (var jsonUtf8 = new Utf8JsonWriter(output, options))
                {
                    jsonUtf8.WriteStartObject();
                    Assert.Throws<ArgumentException>(() => jsonUtf8.WriteStartArray(key));
                }
            }
            catch (OutOfMemoryException)
            {
                throw new SkipTestException("Out of memory allocating large objects");
            }
        }

        // NOTE: WritingTooLargePropertyStandalone test is constrained to run on Windows and MacOSX because it causes
        //       problems on Linux due to the way deferred memory allocation works. On Linux, the allocation can
        //       succeed even if there is not enough memory but then the test may get killed by the OOM killer at the
        //       time the memory is accessed which triggers the full memory allocation.
        [PlatformSpecific(TestPlatforms.Windows | TestPlatforms.OSX)]
        [ConditionalTheory(typeof(Environment), nameof(Environment.Is64BitProcess))]
        [OuterLoop]
        [MemberData(nameof(JsonOptions_TestData))]
        public void WritingTooLargePropertyStandalone(JsonWriterOptions options)
        {
            try
            {
                byte[] key;
                char[] keyChars;

                key = new byte[MaxUnescapedTokenSize + 1];
                keyChars = new char[MaxUnescapedTokenSize + 1];

                key.AsSpan().Fill((byte)'a');
                keyChars.AsSpan().Fill('a');

                    var output = new ArrayBufferWriter<byte>(1024);

                using (var jsonUtf8 = new Utf8JsonWriter(output, options))
                {
                    jsonUtf8.WriteStartObject();
                    Assert.Throws<ArgumentException>(() => jsonUtf8.WritePropertyName(keyChars));
                }

                using (var jsonUtf8 = new Utf8JsonWriter(output, options))
                {
                    jsonUtf8.WriteStartObject();
                    Assert.Throws<ArgumentException>(() => jsonUtf8.WritePropertyName(key));
                }
            }
            catch (OutOfMemoryException)
            {
                throw new SkipTestException("Out of memory allocating large objects");
            }
        }

        [ConditionalTheory(typeof(Environment), nameof(Environment.Is64BitProcess))]
        [OuterLoop]
        [MemberData(nameof(JsonOptions_TestData))]
        public void WritingTooLargeBase64Bytes(JsonWriterOptions options)
        {
            try
            {
                byte[] value = new byte[200_000_000];
                value.AsSpan().Fill((byte)'a');

                var output = new ArrayBufferWriter<byte>(value.Length);

                using (var jsonUtf8 = new Utf8JsonWriter(output, options))
                {
                    jsonUtf8.WriteBase64StringValue(value.AsSpan(0, 125_000_001));
                }
                Assert.InRange(output.WrittenCount, 125_000_001, int.MaxValue);
                output.Clear();

                using (var jsonUtf8 = new Utf8JsonWriter(output, options))
                {
                    Assert.Throws<ArgumentException>(() => jsonUtf8.WriteBase64String(value.AsSpan(0, 166_666_667), value.AsSpan(0, 1)));
                }

                using (var jsonUtf8 = new Utf8JsonWriter(output, options))
                {
                    Assert.Throws<ArgumentException>(() => jsonUtf8.WriteBase64String(Encoding.UTF8.GetString(value).ToCharArray().AsSpan(0, 166_666_667), value.AsSpan(0, 1)));
                }

                using (var jsonUtf8 = new Utf8JsonWriter(output, options))
                {
                    jsonUtf8.WriteBase64StringValue(value);
                }
                Assert.InRange(output.WrittenCount, Base64.GetMaxEncodedToUtf8Length(value.Length), int.MaxValue);
                output.Clear();

                using (var jsonUtf8 = new Utf8JsonWriter(output, options))
                {
                    jsonUtf8.WriteStartObject();
                    jsonUtf8.WriteBase64String("foo", value);
                }
                Assert.InRange(output.WrittenCount, Base64.GetMaxEncodedToUtf8Length(value.Length), int.MaxValue);
                output.Clear();

                using (var jsonUtf8 = new Utf8JsonWriter(output, options))
                {
                    jsonUtf8.WriteStartObject();
                    jsonUtf8.WriteBase64String("foo"u8, value);
                }
                Assert.InRange(output.WrittenCount, Base64.GetMaxEncodedToUtf8Length(value.Length), int.MaxValue);
                output.Clear();

                using (var jsonUtf8 = new Utf8JsonWriter(output, options))
                {
                    jsonUtf8.WriteStartObject();
                    jsonUtf8.WriteBase64String("foo".AsSpan(), value);
                }
                Assert.InRange(output.WrittenCount, Base64.GetMaxEncodedToUtf8Length(value.Length), int.MaxValue);
                output.Clear();

                using (var jsonUtf8 = new Utf8JsonWriter(output, options))
                {
                    jsonUtf8.WriteStartObject();
                    jsonUtf8.WriteBase64String(JsonEncodedText.Encode("foo"), value);
                }
                Assert.InRange(output.WrittenCount, Base64.GetMaxEncodedToUtf8Length(value.Length), int.MaxValue);
                output.Clear();
            }
            catch (OutOfMemoryException)
            {
                throw new SkipTestException("Out of memory allocating large objects");
            }
        }

        // NOTE: WritingTooLargeProperty test is constrained to run on Windows and MacOSX because it causes
        //       problems on Linux due to the way deferred memory allocation works. On Linux, the allocation can
        //       succeed even if there is not enough memory but then the test may get killed by the OOM killer at the
        //       time the memory is accessed which triggers the full memory allocation.
        [PlatformSpecific(TestPlatforms.Windows | TestPlatforms.OSX)]
        [ConditionalTheory(typeof(Environment), nameof(Environment.Is64BitProcess))]
        [OuterLoop]
        [MemberData(nameof(JsonOptions_TestData))]
        public void WritingHugeBase64Bytes(JsonWriterOptions options)
        {
            try
            {
                byte[] value = new byte[1_000_000_000];

                value.AsSpan().Fill(168);

                    var output = new ArrayBufferWriter<byte>(1024);

                using (var jsonUtf8 = new Utf8JsonWriter(output, options))
                {
                    jsonUtf8.WriteBase64StringValue(value);
                }

                output.Clear();
                using (var jsonUtf8 = new Utf8JsonWriter(output, options))
                {
                    jsonUtf8.WriteStartObject();
                    jsonUtf8.WriteBase64String("foo", value);
                    jsonUtf8.WriteEndObject();
                }

                output.Clear();
                using (var jsonUtf8 = new Utf8JsonWriter(output, options))
                {
                    jsonUtf8.WriteStartObject();
                    jsonUtf8.WriteBase64String("foo"u8, value);
                    jsonUtf8.WriteEndObject();
                }

                output.Clear();
                using (var jsonUtf8 = new Utf8JsonWriter(output, options))
                {
                    jsonUtf8.WriteStartObject();
                    jsonUtf8.WriteBase64String("foo".AsSpan(), value);
                    jsonUtf8.WriteEndObject();
                }

                output.Clear();
                using (var jsonUtf8 = new Utf8JsonWriter(output, options))
                {
                    jsonUtf8.WriteStartObject();
                    jsonUtf8.WriteBase64String(JsonEncodedText.Encode("foo"), value);
                    jsonUtf8.WriteEndObject();
                }
            }
            catch (OutOfMemoryException)
            {
                throw new SkipTestException("Out of memory allocating large objects");
            }
        }

        // https://github.com/dotnet/runtime/issues/30746
        [Theory, OuterLoop("Very long running test")]
        [MemberData(nameof(JsonOptions_TestData))]
        [SkipOnCoreClr("https://github.com/dotnet/runtime/issues/45464", ~RuntimeConfiguration.Release)]
        public void Writing3MBBase64Bytes(JsonWriterOptions options)
        {
            byte[] value = new byte[3 * 1024 * 1024];

            value.AsSpan().Fill(168);

            byte[] base64StringUtf8 = new byte[Base64.GetMaxEncodedToUtf8Length(value.Length)];
            Base64.EncodeToUtf8(value, base64StringUtf8, out _, out int bytesWritten);
            string expectedValue = Encoding.UTF8.GetString(base64StringUtf8.AsSpan(0, bytesWritten).ToArray());

            string expectedJson = options.Indented ? $"{{{options.NewLine}{GetIndentText(options)}\"foo\": \"{expectedValue}\"{options.NewLine}}}" : $"{{\"foo\":\"{expectedValue}\"}}";

            var output = new ArrayBufferWriter<byte>(1024);

            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteBase64StringValue(value);
            }
            JsonTestHelper.AssertContents($"\"{expectedValue}\"", output);

            output.Clear();
            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteStartObject();
                jsonUtf8.WriteBase64String("foo", value);
                jsonUtf8.WriteEndObject();
            }
            JsonTestHelper.AssertContents(expectedJson, output);

            output.Clear();
            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteStartObject();
                jsonUtf8.WriteBase64String("foo"u8, value);
                jsonUtf8.WriteEndObject();
            }
            JsonTestHelper.AssertContents(expectedJson, output);

            output.Clear();
            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteStartObject();
                jsonUtf8.WriteBase64String("foo".AsSpan(), value);
                jsonUtf8.WriteEndObject();
            }
            JsonTestHelper.AssertContents(expectedJson, output);

            output.Clear();
            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteStartObject();
                jsonUtf8.WriteBase64String(JsonEncodedText.Encode("foo"), value);
                jsonUtf8.WriteEndObject();
            }
            JsonTestHelper.AssertContents(expectedJson, output);
        }

        [Theory]
        [MemberData(nameof(JsonOptions_TestData))]
        public void WriteSingleValue(JsonWriterOptions options)
        {
            string expectedStr = "123456789012345";

            
            var output = new ArrayBufferWriter<byte>(1024);
            using var jsonUtf8 = new Utf8JsonWriter(output, options);

            jsonUtf8.WriteNumberValue(123456789012345);

            jsonUtf8.Flush();

            JsonTestHelper.AssertContents(expectedStr, output);
        }

        [Theory]
        [MemberData(nameof(JsonOptions_TestData))]
        public void WriteHelloWorld(JsonWriterOptions options)
        {
            string propertyName = "message";
            string value = "Hello, World!";
            string expectedStr = GetHelloWorldExpectedString(options, propertyName, value);

            JsonEncodedText encodedPropertyName = JsonEncodedText.Encode(propertyName);
            JsonEncodedText encodedValue = JsonEncodedText.Encode(value);

            ReadOnlySpan<byte> utf8PropertyName = "message"u8;
            ReadOnlySpan<byte> utf8Value = "Hello, World!"u8;


            for (int i = 0; i < 32; i++)
            {
                var output = new ArrayBufferWriter<byte>(32);
                using var jsonUtf8 = new Utf8JsonWriter(output, options);

                jsonUtf8.WriteStartObject();

                switch (i)
                {
                    case 0:
                        jsonUtf8.WriteString(propertyName, value);
                        jsonUtf8.WriteString(propertyName, value);
                        break;
                    case 1:
                        jsonUtf8.WriteString(propertyName, value.AsSpan());
                        jsonUtf8.WriteString(propertyName, value.AsSpan());
                        break;
                    case 2:
                        jsonUtf8.WriteString(propertyName, utf8Value);
                        jsonUtf8.WriteString(propertyName, utf8Value);
                        break;
                    case 3:
                        jsonUtf8.WriteString(propertyName.AsSpan(), value);
                        jsonUtf8.WriteString(propertyName.AsSpan(), value);
                        break;
                    case 4:
                        jsonUtf8.WriteString(propertyName.AsSpan(), value.AsSpan());
                        jsonUtf8.WriteString(propertyName.AsSpan(), value.AsSpan());
                        break;
                    case 5:
                        jsonUtf8.WriteString(propertyName.AsSpan(), utf8Value);
                        jsonUtf8.WriteString(propertyName.AsSpan(), utf8Value);
                        break;
                    case 6:
                        jsonUtf8.WriteString(utf8PropertyName, value);
                        jsonUtf8.WriteString(utf8PropertyName, value);
                        break;
                    case 7:
                        jsonUtf8.WriteString(utf8PropertyName, value.AsSpan());
                        jsonUtf8.WriteString(utf8PropertyName, value.AsSpan());
                        break;
                    case 8:
                        jsonUtf8.WriteString(utf8PropertyName, utf8Value);
                        jsonUtf8.WriteString(utf8PropertyName, utf8Value);
                        break;
                    case 9:
                        jsonUtf8.WriteString(encodedPropertyName, value);
                        jsonUtf8.WriteString(encodedPropertyName, value);
                        break;
                    case 10:
                        jsonUtf8.WriteString(encodedPropertyName, value.AsSpan());
                        jsonUtf8.WriteString(encodedPropertyName, value.AsSpan());
                        break;
                    case 11:
                        jsonUtf8.WriteString(encodedPropertyName, utf8Value);
                        jsonUtf8.WriteString(encodedPropertyName, utf8Value);
                        break;
                    case 12:
                        jsonUtf8.WriteString(encodedPropertyName, encodedValue);
                        jsonUtf8.WriteString(encodedPropertyName, encodedValue);
                        break;
                    case 13:
                        jsonUtf8.WriteString(propertyName, encodedValue);
                        jsonUtf8.WriteString(propertyName, encodedValue);
                        break;
                    case 14:
                        jsonUtf8.WriteString(propertyName.AsSpan(), encodedValue);
                        jsonUtf8.WriteString(propertyName.AsSpan(), encodedValue);
                        break;
                    case 15:
                        jsonUtf8.WriteString(utf8PropertyName, encodedValue);
                        jsonUtf8.WriteString(utf8PropertyName, encodedValue);
                        break;
                    case 16:
                        jsonUtf8.WritePropertyName(propertyName);
                        jsonUtf8.WriteStringValue(value);
                        jsonUtf8.WritePropertyName(propertyName);
                        jsonUtf8.WriteStringValue(value);
                        break;
                    case 17:
                        jsonUtf8.WritePropertyName(propertyName);
                        jsonUtf8.WriteStringValue(value.AsSpan());
                        jsonUtf8.WritePropertyName(propertyName);
                        jsonUtf8.WriteStringValue(value.AsSpan());
                        break;
                    case 18:
                        jsonUtf8.WritePropertyName(propertyName);
                        jsonUtf8.WriteStringValue(utf8Value);
                        jsonUtf8.WritePropertyName(propertyName);
                        jsonUtf8.WriteStringValue(utf8Value);
                        break;
                    case 19:
                        jsonUtf8.WritePropertyName(propertyName.AsSpan());
                        jsonUtf8.WriteStringValue(value);
                        jsonUtf8.WritePropertyName(propertyName.AsSpan());
                        jsonUtf8.WriteStringValue(value);
                        break;
                    case 20:
                        jsonUtf8.WritePropertyName(propertyName.AsSpan());
                        jsonUtf8.WriteStringValue(value.AsSpan());
                        jsonUtf8.WritePropertyName(propertyName.AsSpan());
                        jsonUtf8.WriteStringValue(value.AsSpan());
                        break;
                    case 21:
                        jsonUtf8.WritePropertyName(propertyName.AsSpan());
                        jsonUtf8.WriteStringValue(utf8Value);
                        jsonUtf8.WritePropertyName(propertyName.AsSpan());
                        jsonUtf8.WriteStringValue(utf8Value);
                        break;
                    case 22:
                        jsonUtf8.WritePropertyName(utf8PropertyName);
                        jsonUtf8.WriteStringValue(value);
                        jsonUtf8.WritePropertyName(utf8PropertyName);
                        jsonUtf8.WriteStringValue(value);
                        break;
                    case 23:
                        jsonUtf8.WritePropertyName(utf8PropertyName);
                        jsonUtf8.WriteStringValue(value.AsSpan());
                        jsonUtf8.WritePropertyName(utf8PropertyName);
                        jsonUtf8.WriteStringValue(value.AsSpan());
                        break;
                    case 24:
                        jsonUtf8.WritePropertyName(utf8PropertyName);
                        jsonUtf8.WriteStringValue(utf8Value);
                        jsonUtf8.WritePropertyName(utf8PropertyName);
                        jsonUtf8.WriteStringValue(utf8Value);
                        break;
                    case 25:
                        jsonUtf8.WritePropertyName(encodedPropertyName);
                        jsonUtf8.WriteStringValue(value);
                        jsonUtf8.WritePropertyName(encodedPropertyName);
                        jsonUtf8.WriteStringValue(value);
                        break;
                    case 26:
                        jsonUtf8.WritePropertyName(encodedPropertyName);
                        jsonUtf8.WriteStringValue(value.AsSpan());
                        jsonUtf8.WritePropertyName(encodedPropertyName);
                        jsonUtf8.WriteStringValue(value.AsSpan());
                        break;
                    case 27:
                        jsonUtf8.WritePropertyName(encodedPropertyName);
                        jsonUtf8.WriteStringValue(utf8Value);
                        jsonUtf8.WritePropertyName(encodedPropertyName);
                        jsonUtf8.WriteStringValue(utf8Value);
                        break;
                    case 28:
                        jsonUtf8.WritePropertyName(encodedPropertyName);
                        jsonUtf8.WriteStringValue(encodedValue);
                        jsonUtf8.WritePropertyName(encodedPropertyName);
                        jsonUtf8.WriteStringValue(encodedValue);
                        break;
                    case 29:
                        jsonUtf8.WritePropertyName(propertyName);
                        jsonUtf8.WriteStringValue(encodedValue);
                        jsonUtf8.WritePropertyName(propertyName);
                        jsonUtf8.WriteStringValue(encodedValue);
                        break;
                    case 30:
                        jsonUtf8.WritePropertyName(propertyName.AsSpan());
                        jsonUtf8.WriteStringValue(encodedValue);
                        jsonUtf8.WritePropertyName(propertyName.AsSpan());
                        jsonUtf8.WriteStringValue(encodedValue);
                        break;
                    case 31:
                        jsonUtf8.WritePropertyName(utf8PropertyName);
                        jsonUtf8.WriteStringValue(encodedValue);
                        jsonUtf8.WritePropertyName(utf8PropertyName);
                        jsonUtf8.WriteStringValue(encodedValue);
                        break;
                }

                jsonUtf8.WriteEndObject();
                jsonUtf8.Flush();

                JsonTestHelper.AssertContents(expectedStr, output);
            }
        }

        [Theory]
        [MemberData(nameof(JsonOptions_TestData))]
        public void WriteHelloWorldEscaped(JsonWriterOptions options)
        {
            string propertyName = "mess><age";
            string value = "Hello,>< World!";
            string expectedStr = GetHelloWorldExpectedString(options, propertyName, value);

            ReadOnlySpan<char> propertyNameSpan = propertyName.AsSpan();
            ReadOnlySpan<char> valueSpan = value.AsSpan();
            ReadOnlySpan<byte> propertyNameSpanUtf8 = Encoding.UTF8.GetBytes(propertyName);
            ReadOnlySpan<byte> valueSpanUtf8 = Encoding.UTF8.GetBytes(value);

            JsonEncodedText encodedPropertyName = JsonEncodedText.Encode(propertyName);
            JsonEncodedText encodedValue = JsonEncodedText.Encode(value);

            for (int i = 0; i < 32; i++)
            {
                var output = new ArrayBufferWriter<byte>(32);
                using var jsonUtf8 = new Utf8JsonWriter(output, options);

                jsonUtf8.WriteStartObject();

                switch (i)
                {
                    case 0:
                        jsonUtf8.WriteString(propertyName, value);
                        jsonUtf8.WriteString(propertyName, value);
                        break;
                    case 1:
                        jsonUtf8.WriteString(propertyName, valueSpan);
                        jsonUtf8.WriteString(propertyName, valueSpan);
                        break;
                    case 2:
                        jsonUtf8.WriteString(propertyName, valueSpanUtf8);
                        jsonUtf8.WriteString(propertyName, valueSpanUtf8);
                        break;
                    case 3:
                        jsonUtf8.WriteString(propertyNameSpan, value);
                        jsonUtf8.WriteString(propertyNameSpan, value);
                        break;
                    case 4:
                        jsonUtf8.WriteString(propertyNameSpan, valueSpan);
                        jsonUtf8.WriteString(propertyNameSpan, valueSpan);
                        break;
                    case 5:
                        jsonUtf8.WriteString(propertyNameSpan, valueSpanUtf8);
                        jsonUtf8.WriteString(propertyNameSpan, valueSpanUtf8);
                        break;
                    case 6:
                        jsonUtf8.WriteString(propertyNameSpanUtf8, value);
                        jsonUtf8.WriteString(propertyNameSpanUtf8, value);
                        break;
                    case 7:
                        jsonUtf8.WriteString(propertyNameSpanUtf8, valueSpan);
                        jsonUtf8.WriteString(propertyNameSpanUtf8, valueSpan);
                        break;
                    case 8:
                        jsonUtf8.WriteString(propertyNameSpanUtf8, valueSpanUtf8);
                        jsonUtf8.WriteString(propertyNameSpanUtf8, valueSpanUtf8);
                        break;
                    case 9:
                        jsonUtf8.WriteString(encodedPropertyName, value);
                        jsonUtf8.WriteString(encodedPropertyName, value);
                        break;
                    case 10:
                        jsonUtf8.WriteString(encodedPropertyName, value.AsSpan());
                        jsonUtf8.WriteString(encodedPropertyName, value.AsSpan());
                        break;
                    case 11:
                        jsonUtf8.WriteString(encodedPropertyName, valueSpanUtf8);
                        jsonUtf8.WriteString(encodedPropertyName, valueSpanUtf8);
                        break;
                    case 12:
                        jsonUtf8.WriteString(encodedPropertyName, encodedValue);
                        jsonUtf8.WriteString(encodedPropertyName, encodedValue);
                        break;
                    case 13:
                        jsonUtf8.WriteString(propertyName, encodedValue);
                        jsonUtf8.WriteString(propertyName, encodedValue);
                        break;
                    case 14:
                        jsonUtf8.WriteString(propertyName.AsSpan(), encodedValue);
                        jsonUtf8.WriteString(propertyName.AsSpan(), encodedValue);
                        break;
                    case 15:
                        jsonUtf8.WriteString(propertyNameSpanUtf8, encodedValue);
                        jsonUtf8.WriteString(propertyNameSpanUtf8, encodedValue);
                        break;
                    case 16:
                        jsonUtf8.WritePropertyName(propertyName);
                        jsonUtf8.WriteStringValue(value);
                        jsonUtf8.WritePropertyName(propertyName);
                        jsonUtf8.WriteStringValue(value);
                        break;
                    case 17:
                        jsonUtf8.WritePropertyName(propertyName);
                        jsonUtf8.WriteStringValue(valueSpan);
                        jsonUtf8.WritePropertyName(propertyName);
                        jsonUtf8.WriteStringValue(valueSpan);
                        break;
                    case 18:
                        jsonUtf8.WritePropertyName(propertyName);
                        jsonUtf8.WriteStringValue(valueSpanUtf8);
                        jsonUtf8.WritePropertyName(propertyName);
                        jsonUtf8.WriteStringValue(valueSpanUtf8);
                        break;
                    case 19:
                        jsonUtf8.WritePropertyName(propertyNameSpan);
                        jsonUtf8.WriteStringValue(value);
                        jsonUtf8.WritePropertyName(propertyNameSpan);
                        jsonUtf8.WriteStringValue(value);
                        break;
                    case 20:
                        jsonUtf8.WritePropertyName(propertyNameSpan);
                        jsonUtf8.WriteStringValue(valueSpan);
                        jsonUtf8.WritePropertyName(propertyNameSpan);
                        jsonUtf8.WriteStringValue(valueSpan);
                        break;
                    case 21:
                        jsonUtf8.WritePropertyName(propertyNameSpan);
                        jsonUtf8.WriteStringValue(valueSpanUtf8);
                        jsonUtf8.WritePropertyName(propertyNameSpan);
                        jsonUtf8.WriteStringValue(valueSpanUtf8);
                        break;
                    case 22:
                        jsonUtf8.WritePropertyName(propertyNameSpanUtf8);
                        jsonUtf8.WriteStringValue(value);
                        jsonUtf8.WritePropertyName(propertyNameSpanUtf8);
                        jsonUtf8.WriteStringValue(value);
                        break;
                    case 23:
                        jsonUtf8.WritePropertyName(propertyNameSpanUtf8);
                        jsonUtf8.WriteStringValue(valueSpan);
                        jsonUtf8.WritePropertyName(propertyNameSpanUtf8);
                        jsonUtf8.WriteStringValue(valueSpan);
                        break;
                    case 24:
                        jsonUtf8.WritePropertyName(propertyNameSpanUtf8);
                        jsonUtf8.WriteStringValue(valueSpanUtf8);
                        jsonUtf8.WritePropertyName(propertyNameSpanUtf8);
                        jsonUtf8.WriteStringValue(valueSpanUtf8);
                        break;
                    case 25:
                        jsonUtf8.WritePropertyName(encodedPropertyName);
                        jsonUtf8.WriteStringValue(value);
                        jsonUtf8.WritePropertyName(encodedPropertyName);
                        jsonUtf8.WriteStringValue(value);
                        break;
                    case 26:
                        jsonUtf8.WritePropertyName(encodedPropertyName);
                        jsonUtf8.WriteStringValue(value.AsSpan());
                        jsonUtf8.WritePropertyName(encodedPropertyName);
                        jsonUtf8.WriteStringValue(value.AsSpan());
                        break;
                    case 27:
                        jsonUtf8.WritePropertyName(encodedPropertyName);
                        jsonUtf8.WriteStringValue(valueSpanUtf8);
                        jsonUtf8.WritePropertyName(encodedPropertyName);
                        jsonUtf8.WriteStringValue(valueSpanUtf8);
                        break;
                    case 28:
                        jsonUtf8.WritePropertyName(encodedPropertyName);
                        jsonUtf8.WriteStringValue(encodedValue);
                        jsonUtf8.WritePropertyName(encodedPropertyName);
                        jsonUtf8.WriteStringValue(encodedValue);
                        break;
                    case 29:
                        jsonUtf8.WritePropertyName(propertyName);
                        jsonUtf8.WriteStringValue(encodedValue);
                        jsonUtf8.WritePropertyName(propertyName);
                        jsonUtf8.WriteStringValue(encodedValue);
                        break;
                    case 30:
                        jsonUtf8.WritePropertyName(propertyName.AsSpan());
                        jsonUtf8.WriteStringValue(encodedValue);
                        jsonUtf8.WritePropertyName(propertyName.AsSpan());
                        jsonUtf8.WriteStringValue(encodedValue);
                        break;
                    case 31:
                        jsonUtf8.WritePropertyName(propertyNameSpanUtf8);
                        jsonUtf8.WriteStringValue(encodedValue);
                        jsonUtf8.WritePropertyName(propertyNameSpanUtf8);
                        jsonUtf8.WriteStringValue(encodedValue);
                        break;
                }

                jsonUtf8.WriteEndObject();
                jsonUtf8.Flush();

                JsonTestHelper.AssertContents(expectedStr, output);
            }

            // Verify that escaping does not change the input strings/spans.
            Assert.Equal("mess><age", propertyName);
            Assert.Equal("Hello,>< World!", value);
            Assert.True(propertyName.AsSpan().SequenceEqual(propertyNameSpan));
            Assert.True(value.AsSpan().SequenceEqual(valueSpan));
            Assert.True(Encoding.UTF8.GetBytes(propertyName).AsSpan().SequenceEqual(propertyNameSpanUtf8));
            Assert.True(Encoding.UTF8.GetBytes(value).AsSpan().SequenceEqual(valueSpanUtf8));
        }

        [Theory]
        [MemberData(nameof(JsonOptions_TestData))]
        public void WritePartialHelloWorld(JsonWriterOptions options)
        {
            var output = new ArrayBufferWriter<byte>(10);
            using var jsonUtf8 = new Utf8JsonWriter(output, options);

            jsonUtf8.WriteStartObject();

            Assert.Equal(0, jsonUtf8.BytesCommitted);
            Assert.Equal(1, jsonUtf8.BytesPending);

            jsonUtf8.WriteString("message", "Hello, World!");

            Assert.Equal(0, jsonUtf8.BytesCommitted);
            if (options.Indented)
                Assert.Equal(26 + options.IndentSize + options.NewLine.Length + 1, jsonUtf8.BytesPending); // new lines, indentation, white space
            else
                Assert.Equal(26, jsonUtf8.BytesPending);

            jsonUtf8.Flush();

            if (options.Indented)
                Assert.Equal(26 + options.IndentSize + options.NewLine.Length + 1, jsonUtf8.BytesCommitted); // new lines, indentation, white space
            else
                Assert.Equal(26, jsonUtf8.BytesCommitted);

            Assert.Equal(0, jsonUtf8.BytesPending);

            jsonUtf8.WriteString("message", "Hello, World!");
            jsonUtf8.WriteEndObject();

            if (options.Indented)
                Assert.Equal(26 + options.IndentSize + options.NewLine.Length + 1, jsonUtf8.BytesCommitted);
            else
                Assert.Equal(26, jsonUtf8.BytesCommitted);

            if (options.Indented)
                Assert.Equal(27 + options.IndentSize + (2 * options.NewLine.Length) + 1, jsonUtf8.BytesPending); // new lines, indentation, white space
            else
                Assert.Equal(27, jsonUtf8.BytesPending);

            jsonUtf8.Flush();

            if (options.Indented)
                Assert.Equal(53 + (2 * options.IndentSize) + (3 * options.NewLine.Length) + (1 * 2), jsonUtf8.BytesCommitted); // new lines, indentation, white space
            else
                Assert.Equal(53, jsonUtf8.BytesCommitted);

            Assert.Equal(0, jsonUtf8.BytesPending);
        }

        public static IEnumerable<object[]> WriteBase64String_TestData() =>
            JsonOptionsWith([
                "message",
                "escape mess><age",
                "<write base64 string when escape length bigger than given string",
                ">>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>"
            ]);

        [Theory]
        [MemberData(nameof(WriteBase64String_TestData))]
        public void WriteBase64String(JsonWriterOptions options, string inputValue)
        {
            string propertyName = inputValue;
            byte[] value = { 1, 2, 3, 4, 5, 6 };
            string expectedStr = GetBase64ExpectedString(options, propertyName, value);

            ReadOnlySpan<char> propertyNameSpan = propertyName.AsSpan();
            ReadOnlySpan<byte> propertyNameSpanUtf8 = Encoding.UTF8.GetBytes(propertyName);
            JsonEncodedText encodedPropertyName = JsonEncodedText.Encode(propertyName);

            for (int i = 0; i < 4; i++)
            {
                var output = new ArrayBufferWriter<byte>(32);
                using var jsonUtf8 = new Utf8JsonWriter(output, options);

                jsonUtf8.WriteStartObject();

                switch (i)
                {
                    case 0:
                        jsonUtf8.WriteBase64String(propertyName, value);
                        jsonUtf8.WriteBase64String(propertyName, value);
                        break;
                    case 1:
                        jsonUtf8.WriteBase64String(propertyNameSpan, value);
                        jsonUtf8.WriteBase64String(propertyNameSpan, value);
                        break;
                    case 2:
                        jsonUtf8.WriteBase64String(propertyNameSpanUtf8, value);
                        jsonUtf8.WriteBase64String(propertyNameSpanUtf8, value);
                        break;
                    case 3:
                        jsonUtf8.WriteBase64String(encodedPropertyName, value);
                        jsonUtf8.WriteBase64String(encodedPropertyName, value);
                        break;
                }

                jsonUtf8.WriteStartArray("array");
                jsonUtf8.WriteBase64StringValue(new byte[] { 1, 2 });
                jsonUtf8.WriteBase64StringValue(new byte[] { 3, 4 });
                jsonUtf8.WriteEndArray();

                jsonUtf8.WriteEndObject();
                jsonUtf8.Flush();

                JsonTestHelper.AssertContents(expectedStr, output);
            }

            // Verify that escaping does not change the input strings/spans.
            Assert.Equal(inputValue, propertyName);
            Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6 }, value);
            Assert.True(propertyName.AsSpan().SequenceEqual(propertyNameSpan));
            Assert.True(Encoding.UTF8.GetBytes(propertyName).AsSpan().SequenceEqual(propertyNameSpanUtf8));
        }

        [Theory]
        [MemberData(nameof(JsonOptions_TestData))]
        public void WritePartialBase64String(JsonWriterOptions options)
        {
            var output = new ArrayBufferWriter<byte>(10);
            using var jsonUtf8 = new Utf8JsonWriter(output, options);

            jsonUtf8.WriteStartObject();

            Assert.Equal(0, jsonUtf8.BytesCommitted);
            Assert.Equal(1, jsonUtf8.BytesPending);

            jsonUtf8.WriteBase64String("message", new byte[] { 201, 153, 199 });

            Assert.Equal(0, jsonUtf8.BytesCommitted);
            if (options.Indented)
                Assert.Equal(17 + options.IndentSize + options.NewLine.Length + 1, jsonUtf8.BytesPending); // new lines, indentation, white space
            else
                Assert.Equal(17, jsonUtf8.BytesPending);

            jsonUtf8.Flush();

            if (options.Indented)
                Assert.Equal(17 + options.IndentSize + options.NewLine.Length + 1, jsonUtf8.BytesCommitted); // new lines, indentation, white space
            else
                Assert.Equal(17, jsonUtf8.BytesCommitted);

            Assert.Equal(0, jsonUtf8.BytesPending);

            jsonUtf8.WriteBase64String("message", new byte[] { 201, 153, 199 });
            jsonUtf8.WriteEndObject();

            if (options.Indented)
                Assert.Equal(17 + options.IndentSize + options.NewLine.Length + 1, jsonUtf8.BytesCommitted);
            else
                Assert.Equal(17, jsonUtf8.BytesCommitted);

            if (options.Indented)
                Assert.Equal(18 + options.IndentSize + (2 * options.NewLine.Length) + 1, jsonUtf8.BytesPending); // new lines, indentation, white space
            else
                Assert.Equal(18, jsonUtf8.BytesPending);

            jsonUtf8.Flush();

            if (options.Indented)
                Assert.Equal(35 + (2 * options.IndentSize) + (3 * options.NewLine.Length) + (1 * 2), jsonUtf8.BytesCommitted); // new lines, indentation, white space
            else
                Assert.Equal(35, jsonUtf8.BytesCommitted);

            Assert.Equal(0, jsonUtf8.BytesPending);
        }

        [Theory]
        [MemberData(nameof(JsonOptions_TestData))]
        public void WriteInvalidPartialJson(JsonWriterOptions options)
        {
            var output = new ArrayBufferWriter<byte>(10);
            using var jsonUtf8 = new Utf8JsonWriter(output, options);

            jsonUtf8.WriteStartObject();

            Assert.Equal(0, jsonUtf8.BytesCommitted);
            Assert.Equal(1, jsonUtf8.BytesPending);

            jsonUtf8.Flush();

            Assert.Equal(1, jsonUtf8.BytesCommitted);
            Assert.Equal(0, jsonUtf8.BytesPending);

            if (options.SkipValidation)
            {
                jsonUtf8.WriteStringValue("Hello, World!");
                jsonUtf8.WriteEndArray();
            }
            else
            {
                Assert.Throws<InvalidOperationException>(() => jsonUtf8.WriteStringValue("Hello, World!"));
                Assert.Throws<InvalidOperationException>(() => jsonUtf8.WriteEndArray());
            }
        }

        [Theory]
        [MemberData(nameof(JsonOptions_TestData))]
        public void WriteInvalidBase64(JsonWriterOptions options)
        {
            {
                var output = new ArrayBufferWriter<byte>(10);
                using var jsonUtf8 = new Utf8JsonWriter(output, options);

                jsonUtf8.WriteStartObject();

                Assert.Equal(0, jsonUtf8.BytesCommitted);
                Assert.Equal(1, jsonUtf8.BytesPending);

                jsonUtf8.Flush();

                Assert.Equal(1, jsonUtf8.BytesCommitted);
                Assert.Equal(0, jsonUtf8.BytesPending);

                if (options.SkipValidation)
                {
                    jsonUtf8.WriteBase64StringValue(new byte[] { 1, 2 });
                    jsonUtf8.WriteEndArray();
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => jsonUtf8.WriteBase64StringValue(new byte[] { 1, 2 }));
                    Assert.Throws<InvalidOperationException>(() => jsonUtf8.WriteEndArray());
                }
            }
            {
                var output = new ArrayBufferWriter<byte>(10);
                using var jsonUtf8 = new Utf8JsonWriter(output, options);

                jsonUtf8.WriteStartArray();

                Assert.Equal(0, jsonUtf8.BytesCommitted);
                Assert.Equal(1, jsonUtf8.BytesPending);

                jsonUtf8.Flush();

                Assert.Equal(1, jsonUtf8.BytesCommitted);
                Assert.Equal(0, jsonUtf8.BytesPending);

                if (options.SkipValidation)
                {
                    jsonUtf8.WriteBase64String("foo", new byte[] { 1, 2 });
                    jsonUtf8.WriteEndObject();
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => jsonUtf8.WriteBase64String("foo", new byte[] { 1, 2 }));
                    Assert.Throws<InvalidOperationException>(() => jsonUtf8.WriteEndObject());
                }
            }
        }

        [Fact]
        public void WriteBase64DoesNotEscape()
        {
            var output = new ArrayBufferWriter<byte>(10);
            using var jsonUtf8 = new Utf8JsonWriter(output);

            var bytes = new byte[3] { 0xFB, 0xEF, 0xBE };
            jsonUtf8.WriteBase64StringValue(bytes);

            jsonUtf8.Flush();

            JsonTestHelper.AssertContents("\"++++\"", output);
        }

        [Fact]
        public void WriteBase64DoesNotEscapeLarge()
        {
            var output = new ArrayBufferWriter<byte>(10);
            using var jsonUtf8 = new Utf8JsonWriter(output);

            var bytes = new byte[200];

            bytes.AsSpan().Fill(100);
            bytes[4] = 0xFB;
            bytes[5] = 0xEF;
            bytes[6] = 0xBE;
            bytes[15] = 0;
            bytes[16] = 0x10;
            bytes[17] = 0xBF;

            jsonUtf8.WriteBase64StringValue(bytes);

            jsonUtf8.Flush();

            var builder = new StringBuilder();
            builder.Append("\"ZGRkZPvvvmRkZGRkZGRkABC/");
            for (int i = 0; i < 60; i++)
            {
                builder.Append("ZGRk");
            }
            builder.Append("ZGQ=\"");
            JsonTestHelper.AssertContents(builder.ToString(), output);
        }

        [Theory]
        [MemberData(nameof(JsonOptions_TestData))]
        public void WriteInvalidDepthPartial(JsonWriterOptions options)
        {
            {
                    var output = new ArrayBufferWriter<byte>(10);
                using var jsonUtf8 = new Utf8JsonWriter(output, options);

                jsonUtf8.WriteStartObject();
                jsonUtf8.WriteEndObject();
                jsonUtf8.Flush();

                Assert.Equal(0, jsonUtf8.CurrentDepth);

                if (options.SkipValidation)
                {
                    jsonUtf8.WriteStartObject();
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => jsonUtf8.WriteStartObject());
                }
            }

            {
                    var output = new ArrayBufferWriter<byte>(10);
                using var jsonUtf8 = new Utf8JsonWriter(output, options);

                jsonUtf8.WriteStartObject();
                jsonUtf8.WriteEndObject();
                jsonUtf8.Flush();

                if (options.SkipValidation)
                {
                    jsonUtf8.WriteStartObject("name");
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => jsonUtf8.WriteStartObject("name"));
                }
            }
        }

        public static IEnumerable<object[]> WriteComments_TestData() =>
            JsonOptionsWith([
                "comment",
                "comm><ent",
                ">>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>"
            ]);

        [Theory]
        [MemberData(nameof(WriteComments_TestData))]
        public void WriteCommentsInArray(JsonWriterOptions options, string comment)
        {
            string expectedStr = GetCommentInArrayExpectedString(options, comment);

            for (int i = 0; i < 3; i++)
            {
                var output = new ArrayBufferWriter<byte>(32);
                using var jsonUtf8 = new Utf8JsonWriter(output, options);

                WriteCommentValue(jsonUtf8, i, comment);
                jsonUtf8.WriteStartArray();

                for (int j = 0; j < 10; j++)
                {
                    WriteCommentValue(jsonUtf8, i, comment);
                }

                switch (i)
                {
                    case 0:
                        jsonUtf8.WriteStringValue(comment);
                        break;
                    case 1:
                        jsonUtf8.WriteStringValue(comment.AsSpan());
                        break;
                    case 2:
                        jsonUtf8.WriteStringValue(Encoding.UTF8.GetBytes(comment));
                        break;
                }

                WriteCommentValue(jsonUtf8, i, comment);

                jsonUtf8.WriteStartArray();
                jsonUtf8.WriteEndArray();

                WriteCommentValue(jsonUtf8, i, comment);

                jsonUtf8.WriteStartObject();
                jsonUtf8.WriteEndObject();

                WriteCommentValue(jsonUtf8, i, comment);

                jsonUtf8.WriteEndArray();

                WriteCommentValue(jsonUtf8, i, comment);

                jsonUtf8.Flush();

                JsonTestHelper.AssertContents(expectedStr, output);
            }
        }

        [Theory]
        [MemberData(nameof(WriteComments_TestData))]
        public void WriteCommentsInObject(JsonWriterOptions options, string comment)
        {
            string expectedStr = GetCommentInObjectExpectedString(options, comment);

            for (int i = 0; i < 3; i++)
            {
                var output = new ArrayBufferWriter<byte>();
                using var jsonUtf8 = new Utf8JsonWriter(output, options);

                WriteCommentValue(jsonUtf8, i, comment);

                jsonUtf8.WriteStartObject();

                WriteCommentValue(jsonUtf8, i, comment);

                jsonUtf8.WritePropertyName("property1");
                WriteCommentValue(jsonUtf8, i, comment);
                jsonUtf8.WriteStartArray();
                jsonUtf8.WriteEndArray();

                WriteCommentValue(jsonUtf8, i, comment);

                jsonUtf8.WritePropertyName("property2");
                WriteCommentValue(jsonUtf8, i, comment);
                jsonUtf8.WriteStartObject();
                jsonUtf8.WriteEndObject();

                WriteCommentValue(jsonUtf8, i, comment);

                jsonUtf8.WriteEndObject();

                WriteCommentValue(jsonUtf8, i, comment);

                jsonUtf8.Flush();

                JsonTestHelper.AssertContents(expectedStr, output);
            }
        }

        private static void WriteCommentValue(Utf8JsonWriter jsonUtf8, int i, string comment)
        {
            switch (i)
            {
                case 0:
                    jsonUtf8.WriteCommentValue(comment);
                    break;
                case 1:
                    jsonUtf8.WriteCommentValue(comment.AsSpan());
                    break;
                case 2:
                    jsonUtf8.WriteCommentValue(Encoding.UTF8.GetBytes(comment));
                    break;
            }
        }

        [Theory]
        [MemberData(nameof(JsonOptions_TestData))]
        public void WriteInvalidComment(JsonWriterOptions options)
        {
            var output = new ArrayBufferWriter<byte>(32);
            using var jsonUtf8 = new Utf8JsonWriter(output, options);

            string comment = "comment is */ invalid";
            Assert.Throws<ArgumentException>(() => jsonUtf8.WriteCommentValue(comment));
            Assert.Throws<ArgumentException>(() => jsonUtf8.WriteCommentValue(comment.AsSpan()));
            Assert.Throws<ArgumentException>(() => jsonUtf8.WriteCommentValue(Encoding.UTF8.GetBytes(comment)));

            comment = "comment with unpaired surrogate \udc00";
            Assert.Throws<ArgumentException>(() => jsonUtf8.WriteCommentValue(comment));
            Assert.Throws<ArgumentException>(() => jsonUtf8.WriteCommentValue(comment.AsSpan()));

            var invalidUtf8 = new byte[2] { 0xc3, 0x28 };
            Assert.Throws<ArgumentException>(() => jsonUtf8.WriteCommentValue(invalidUtf8));
        }

        [Theory]
        [MemberData(nameof(JsonOptions_TestData))]
        public void WriteCommentsInvalidTextAllowed(JsonWriterOptions options)
        {
            var output = new ArrayBufferWriter<byte>(32);
            using var jsonUtf8 = new Utf8JsonWriter(output, options);

            jsonUtf8.WriteStartArray();

            string comment = "comment is * / valid";
            jsonUtf8.WriteCommentValue(comment);
            jsonUtf8.WriteCommentValue(comment.AsSpan());
            jsonUtf8.WriteCommentValue(Encoding.UTF8.GetBytes(comment));

            comment = "comment is /* valid";
            jsonUtf8.WriteCommentValue(comment);
            jsonUtf8.WriteCommentValue(comment.AsSpan());
            jsonUtf8.WriteCommentValue(Encoding.UTF8.GetBytes(comment));

            comment = "comment is / * valid";
            jsonUtf8.WriteCommentValue(comment);
            jsonUtf8.WriteCommentValue(comment.AsSpan());

            jsonUtf8.Flush();
            string expectedStr = GetCommentExpectedString(options);
            JsonTestHelper.AssertContents(expectedStr, output);
        }

        private static string GetCommentExpectedString(JsonWriterOptions options)
        {
            var ms = new MemoryStream();
            TextWriter streamWriter = new StreamWriter(ms, new UTF8Encoding(false), 1024, true);

            var json = new JsonTextWriter(streamWriter)
            {
                Formatting = options.Indented ? Formatting.Indented : Formatting.None,
                StringEscapeHandling = StringEscapeHandling.EscapeHtml,
            };

            json.WriteStartArray();

            string comment = "comment is * / valid";
            json.WriteComment(comment);
            json.WriteComment(comment);
            json.WriteComment(comment);

            comment = "comment is /* valid";
            json.WriteComment(comment);
            json.WriteComment(comment);
            json.WriteComment(comment);

            comment = "comment is / * valid";
            json.WriteComment(comment);
            json.WriteComment(comment);

            json.Flush();

            return HandleFormatting(Encoding.UTF8.GetString(ms.ToArray()), options);
        }

        [Theory]
        [InlineData(true, ExpectedIndentedCommentJsonOfArray)]
        [InlineData(false, ExpectedNonIndentedCommentJsonOfArray)]
        public void WriteCommentsInArray_ComparedWithStringLiteral(bool formatted, string expectedJson)
        {
            var options = new JsonWriterOptions { Indented = formatted, SkipValidation = false };

            for (int i = 0; i < 3; i++)
            {
                var output = new ArrayBufferWriter<byte>(32);
                using var jsonUtf8 = new Utf8JsonWriter(output, options);

                WriteCommentValue(jsonUtf8, i, "Comment at start of doc");
                WriteCommentValue(jsonUtf8, i, "Multiple comment line");
                jsonUtf8.WriteStartArray();

                WriteCommentValue(jsonUtf8, i, "Comment as first array item");
                WriteCommentValue(jsonUtf8, i, "Multiple comment line");

                jsonUtf8.WriteStartArray();
                jsonUtf8.WriteEndArray();

                WriteCommentValue(jsonUtf8, i, "Comment in the middle of array");

                jsonUtf8.WriteStartObject();
                jsonUtf8.WriteEndObject();

                WriteCommentValue(jsonUtf8, i, "Comment as the last array item");

                jsonUtf8.WriteEndArray();

                WriteCommentValue(jsonUtf8, i, "Comment at end of doc");

                jsonUtf8.Flush();

                JsonTestHelper.AssertContents(expectedJson, output);
            }
        }

        [Theory]
        [InlineData(true, ExpectedIndentedCommentJsonOfObject)]
        [InlineData(false, ExpectedNonIndentedCommentJsonOfObject)]
        public void WriteCommentsInObject_ComparedWithStringLiteral(bool formatted, string expectedJson)
        {
            var options = new JsonWriterOptions { Indented = formatted, SkipValidation = false };

            for (int i = 0; i < 3; i++)
            {
                var output = new ArrayBufferWriter<byte>(32);
                using var jsonUtf8 = new Utf8JsonWriter(output, options);

                WriteCommentValue(jsonUtf8, i, "Comment at start of doc");
                WriteCommentValue(jsonUtf8, i, "Multiple comment line");
                jsonUtf8.WriteStartObject();

                WriteCommentValue(jsonUtf8, i, "Comment before first object property");
                WriteCommentValue(jsonUtf8, i, "Multiple comment line");

                jsonUtf8.WritePropertyName("property1");
                WriteCommentValue(jsonUtf8, i, "Comment of string property value");
                jsonUtf8.WriteStringValue("stringValue");

                jsonUtf8.WritePropertyName("property2");
                WriteCommentValue(jsonUtf8, i, "Comment of object property value");
                jsonUtf8.WriteStartObject();
                jsonUtf8.WriteEndObject();

                WriteCommentValue(jsonUtf8, i, "Comment in the middle of object");

                jsonUtf8.WritePropertyName("property3");
                WriteCommentValue(jsonUtf8, i, "Comment of array property value");
                jsonUtf8.WriteStartArray();
                jsonUtf8.WriteEndArray();

                WriteCommentValue(jsonUtf8, i, "Comment after the last property");

                jsonUtf8.WriteEndObject();

                WriteCommentValue(jsonUtf8, i, "Comment at end of doc");

                jsonUtf8.Flush();

                JsonTestHelper.AssertContents(expectedJson, output);
            }
        }

        [Theory]
        [MemberData(nameof(JsonOptions_TestData))]
        public void WriteStrings(JsonWriterOptions options)
        {
            string value = "temp";
            string expectedStr = GetStringsExpectedString(options, value);
            
            for (int i = 0; i < 3; i++)
            {
                var output = new ArrayBufferWriter<byte>(1024);
                using var jsonUtf8 = new Utf8JsonWriter(output, options);

                jsonUtf8.WriteStartArray();

                for (int j = 0; j < 10; j++)
                {
                    switch (i)
                    {
                        case 0:
                            jsonUtf8.WriteStringValue(value);
                            break;
                        case 1:
                            jsonUtf8.WriteStringValue(value.AsSpan());
                            break;
                        case 2:
                            jsonUtf8.WriteStringValue(Encoding.UTF8.GetBytes(value));
                            break;
                    }
                }

                jsonUtf8.WriteEndArray();
                jsonUtf8.Flush();

                JsonTestHelper.AssertContents(expectedStr, output);
            }
        }

        public static IEnumerable<object[]> WriteHelloWorldEscaped_AdditionalCases_TestData() =>
            JsonOptionsWith([
                "mess\nage",
                "message"
            ],
            [
                "Hello, \nWorld!",
                "Hello, World!"
            ]);

        [Theory]
        [MemberData(nameof(WriteHelloWorldEscaped_AdditionalCases_TestData))]
        public void WriteHelloWorldEscaped_AdditionalCases(JsonWriterOptions options, string key, string value)
        {
            string expectedStr = GetEscapedExpectedString(options, key, value, StringEscapeHandling.EscapeHtml);

            byte[] keyUtf8 = Encoding.UTF8.GetBytes(key);
            byte[] valueUtf8 = Encoding.UTF8.GetBytes(value);

            JsonEncodedText encodedKey = JsonEncodedText.Encode(key);
            JsonEncodedText encodedValue = JsonEncodedText.Encode(value);

            for (int i = 0; i < 32; i++)
            {
                var output = new ArrayBufferWriter<byte>(1024);
                using var jsonUtf8 = new Utf8JsonWriter(output, options);

                jsonUtf8.WriteStartObject();

                switch (i)
                {
                    case 0:
                        jsonUtf8.WriteString(key, value);
                        break;
                    case 1:
                        jsonUtf8.WriteString(key, value.AsSpan());
                        break;
                    case 2:
                        jsonUtf8.WriteString(key, valueUtf8);
                        break;
                    case 3:
                        jsonUtf8.WriteString(key.AsSpan(), value);
                        break;
                    case 4:
                        jsonUtf8.WriteString(key.AsSpan(), value.AsSpan());
                        break;
                    case 5:
                        jsonUtf8.WriteString(key.AsSpan(), valueUtf8);
                        break;
                    case 6:
                        jsonUtf8.WriteString(keyUtf8, value);
                        break;
                    case 7:
                        jsonUtf8.WriteString(keyUtf8, value.AsSpan());
                        break;
                    case 8:
                        jsonUtf8.WriteString(keyUtf8, valueUtf8);
                        break;
                    case 9:
                        jsonUtf8.WriteString(encodedKey, value);
                        break;
                    case 10:
                        jsonUtf8.WriteString(encodedKey, value.AsSpan());
                        break;
                    case 11:
                        jsonUtf8.WriteString(encodedKey, valueUtf8);
                        break;
                    case 12:
                        jsonUtf8.WriteString(encodedKey, encodedValue);
                        break;
                    case 13:
                        jsonUtf8.WriteString(key, encodedValue);
                        break;
                    case 14:
                        jsonUtf8.WriteString(key.AsSpan(), encodedValue);
                        break;
                    case 15:
                        jsonUtf8.WriteString(keyUtf8, encodedValue);
                        break;
                    case 16:
                        jsonUtf8.WritePropertyName(key);
                        jsonUtf8.WriteStringValue(value);
                        break;
                    case 17:
                        jsonUtf8.WritePropertyName(key);
                        jsonUtf8.WriteStringValue(value.AsSpan());
                        break;
                    case 18:
                        jsonUtf8.WritePropertyName(key);
                        jsonUtf8.WriteStringValue(valueUtf8);
                        break;
                    case 19:
                        jsonUtf8.WritePropertyName(key.AsSpan());
                        jsonUtf8.WriteStringValue(value);
                        break;
                    case 20:
                        jsonUtf8.WritePropertyName(key.AsSpan());
                        jsonUtf8.WriteStringValue(value.AsSpan());
                        break;
                    case 21:
                        jsonUtf8.WritePropertyName(key.AsSpan());
                        jsonUtf8.WriteStringValue(valueUtf8);
                        break;
                    case 22:
                        jsonUtf8.WritePropertyName(keyUtf8);
                        jsonUtf8.WriteStringValue(value);
                        break;
                    case 23:
                        jsonUtf8.WritePropertyName(keyUtf8);
                        jsonUtf8.WriteStringValue(value.AsSpan());
                        break;
                    case 24:
                        jsonUtf8.WritePropertyName(keyUtf8);
                        jsonUtf8.WriteStringValue(valueUtf8);
                        break;
                    case 25:
                        jsonUtf8.WritePropertyName(encodedKey);
                        jsonUtf8.WriteStringValue(value);
                        break;
                    case 26:
                        jsonUtf8.WritePropertyName(encodedKey);
                        jsonUtf8.WriteStringValue(value.AsSpan());
                        break;
                    case 27:
                        jsonUtf8.WritePropertyName(encodedKey);
                        jsonUtf8.WriteStringValue(valueUtf8);
                        break;
                    case 28:
                        jsonUtf8.WritePropertyName(encodedKey);
                        jsonUtf8.WriteStringValue(encodedValue);
                        break;
                    case 29:
                        jsonUtf8.WritePropertyName(key);
                        jsonUtf8.WriteStringValue(encodedValue);
                        break;
                    case 30:
                        jsonUtf8.WritePropertyName(key.AsSpan());
                        jsonUtf8.WriteStringValue(encodedValue);
                        break;
                    case 31:
                        jsonUtf8.WritePropertyName(keyUtf8);
                        jsonUtf8.WriteStringValue(encodedValue);
                        break;
                }

                jsonUtf8.WriteEndObject();
                jsonUtf8.Flush();

                JsonTestHelper.AssertContents(expectedStr, output);
            }
        }

        [Theory]
        [MemberData(nameof(JsonOptions_TestData))]
        public void EscapeAsciiCharacters(JsonWriterOptions options)
        {
            var propertyArray = new char[128];

            char[] specialCases = { '+', '`', (char)0x7F, '/' };
            for (int i = 0; i < propertyArray.Length; i++)
            {
                if (Array.IndexOf(specialCases, (char)i) != -1)
                {
                    propertyArray[i] = (char)0;
                }
                else
                {
                    propertyArray[i] = (char)i;
                }
            }

            string propertyName = new string(propertyArray);
            string value = new string(propertyArray);

            string expectedStr = GetEscapedExpectedString(options, propertyName, value, StringEscapeHandling.EscapeHtml);

            for (int i = 0; i < 6; i++)
            {
                var output = new ArrayBufferWriter<byte>(1024);
                using var jsonUtf8 = new Utf8JsonWriter(output, options);

                jsonUtf8.WriteStartObject();

                switch (i)
                {
                    case 0:
                        jsonUtf8.WriteString(propertyName, value);
                        break;
                    case 1:
                        jsonUtf8.WriteString(Encoding.UTF8.GetBytes(propertyName), Encoding.UTF8.GetBytes(value));
                        break;
                    case 2:
                        jsonUtf8.WriteString(JsonEncodedText.Encode(propertyName), JsonEncodedText.Encode(value));
                        break;
                    case 3:
                        jsonUtf8.WritePropertyName(propertyName);
                        jsonUtf8.WriteStringValue(value);
                        break;
                    case 4:
                        jsonUtf8.WritePropertyName(Encoding.UTF8.GetBytes(propertyName));
                        jsonUtf8.WriteStringValue(Encoding.UTF8.GetBytes(value));
                        break;
                    case 5:
                        jsonUtf8.WritePropertyName(JsonEncodedText.Encode(propertyName));
                        jsonUtf8.WriteStringValue(JsonEncodedText.Encode(value));
                        break;
                }

                jsonUtf8.WriteEndObject();
                jsonUtf8.Flush();

                JsonTestHelper.AssertContents(expectedStr, output);
            }
        }

        [Theory]
        [MemberData(nameof(JsonOptions_TestData))]
        [OuterLoop("Too slow", typeof(PlatformDetection), nameof(PlatformDetection.IsMonoRuntime))]
        public void EscapeCharacters(JsonWriterOptions options)
        {
            // Do not include surrogate pairs.
            var propertyArray = new char[0xD800 + (0xFFFF - 0xE000) + 1];

            for (int i = 128; i < propertyArray.Length; i++)
            {
                if (i < 0xD800 || i > 0xDFFF)
                {
                    propertyArray[i] = (char)i;
                }
            }

            string propertyName = new string(propertyArray);
            string value = new string(propertyArray);

            string expectedStr = GetEscapedExpectedString(options, propertyName, value, StringEscapeHandling.EscapeNonAscii);

            for (int i = 0; i < 6; i++)
            {
                var output = new ArrayBufferWriter<byte>(1024);
                using var jsonUtf8 = new Utf8JsonWriter(output, options);

                jsonUtf8.WriteStartObject();

                switch (i)
                {
                    case 0:
                        jsonUtf8.WriteString(propertyName, value);
                        break;
                    case 1:
                        jsonUtf8.WriteString(Encoding.UTF8.GetBytes(propertyName), Encoding.UTF8.GetBytes(value));
                        break;
                    case 2:
                        jsonUtf8.WriteString(JsonEncodedText.Encode(propertyName), JsonEncodedText.Encode(value));
                        break;
                    case 3:
                        jsonUtf8.WritePropertyName(propertyName);
                        jsonUtf8.WriteStringValue(value);
                        break;
                    case 4:
                        jsonUtf8.WritePropertyName(Encoding.UTF8.GetBytes(propertyName));
                        jsonUtf8.WriteStringValue(Encoding.UTF8.GetBytes(value));
                        break;
                    case 5:
                        jsonUtf8.WritePropertyName(JsonEncodedText.Encode(propertyName));
                        jsonUtf8.WriteStringValue(JsonEncodedText.Encode(value));
                        break;
                }

                jsonUtf8.WriteEndObject();
                jsonUtf8.Flush();

                JsonTestHelper.AssertContents(expectedStr, output);
            }
        }

        [Theory]
        [MemberData(nameof(JsonOptions_TestData))]
        public void HighSurrogateMissingGetsReplaced(JsonWriterOptions options)
        {
            var propertyArray = new char[10] { 'a', (char)0xD800, (char)0xDC00, (char)0xD803, (char)0xDE6D, (char)0xD834, (char)0xDD1E, (char)0xDBFF, (char)0xDFFF, 'a' };

            string propertyName = new string(propertyArray);
            string value = new string(propertyArray);

            string expectedStr = GetEscapedExpectedString(options, propertyName, value, StringEscapeHandling.EscapeNonAscii);

            for (int i = 0; i < 6; i++)
            {
                var output = new ArrayBufferWriter<byte>(1024);
                using var jsonUtf8 = new Utf8JsonWriter(output, options);

                jsonUtf8.WriteStartObject();

                switch (i)
                {
                    case 0:
                        jsonUtf8.WriteString(propertyName, value);
                        break;
                    case 1:
                        jsonUtf8.WriteString(Encoding.UTF8.GetBytes(propertyName), Encoding.UTF8.GetBytes(value));
                        break;
                    case 2:
                        jsonUtf8.WriteString(JsonEncodedText.Encode(propertyName), JsonEncodedText.Encode(value));
                        break;
                    case 3:
                        jsonUtf8.WritePropertyName(propertyName);
                        jsonUtf8.WriteStringValue(value);
                        break;
                    case 4:
                        jsonUtf8.WritePropertyName(Encoding.UTF8.GetBytes(propertyName));
                        jsonUtf8.WriteStringValue(Encoding.UTF8.GetBytes(value));
                        break;
                    case 5:
                        jsonUtf8.WritePropertyName(JsonEncodedText.Encode(propertyName));
                        jsonUtf8.WriteStringValue(JsonEncodedText.Encode(value));
                        break;
                }

                jsonUtf8.WriteEndObject();
                jsonUtf8.Flush();

                JsonTestHelper.AssertContents(expectedStr, output);
            }
        }

        [Fact]
        public void EscapeSurrogatePairs()
        {
            string propertyName = "a \uD800\uDC00\uDE6D a";
            string value = propertyName;
            var output = new ArrayBufferWriter<byte>(12);
            using (var jsonUtf8 = new Utf8JsonWriter(output))
            {
                jsonUtf8.WriteStartObject();
                jsonUtf8.WriteString(propertyName, value);
                jsonUtf8.WriteEndObject();
            }

            JsonTestHelper.AssertContents("{\"a \\uD800\\uDC00\\uFFFD a\":\"a \\uD800\\uDC00\\uFFFD a\"}", output);
        }

        private static readonly byte[] s_InvalidUtf8Input = new byte[2] { 0xc3, 0x28 };
        private const string InvalidUtf8Expected = "\"\\uFFFD(\"";

        private static readonly byte[] s_ValidUtf8Input = new byte[2] { 0xc3, 0xb1 }; // 0xF1
        private const string ValidUtf8Expected = "\"\\u00F1\"";

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Utf8SurrogatePairReplacement_InvalidPropertyName_InvalidValue(bool skipValidation)
        {
            // SkipValidation does not affect whether we write the replacement character or not (we always do, unless we add a new option to control).
            // Comment also applies to other Utf8SurrogatePairReplacement* tests below.
            var options = new JsonWriterOptions { SkipValidation = skipValidation };

            var output = new ArrayBufferWriter<byte>(1024);
            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteStartObject();
                jsonUtf8.WriteString(s_InvalidUtf8Input, s_InvalidUtf8Input);
                jsonUtf8.WriteEndObject();
            }

            JsonTestHelper.AssertContents("{" + InvalidUtf8Expected + ":" + InvalidUtf8Expected + "}", output);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Utf8SurrogatePairReplacement_InvalidPropertyName_ValidValue(bool skipValidation)
        {
            var options = new JsonWriterOptions { SkipValidation = skipValidation };

            var output = new ArrayBufferWriter<byte>(1024);
            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteStartObject();
                jsonUtf8.WriteString(s_InvalidUtf8Input, s_ValidUtf8Input);
                jsonUtf8.WriteEndObject();
            }

            JsonTestHelper.AssertContents("{" + InvalidUtf8Expected + ":" + ValidUtf8Expected + "}", output);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Utf8SurrogatePairReplacement_ValidPropertyName_InvalidValue(bool skipValidation)
        {
            var options = new JsonWriterOptions { SkipValidation = skipValidation };

            var output = new ArrayBufferWriter<byte>(1024);
            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteStartObject();
                jsonUtf8.WriteString(s_ValidUtf8Input, s_InvalidUtf8Input);
                jsonUtf8.WriteEndObject();
            }

            JsonTestHelper.AssertContents("{" + ValidUtf8Expected + ":" + InvalidUtf8Expected + "}", output);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Utf8SurrogatePairReplacement_ValidPropertyName_ValidValue(bool skipValidation)
        {
            var options = new JsonWriterOptions { SkipValidation = skipValidation };

            var output = new ArrayBufferWriter<byte>(1024);
            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteStartObject();
                jsonUtf8.WriteString(s_ValidUtf8Input, s_ValidUtf8Input);
                jsonUtf8.WriteEndObject();
            }

            JsonTestHelper.AssertContents("{" + ValidUtf8Expected + ":" + ValidUtf8Expected + "}", output);
        }

        private static readonly string s_InvalidUtf16Input = new string(new char[2] { (char)0xD801, 'a' });
        private const string InvalidUtf16Expected = "\"\\uFFFDa\"";

        private static readonly string s_ValidUtf16Input = new string(new char[2] { (char)0xD801, (char)0xDC37 }); // 0x10437
        private const string ValidUtf16Expected = "\"\\uD801\\uDC37\"";

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Utf16SurrogatePairReplacement_InvalidPropertyName_InvalidValue(bool skipValidation)
        {
            // SkipValidation does not affect whether we write the replacement character or not (we always do, unless we add a new option to control).
            // Comment also applies to other Utf16SurrogatePairReplacement* tests below.
            var options = new JsonWriterOptions { SkipValidation = skipValidation };

            var output = new ArrayBufferWriter<byte>(1024);
            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteStartObject();
                jsonUtf8.WriteString(s_InvalidUtf16Input, s_InvalidUtf16Input);
                jsonUtf8.WriteEndObject();
            }

            JsonTestHelper.AssertContents("{" + InvalidUtf16Expected + ":" + InvalidUtf16Expected + "}", output);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Utf16SurrogatePairReplacement_InvalidPropertyName_ValidValue(bool skipValidation)
        {
            var options = new JsonWriterOptions { SkipValidation = skipValidation };

            var output = new ArrayBufferWriter<byte>(1024);
            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteStartObject();
                jsonUtf8.WriteString(s_InvalidUtf16Input, s_ValidUtf16Input);
                jsonUtf8.WriteEndObject();
            }

            JsonTestHelper.AssertContents("{" + InvalidUtf16Expected + ":" + ValidUtf16Expected + "}", output);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Utf16SurrogatePairReplacement_ValidPropertyName_InvalidValue(bool skipValidation)
        {
            var options = new JsonWriterOptions { SkipValidation = skipValidation };

            var output = new ArrayBufferWriter<byte>(1024);
            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteStartObject();
                jsonUtf8.WriteString(s_ValidUtf16Input, s_InvalidUtf16Input);
                jsonUtf8.WriteEndObject();
            }

            JsonTestHelper.AssertContents("{" + ValidUtf16Expected + ":" + InvalidUtf16Expected + "}", output);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Utf16SurrogatePairReplacement_ValidPropertyName_ValidValue(bool skipValidation)
        {
            var options = new JsonWriterOptions { SkipValidation = skipValidation };

            var output = new ArrayBufferWriter<byte>(1024);
            using (var jsonUtf8 = new Utf8JsonWriter(output, options))
            {
                jsonUtf8.WriteStartObject();
                jsonUtf8.WriteString(s_ValidUtf16Input, s_ValidUtf16Input);
                jsonUtf8.WriteEndObject();
            }

            JsonTestHelper.AssertContents("{" + ValidUtf16Expected + ":" + ValidUtf16Expected + "}", output);
        }

        // Test case from https://github.com/dotnet/runtime/issues/30727
        [Fact]
        public void OutputConsistentWithJsonEncodedText()
        {
            string jsonEncodedText = $"{{\"{JsonEncodedText.Encode("propertyName+1")}\":\"{JsonEncodedText.Encode("value+1")}\"}}";

            var output = new ArrayBufferWriter<byte>(1024);

            using (var writer = new Utf8JsonWriter(output))
            {
                writer.WriteStartObject();
                writer.WriteString("propertyName+1", "value+1");
                writer.WriteEndObject();
            }

            JsonTestHelper.AssertContents(jsonEncodedText, output);
        }

        public static IEnumerable<object[]> JsonEncodedTextStringsCustomAll
        {
            get
            {
                return new List<object[]>
                {
                    new object[] { "\u00E9\u00E9\u00E9\u00E9\u00E9\u00EA\u00EA\u00EA\u00EA\u00EA", "{\"Prop\":\"\u00E9\u00E9\u00E9\u00E9\u00E9\u00EA\u00EA\u00EA\u00EA\u00EA\"}" },
                    new object[] { "a\u0467\u0466a", "{\"Prop\":\"a\u0467\u0466a\"}" },
                };
            }
        }

        [Theory]
        [MemberData(nameof(JsonEncodedTextStringsCustomAll))]
        public void CustomEscaper(string value, string expectedStr)
        {
            const string PropertyName = "Prop";

            // Allow all unicode values (except forbidden characters which we don't have in test data here)
            JavaScriptEncoder encoder = JavaScriptEncoder.Create(UnicodeRanges.All);

            var options = new JsonWriterOptions();
            options.Encoder = encoder;

            for (int i = 0; i < 6; i++)
            {
                var output = new ArrayBufferWriter<byte>(1024);
                using var jsonUtf8 = new Utf8JsonWriter(output, options);

                jsonUtf8.WriteStartObject();

                switch (i)
                {
                    case 0:
                        jsonUtf8.WriteString(PropertyName, value);
                        break;
                    case 1:
                        jsonUtf8.WriteString(Encoding.UTF8.GetBytes(PropertyName), Encoding.UTF8.GetBytes(value));
                        break;
                    case 2:
                        jsonUtf8.WriteString(JsonEncodedText.Encode(PropertyName), JsonEncodedText.Encode(value, encoder));
                        break;
                    case 3:
                        jsonUtf8.WritePropertyName(PropertyName);
                        jsonUtf8.WriteStringValue(value);
                        break;
                    case 4:
                        jsonUtf8.WritePropertyName(Encoding.UTF8.GetBytes(PropertyName));
                        jsonUtf8.WriteStringValue(Encoding.UTF8.GetBytes(value));
                        break;
                    case 5:
                        jsonUtf8.WritePropertyName(JsonEncodedText.Encode(PropertyName, encoder));
                        jsonUtf8.WriteStringValue(JsonEncodedText.Encode(value, encoder));
                        break;
                }

                jsonUtf8.WriteEndObject();
                jsonUtf8.Flush();

                string result = Encoding.UTF8.GetString(
                        output.WrittenSpan
#if NETFRAMEWORK
                        .ToArray()
#endif
                    );

                Assert.Equal(expectedStr, result);
            }
        }

        [Theory]
        [MemberData(nameof(JsonOptions_TestData))]
        public void WriteCustomStrings(JsonWriterOptions options)
        {
            var output = new ArrayBufferWriter<byte>(10);
            using var jsonUtf8 = new Utf8JsonWriter(output, options);

            jsonUtf8.WriteStartObject();

            for (int i = 0; i < 1_000; i++)
            {
                jsonUtf8.WriteString("message", "Hello, World!");
            }

            jsonUtf8.WriteEndObject();
            jsonUtf8.Flush();

            JsonTestHelper.AssertContents(GetCustomExpectedString(options), output);
        }

        [Theory]
        [MemberData(nameof(JsonOptions_TestData))]
        public void WriteStartEnd(JsonWriterOptions options)
        {
            string expectedStr = GetStartEndExpectedString(options);

            var output = new ArrayBufferWriter<byte>(1024);

            using var jsonUtf8 = new Utf8JsonWriter(output, options);

            jsonUtf8.WriteStartArray();
            jsonUtf8.WriteStartObject();
            jsonUtf8.WriteEndObject();
            jsonUtf8.WriteEndArray();
            jsonUtf8.Flush();

            JsonTestHelper.AssertContents(expectedStr, output);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void WriteStartEndInvalid(bool formatted)
        {
            {
                string expectedStr = "[}";

                var options = new JsonWriterOptions { Indented = formatted, SkipValidation = true };
                var output = new ArrayBufferWriter<byte>(1024);

                using var jsonUtf8 = new Utf8JsonWriter(output, options);

                jsonUtf8.WriteStartArray();
                jsonUtf8.WriteEndObject();
                jsonUtf8.Flush();

                JsonTestHelper.AssertContents(expectedStr, output);
            }

            {
                string expectedStr = "{]";

                var options = new JsonWriterOptions { Indented = formatted, SkipValidation = true };
                var output = new ArrayBufferWriter<byte>(1024);

                using var jsonUtf8 = new Utf8JsonWriter(output, options);

                jsonUtf8.WriteStartObject();
                jsonUtf8.WriteEndArray();
                jsonUtf8.Flush();

                JsonTestHelper.AssertContents(expectedStr, output);
            }
        }

        [Theory]
        [MemberData(nameof(JsonOptions_TestData))]
        public void WriteStartEndWithPropertyNameArray(JsonWriterOptions options)
        {
            string expectedStr = GetStartEndWithPropertyArrayExpectedString(options);

            
            for (int i = 0; i < 3; i++)
            {
                var output = new ArrayBufferWriter<byte>(1024);
                using var jsonUtf8 = new Utf8JsonWriter(output, options);

                jsonUtf8.WriteStartObject();

                switch (i)
                {
                    case 0:
                        jsonUtf8.WriteStartArray("property name");
                        break;
                    case 1:
                        jsonUtf8.WriteStartArray("property name".AsSpan());
                        break;
                    case 2:
                        jsonUtf8.WriteStartArray("property name"u8);
                        break;
                }

                jsonUtf8.WriteEndArray();
                jsonUtf8.WriteEndObject();
                jsonUtf8.Flush();

                JsonTestHelper.AssertContents(expectedStr, output);
            }
        }

        public static IEnumerable<object[]> WriteStartEndWithPropertyName_TestData() =>
            JsonOptionsWith([
                10,
                100
            ]);

        [Theory]
        [MemberData(nameof(WriteStartEndWithPropertyName_TestData))]
        public void WriteStartEndWithPropertyNameArrayDifferentKeyLengths(JsonWriterOptions options, int keyLength)
        {
            var keyChars = new char[keyLength];
            for (int i = 0; i < keyChars.Length; i++)
            {
                keyChars[i] = '<';
            }
            var key = new string(keyChars);

            string expectedStr = GetStartEndWithPropertyArrayExpectedString(key, options, escape: true);

            for (int i = 0; i < 3; i++)
            {
                var output = new ArrayBufferWriter<byte>(1024);
                using var jsonUtf8 = new Utf8JsonWriter(output, options);

                jsonUtf8.WriteStartObject();

                switch (i)
                {
                    case 0:
                        jsonUtf8.WriteStartArray(key);
                        break;
                    case 1:
                        jsonUtf8.WriteStartArray(key.AsSpan());
                        break;
                    case 2:
                        jsonUtf8.WriteStartArray(Encoding.UTF8.GetBytes(key));
                        break;
                }

                jsonUtf8.WriteEndArray();
                jsonUtf8.WriteEndObject();
                jsonUtf8.Flush();

                JsonTestHelper.AssertContents(expectedStr, output);
            }
        }

        [Theory]
        [MemberData(nameof(JsonOptions_TestData))]
        public void WriteStartEndWithPropertyNameObject(JsonWriterOptions options)
        {
            string expectedStr = GetStartEndWithPropertyObjectExpectedString(options);

            for (int i = 0; i < 3; i++)
            {
                var output = new ArrayBufferWriter<byte>(1024);
                using var jsonUtf8 = new Utf8JsonWriter(output, options);

                jsonUtf8.WriteStartObject();

                switch (i)
                {
                    case 0:
                        jsonUtf8.WriteStartObject("property name");
                        break;
                    case 1:
                        jsonUtf8.WriteStartObject("property name".AsSpan());
                        break;
                    case 2:
                        jsonUtf8.WriteStartObject("property name"u8);
                        break;
                }

                jsonUtf8.WriteEndObject();
                jsonUtf8.WriteEndObject();
                jsonUtf8.Flush();

                JsonTestHelper.AssertContents(expectedStr, output);
            }
        }

        [Theory]
        [MemberData(nameof(WriteStartEndWithPropertyName_TestData))]
        public void WriteStartEndWithPropertyNameObjectDifferentKeyLengths(JsonWriterOptions options, int keyLength)
        {
            var keyChars = new char[keyLength];
            for (int i = 0; i < keyChars.Length; i++)
            {
                keyChars[i] = '<';
            }
            var key = new string(keyChars);

            string expectedStr = GetStartEndWithPropertyObjectExpectedString(key, options, escape: true);

            for (int i = 0; i < 3; i++)
            {
                var output = new ArrayBufferWriter<byte>(1024);
                using var jsonUtf8 = new Utf8JsonWriter(output, options);

                jsonUtf8.WriteStartObject();

                switch (i)
                {
                    case 0:
                        jsonUtf8.WriteStartObject(key);
                        break;
                    case 1:
                        jsonUtf8.WriteStartObject(key.AsSpan());
                        break;
                    case 2:
                        jsonUtf8.WriteStartObject(Encoding.UTF8.GetBytes(key));
                        break;
                }

                jsonUtf8.WriteEndObject();
                jsonUtf8.WriteEndObject();
                jsonUtf8.Flush();

                JsonTestHelper.AssertContents(expectedStr, output);
            }
        }

        [Theory]
        [MemberData(nameof(JsonOptions_TestData))]
        public void WriteArrayWithProperty(JsonWriterOptions options)
        {
            string expectedStr = GetArrayWithPropertyExpectedString(options);

            for (int i = 0; i < 3; i++)
            {
                var output = new ArrayBufferWriter<byte>(1024);

                using var jsonUtf8 = new Utf8JsonWriter(output, options);
                jsonUtf8.WriteStartObject();

                switch (i)
                {
                    case 0:
                        jsonUtf8.WriteStartArray("message");
                        break;
                    case 1:
                        jsonUtf8.WriteStartArray("message".AsSpan());
                        break;
                    case 2:
                        jsonUtf8.WriteStartArray("message"u8);
                        break;
                }

                jsonUtf8.WriteEndArray();
                jsonUtf8.WriteEndObject();
                jsonUtf8.Flush();

                JsonTestHelper.AssertContents(expectedStr, output);
            }
        }

        public static IEnumerable<object[]> WriteBooleanValue_TestData() =>
            JsonOptionsWith([
                true,
                false
            ],
            [
                "message",
                "mess><age",
                ">>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>"
            ]);

        [Theory]
        [MemberData(nameof(WriteBooleanValue_TestData))]
        public void WriteBooleanValue(JsonWriterOptions options, bool value, string keyString)
        {
            string expectedStr = GetBooleanExpectedString(options, keyString, value, escape: true);

            for (int i = 0; i < 4; i++)
            {
                var output = new ArrayBufferWriter<byte>(1024);
                using var jsonUtf8 = new Utf8JsonWriter(output, options);

                jsonUtf8.WriteStartObject();

                switch (i)
                {
                    case 0:
                        jsonUtf8.WriteBoolean(keyString, value);
                        break;
                    case 1:
                        jsonUtf8.WriteBoolean(keyString.AsSpan(), value);
                        break;
                    case 2:
                        jsonUtf8.WriteBoolean(Encoding.UTF8.GetBytes(keyString), value);
                        break;
                    case 3:
                        jsonUtf8.WritePropertyName(keyString);
                        jsonUtf8.WriteBooleanValue(value);
                        break;
                }

                jsonUtf8.WriteStartArray("temp");
                jsonUtf8.WriteBooleanValue(true);
                jsonUtf8.WriteBooleanValue(true);
                jsonUtf8.WriteBooleanValue(false);
                jsonUtf8.WriteBooleanValue(false);
                jsonUtf8.WriteEndArray();

                jsonUtf8.WriteEndObject();
                jsonUtf8.Flush();

                JsonTestHelper.AssertContents(expectedStr, output);
            }
        }

        public static IEnumerable<object[]> WriteValue_TestData() =>
            JsonOptionsWith([
                "message",
                "mess><age",
                ">>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>"
            ]);

        [Theory]
        [MemberData(nameof(WriteValue_TestData))]
        public void WriteNullValue(JsonWriterOptions options, string keyString)
        {
            string expectedStr = GetNullExpectedString(options, keyString, escape: true);

            for (int i = 0; i < 4; i++)
            {
                var output = new ArrayBufferWriter<byte>(16);
                using var jsonUtf8 = new Utf8JsonWriter(output, options);

                jsonUtf8.WriteStartObject();

                switch (i)
                {
                    case 0:
                        jsonUtf8.WriteNull(keyString);
                        jsonUtf8.WriteNull(keyString);
                        break;
                    case 1:
                        jsonUtf8.WriteNull(keyString.AsSpan());
                        jsonUtf8.WriteNull(keyString.AsSpan());
                        break;
                    case 2:
                        jsonUtf8.WriteNull(Encoding.UTF8.GetBytes(keyString));
                        jsonUtf8.WriteNull(Encoding.UTF8.GetBytes(keyString));
                        break;
                    case 3:
                        jsonUtf8.WritePropertyName(keyString);
                        jsonUtf8.WriteNullValue();
                        jsonUtf8.WritePropertyName(keyString);
                        jsonUtf8.WriteNullValue();
                        break;
                }

                jsonUtf8.WriteStartArray("temp");
                jsonUtf8.WriteNullValue();
                jsonUtf8.WriteNullValue();
                jsonUtf8.WriteEndArray();

                jsonUtf8.WriteEndObject();
                jsonUtf8.Flush();

                JsonTestHelper.AssertContents(expectedStr, output);
            }
        }

        public static IEnumerable<object[]> WriteIntegerValue_TestData() =>
            JsonOptionsWith([
                0,
               -1,
                1,
                int.MaxValue,
                int.MinValue,
                12345
            ]);

        [Theory]
        [MemberData(nameof(WriteIntegerValue_TestData))]
        public void WriteIntegerValue(JsonWriterOptions options, int value)
        {
            string expectedStr = GetPropertyExpectedString(options, value);

            for (int i = 0; i < 4; i++)
            {
                var output = new ArrayBufferWriter<byte>(1024);
                using var jsonUtf8 = new Utf8JsonWriter(output, options);

                jsonUtf8.WriteStartObject();

                switch (i)
                {
                    case 0:
                        jsonUtf8.WriteNumber("message", value);
                        break;
                    case 1:
                        jsonUtf8.WriteNumber("message".AsSpan(), value);
                        break;
                    case 2:
                        jsonUtf8.WriteNumber("message"u8, value);
                        break;
                    case 3:
                        jsonUtf8.WritePropertyName("message");
                        jsonUtf8.WriteNumberValue(value);
                        break;
                }

                jsonUtf8.WriteEndObject();
                jsonUtf8.Flush();

                JsonTestHelper.AssertContents(expectedStr, output);
            }
        }

        public static IEnumerable<object[]> WriteFloatValue_TestData() =>
            JsonOptionsWith([
                float.MinValue,
                float.MaxValue
            ]);

        [Theory]
        [MemberData(nameof(WriteFloatValue_TestData))]
        public void WriteFloatValue(JsonWriterOptions options, float value)
        {
            string expectedStr = GetPropertyExpectedString(options, value);

            for (int i = 0; i < 4; i++)
            {
                var output = new ArrayBufferWriter<byte>(1024);
                using var jsonUtf8 = new Utf8JsonWriter(output, options);

                jsonUtf8.WriteStartObject();

                switch (i)
                {
                    case 0:
                        jsonUtf8.WriteNumber("message", value);
                        break;
                    case 1:
                        jsonUtf8.WriteNumber("message".AsSpan(), value);
                        break;
                    case 2:
                        jsonUtf8.WriteNumber("message"u8, value);
                        break;
                    case 3:
                        jsonUtf8.WritePropertyName("message");
                        jsonUtf8.WriteNumberValue(value);
                        break;
                }

                jsonUtf8.WriteEndObject();
                jsonUtf8.Flush();

                JsonTestHelper.AssertContents(expectedStr, output);
            }
        }

        public static IEnumerable<object[]> WriteDoubleValue_TestData() =>
            JsonOptionsWith([
                double.MinValue,
                double.MaxValue
            ]);

        [Theory]
        [MemberData(nameof(WriteDoubleValue_TestData))]
        public void WriteDoubleValue(JsonWriterOptions options, double value)
        {
            string expectedStr = GetPropertyExpectedString(options, value);

            for (int i = 0; i < 4; i++)
            {
                var output = new ArrayBufferWriter<byte>(1024);
                using var jsonUtf8 = new Utf8JsonWriter(output, options);

                jsonUtf8.WriteStartObject();

                switch (i)
                {
                    case 0:
                        jsonUtf8.WriteNumber("message", value);
                        break;
                    case 1:
                        jsonUtf8.WriteNumber("message".AsSpan(), value);
                        break;
                    case 2:
                        jsonUtf8.WriteNumber("message"u8, value);
                        break;
                    case 3:
                        jsonUtf8.WritePropertyName("message");
                        jsonUtf8.WriteNumberValue(value);
                        break;
                }

                jsonUtf8.WriteEndObject();
                jsonUtf8.Flush();

                JsonTestHelper.AssertContents(expectedStr, output);
            }
        }

        [Theory]
        [SkipOnTargetFramework(TargetFrameworkMonikers.NetFramework)]
        [MemberData(nameof(WriteValue_TestData))]
        [OuterLoop("Too slow", typeof(PlatformDetection), nameof(PlatformDetection.IsMonoRuntime))]
        public void WriteNumbers(JsonWriterOptions options, string keyString)
        {
            var random = new Random(42);
            const int numberOfItems = 1_000;

            var ints = new int[numberOfItems];
            ints[0] = 0;
            ints[1] = int.MaxValue;
            ints[2] = int.MinValue;
            ints[3] = 12345;
            ints[4] = -12345;
            for (int i = 5; i < numberOfItems; i++)
            {
                ints[i] = random.Next(int.MinValue, int.MaxValue);
            }

            var uints = new uint[numberOfItems];
            uints[0] = uint.MaxValue;
            uints[1] = uint.MinValue;
            uints[2] = 3294967295;
            for (int i = 3; i < numberOfItems; i++)
            {
                uint thirtyBits = (uint)random.Next(1 << 30);
                uint twoBits = (uint)random.Next(1 << 2);
                uint fullRange = (thirtyBits << 2) | twoBits;
                uints[i] = fullRange;
            }

            var longs = new long[numberOfItems];
            longs[0] = 0;
            longs[1] = long.MaxValue;
            longs[2] = long.MinValue;
            longs[3] = 12345678901;
            longs[4] = -12345678901;
            for (int i = 5; i < numberOfItems; i++)
            {
                long value = random.Next(int.MinValue, int.MaxValue);
                value += value < 0 ? int.MinValue : int.MaxValue;
                longs[i] = value;
            }

            var ulongs = new ulong[numberOfItems];
            ulongs[0] = ulong.MaxValue;
            ulongs[1] = ulong.MinValue;
            ulongs[2] = 10446744073709551615;
            for (int i = 3; i < numberOfItems; i++)
            {

            }

            var doubles = new double[numberOfItems * 2];
            doubles[0] = 0.00;
            doubles[1] = double.MaxValue;
            doubles[2] = double.MinValue;
            doubles[3] = 12.345e1;
            doubles[4] = -123.45e1;

            for (int i = 5; i < numberOfItems; i++)
            {
                var value = random.NextDouble();
                if (value < 0.5)
                {
                    doubles[i] = random.NextDouble() * double.MinValue;
                }
                else
                {
                    doubles[i] = random.NextDouble() * double.MaxValue;
                }
            }

            for (int i = numberOfItems; i < numberOfItems * 2; i++)
            {
                var value = random.NextDouble();
                if (value < 0.5)
                {
                    doubles[i] = random.NextDouble() * -1_000_000;
                }
                else
                {
                    doubles[i] = random.NextDouble() * 1_000_000;
                }
            }

            var floats = new float[numberOfItems];
            floats[0] = 0.00f;
            floats[1] = float.MaxValue;
            floats[2] = float.MinValue;
            floats[3] = 12.345e1f;
            floats[4] = -123.45e1f;
            for (int i = 5; i < numberOfItems; i++)
            {
                double mantissa = (random.NextDouble() * 2.0) - 1.0;
                double exponent = Math.Pow(2.0, random.Next(-126, 128));
                floats[i] = (float)(mantissa * exponent);
            }

            var decimals = new decimal[numberOfItems * 2];
            decimals[0] = (decimal)0.00;
            decimals[1] = decimal.MaxValue;
            decimals[2] = decimal.MinValue;
            decimals[3] = (decimal)12.345e1;
            decimals[4] = (decimal)-123.45e1;
            for (int i = 5; i < numberOfItems; i++)
            {
                var value = random.NextDouble();
                if (value < 0.5)
                {
                    decimals[i] = (decimal)(random.NextDouble() * -78E14);
                }
                else
                {
                    decimals[i] = (decimal)(random.NextDouble() * 78E14);
                }
            }

            for (int i = numberOfItems; i < numberOfItems * 2; i++)
            {
                var value = random.NextDouble();
                if (value < 0.5)
                {
                    decimals[i] = (decimal)(random.NextDouble() * -1_000_000);
                }
                else
                {
                    decimals[i] = (decimal)(random.NextDouble() * 1_000_000);
                }
            }

            string expectedStr = GetNumbersExpectedString(options, keyString, ints, uints, longs, ulongs, floats, doubles, decimals, escape: false);

            for (int j = 0; j < 3; j++)
            {
                var output = new ArrayBufferWriter<byte>(1024);
                using var jsonUtf8 = new Utf8JsonWriter(output, options);

                ReadOnlySpan<char> keyUtf16 = keyString.AsSpan();
                ReadOnlySpan<byte> keyUtf8 = Encoding.UTF8.GetBytes(keyString);

                jsonUtf8.WriteStartObject();

                switch (j)
                {
                    case 0:
                        for (int i = 0; i < floats.Length; i++)
                            jsonUtf8.WriteNumber(keyString, floats[i]);
                        for (int i = 0; i < ints.Length; i++)
                            jsonUtf8.WriteNumber(keyString, ints[i]);
                        for (int i = 0; i < uints.Length; i++)
                            jsonUtf8.WriteNumber(keyString, uints[i]);
                        for (int i = 0; i < doubles.Length; i++)
                            jsonUtf8.WriteNumber(keyString, doubles[i]);
                        for (int i = 0; i < longs.Length; i++)
                            jsonUtf8.WriteNumber(keyString, longs[i]);
                        for (int i = 0; i < ulongs.Length; i++)
                            jsonUtf8.WriteNumber(keyString, ulongs[i]);
                        for (int i = 0; i < decimals.Length; i++)
                            jsonUtf8.WriteNumber(keyString, decimals[i]);
                        jsonUtf8.WriteStartArray(keyString);
                        break;
                    case 1:
                        for (int i = 0; i < floats.Length; i++)
                            jsonUtf8.WriteNumber(keyUtf16, floats[i]);
                        for (int i = 0; i < ints.Length; i++)
                            jsonUtf8.WriteNumber(keyUtf16, ints[i]);
                        for (int i = 0; i < uints.Length; i++)
                            jsonUtf8.WriteNumber(keyUtf16, uints[i]);
                        for (int i = 0; i < doubles.Length; i++)
                            jsonUtf8.WriteNumber(keyUtf16, doubles[i]);
                        for (int i = 0; i < longs.Length; i++)
                            jsonUtf8.WriteNumber(keyUtf16, longs[i]);
                        for (int i = 0; i < ulongs.Length; i++)
                            jsonUtf8.WriteNumber(keyUtf16, ulongs[i]);
                        for (int i = 0; i < decimals.Length; i++)
                            jsonUtf8.WriteNumber(keyUtf16, decimals[i]);
                        jsonUtf8.WriteStartArray(keyUtf16);
                        break;
                    case 2:
                        for (int i = 0; i < floats.Length; i++)
                            jsonUtf8.WriteNumber(keyUtf8, floats[i]);
                        for (int i = 0; i < ints.Length; i++)
                            jsonUtf8.WriteNumber(keyUtf8, ints[i]);
                        for (int i = 0; i < uints.Length; i++)
                            jsonUtf8.WriteNumber(keyUtf8, uints[i]);
                        for (int i = 0; i < doubles.Length; i++)
                            jsonUtf8.WriteNumber(keyUtf8, doubles[i]);
                        for (int i = 0; i < longs.Length; i++)
                            jsonUtf8.WriteNumber(keyUtf8, longs[i]);
                        for (int i = 0; i < ulongs.Length; i++)
                            jsonUtf8.WriteNumber(keyUtf8, ulongs[i]);
                        for (int i = 0; i < decimals.Length; i++)
                            jsonUtf8.WriteNumber(keyUtf8, decimals[i]);
                        jsonUtf8.WriteStartArray(keyUtf8);
                        break;
                }

                jsonUtf8.WriteNumberValue(floats[0]);
                jsonUtf8.WriteNumberValue(ints[0]);
                jsonUtf8.WriteNumberValue(uints[0]);
                jsonUtf8.WriteNumberValue(doubles[0]);
                jsonUtf8.WriteNumberValue(longs[0]);
                jsonUtf8.WriteNumberValue(ulongs[0]);
                jsonUtf8.WriteNumberValue(decimals[0]);
                jsonUtf8.WriteEndArray();

                jsonUtf8.WriteEndObject();
                jsonUtf8.Flush();

                JsonTestHelper.AssertContents(expectedStr, output);
            }
        }

        [Theory]
        [MemberData(nameof(JsonOptions_TestData))]
        public void WriteNumberValueInt32(JsonWriterOptions options)
        {
            var output = new ArrayBufferWriter<byte>();
            using var jsonUtf8 = new Utf8JsonWriter(output, options);

            int numberOfElements = 0;
            jsonUtf8.WriteStartArray();
            int currentCapactiy = output.Capacity;
            int value = 1234567;
            while (currentCapactiy == output.Capacity)
            {
                jsonUtf8.WriteNumberValue(value);
                numberOfElements++;
            }
            Assert.Equal(currentCapactiy + 4096, output.Capacity);
            jsonUtf8.WriteEndArray();
            jsonUtf8.Flush();

            string expectedStr = GetNumbersExpectedString(options, numberOfElements, value);
            JsonTestHelper.AssertContents(expectedStr, output);
        }

        [Theory]
        [MemberData(nameof(JsonOptions_TestData))]
        public void WriteNumberValueInt64(JsonWriterOptions options)
        {
            var output = new ArrayBufferWriter<byte>();
            using var jsonUtf8 = new Utf8JsonWriter(output, options);

            int numberOfElements = 0;
            jsonUtf8.WriteStartArray();
            int currentCapactiy = output.Capacity;
            long value = 1234567;
            while (currentCapactiy == output.Capacity)
            {
                jsonUtf8.WriteNumberValue(value);
                numberOfElements++;
            }
            Assert.Equal(currentCapactiy + 4096, output.Capacity);
            jsonUtf8.WriteEndArray();
            jsonUtf8.Flush();

            string expectedStr = GetNumbersExpectedString(options, numberOfElements, value);
            JsonTestHelper.AssertContents(expectedStr, output);
        }

        [Theory]
        [MemberData(nameof(JsonOptions_TestData))]
        public void WriteNumberValueUInt32(JsonWriterOptions options)
        {
            var output = new ArrayBufferWriter<byte>();
            using var jsonUtf8 = new Utf8JsonWriter(output, options);

            int numberOfElements = 0;
            jsonUtf8.WriteStartArray();
            int currentCapactiy = output.Capacity;
            uint value = 1234567;
            while (currentCapactiy == output.Capacity)
            {
                jsonUtf8.WriteNumberValue(value);
                numberOfElements++;
            }
            Assert.Equal(currentCapactiy + 4096, output.Capacity);
            jsonUtf8.WriteEndArray();
            jsonUtf8.Flush();

            string expectedStr = GetNumbersExpectedString(options, numberOfElements, value);
            JsonTestHelper.AssertContents(expectedStr, output);
        }

        [Theory]
        [MemberData(nameof(JsonOptions_TestData))]
        public void WriteNumberValueUInt64(JsonWriterOptions options)
        {
            var output = new ArrayBufferWriter<byte>();
            using var jsonUtf8 = new Utf8JsonWriter(output, options);

            int numberOfElements = 0;
            jsonUtf8.WriteStartArray();
            int currentCapactiy = output.Capacity;
            ulong value = 1234567;
            while (currentCapactiy == output.Capacity)
            {
                jsonUtf8.WriteNumberValue(value);
                numberOfElements++;
            }
            Assert.Equal(currentCapactiy + 4096, output.Capacity);
            jsonUtf8.WriteEndArray();
            jsonUtf8.Flush();

            string expectedStr = GetNumbersExpectedString(options, numberOfElements, value);
            JsonTestHelper.AssertContents(expectedStr, output);
        }

        [Theory]
        [MemberData(nameof(WriteFloatValue_TestData))]
        public void WriteNumberValueSingle(JsonWriterOptions options, float value)
        {
            var output = new ArrayBufferWriter<byte>();
            using var jsonUtf8 = new Utf8JsonWriter(output, options);

            int numberOfElements = 0;
            jsonUtf8.WriteStartArray();
            int currentCapactiy = output.Capacity;
            while (currentCapactiy == output.Capacity)
            {
                jsonUtf8.WriteNumberValue(value);
                numberOfElements++;
            }
            Assert.Equal(currentCapactiy + 4096, output.Capacity);
            jsonUtf8.WriteEndArray();
            jsonUtf8.Flush();

            string expectedStr = GetNumbersExpectedString(options, numberOfElements, value);
            JsonTestHelper.AssertContents(expectedStr, output);
        }

        [Theory]
        [MemberData(nameof(WriteDoubleValue_TestData))]
        public void WriteNumberValueDouble(JsonWriterOptions options, double value)
        {
            var output = new ArrayBufferWriter<byte>();
            using var jsonUtf8 = new Utf8JsonWriter(output, options);

            int numberOfElements = 0;
            jsonUtf8.WriteStartArray();
            int currentCapactiy = output.Capacity;
            while (currentCapactiy == output.Capacity)
            {
                jsonUtf8.WriteNumberValue(value);
                numberOfElements++;
            }
            Assert.Equal(currentCapactiy + 4096, output.Capacity);
            jsonUtf8.WriteEndArray();
            jsonUtf8.Flush();

            string expectedStr = GetNumbersExpectedString(options, numberOfElements, value);
            JsonTestHelper.AssertContents(expectedStr, output);
        }

        [Theory]
        [MemberData(nameof(JsonOptions_TestData))]
        public void WriteNumberValueDecimal(JsonWriterOptions options)
        {
            var output = new ArrayBufferWriter<byte>();
            using var jsonUtf8 = new Utf8JsonWriter(output, options);

            int numberOfElements = 0;
            jsonUtf8.WriteStartArray();
            int currentCapactiy = output.Capacity;
            decimal value = 1234567.0M;
            while (currentCapactiy == output.Capacity)
            {
                jsonUtf8.WriteNumberValue(value);
                numberOfElements++;
            }
            Assert.Equal(currentCapactiy + 4096, output.Capacity);
            jsonUtf8.WriteEndArray();
            jsonUtf8.Flush();

            string expectedStr = GetNumbersExpectedString(options, numberOfElements, value);
            JsonTestHelper.AssertContents(expectedStr, output);
        }

        [Theory]
        [InlineData(true, true, "message", true)]
        [InlineData(true, false, "message", true)]
        [InlineData(false, true, "message", true)]
        [InlineData(false, false, "message", true)]
        [InlineData(true, true, "mess><age", false)]
        [InlineData(true, false, "mess><age", false)]
        [InlineData(false, true, "mess><age", false)]
        [InlineData(false, false, "mess><age", false)]
        [InlineData(true, true, ">><++>>>\">>\\>>&>>>\u6f22\u5B57>>>", false)]
        [InlineData(true, false, ">><++>>>\">>\\>>&>>>\u6f22\u5B57>>>", false)]
        [InlineData(false, true, ">><++>>>\">>\\>>&>>>\u6f22\u5B57>>>", false)]
        [InlineData(false, false, ">><++>>>\">>\\>>&>>>\u6f22\u5B57>>>", false)]
        [InlineData(true, true, "mess\r\nage\u0008\u0001!", true)]
        [InlineData(true, false, "mess\r\nage\u0008\u0001!", true)]
        [InlineData(false, true, "mess\r\nage\u0008\u0001!", true)]
        [InlineData(false, false, "mess\r\nage\u0008\u0001!", true)]
        public void WriteStringsWithRelaxedEscaping(bool formatted, bool skipValidation, string keyString, bool matchesRelaxedEscaping)
        {
            string expectedStr = GetExpectedString_RelaxedEscaping(prettyPrint: formatted, keyString);

            var options = new JsonWriterOptions { Indented = formatted, SkipValidation = skipValidation, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
            WriteStringHelper(options, keyString, expectedStr, shouldMatch: true);

            // Default encoder
            options = new JsonWriterOptions { Indented = formatted, SkipValidation = skipValidation };
            WriteStringHelper(options, keyString, expectedStr, matchesRelaxedEscaping);
        }

        private static void WriteStringHelper(JsonWriterOptions options, string keyString, string expectedStr, bool shouldMatch)
        {
            ReadOnlySpan<char> keyUtf16 = keyString.AsSpan();
            ReadOnlySpan<byte> keyUtf8 = Encoding.UTF8.GetBytes(keyString);

            for (int i = 0; i < 3; i++)
            {
                var output = new ArrayBufferWriter<byte>(1024);
                using var jsonUtf8 = new Utf8JsonWriter(output, options);

                jsonUtf8.WriteStartObject();

                switch (i)
                {
                    case 0:
                        jsonUtf8.WriteString(keyString, keyString);
                        jsonUtf8.WriteStartArray(keyString);
                        break;
                    case 1:
                        jsonUtf8.WriteString(keyUtf16, keyString);
                        jsonUtf8.WriteStartArray(keyUtf16);
                        break;
                    case 2:
                        jsonUtf8.WriteString(keyUtf8, keyString);
                        jsonUtf8.WriteStartArray(keyUtf8);
                        break;
                }

                jsonUtf8.WriteStringValue(keyString);
                jsonUtf8.WriteStringValue(keyString);
                jsonUtf8.WriteEndArray();

                jsonUtf8.WriteEndObject();
                jsonUtf8.Flush();

                if (shouldMatch)
                {
                    JsonTestHelper.AssertContents(expectedStr, output);
                }
                else
                {
                    JsonTestHelper.AssertContentsNotEqual(expectedStr, output, skipSpecialRules: true);
                }
            }
        }

        [Theory]
        [MemberData(nameof(WriteValue_TestData))]
        public void WriteGuidsValue(JsonWriterOptions options, string keyString)
        {
            const int numberOfItems = 1_000;

            var guids = new Guid[numberOfItems];
            for (int i = 0; i < numberOfItems; i++)
            {
                guids[i] = Guid.NewGuid();
            }

            string expectedStr = GetGuidsExpectedString(options, keyString, guids, escape: true);

            ReadOnlySpan<char> keyUtf16 = keyString.AsSpan();
            ReadOnlySpan<byte> keyUtf8 = Encoding.UTF8.GetBytes(keyString);

            for (int i = 0; i < 3; i++)
            {
                var output = new ArrayBufferWriter<byte>(1024);
                using var jsonUtf8 = new Utf8JsonWriter(output, options);

                jsonUtf8.WriteStartObject();

                switch (i)
                {
                    case 0:
                        for (int j = 0; j < numberOfItems; j++)
                            jsonUtf8.WriteString(keyString, guids[j]);
                        jsonUtf8.WriteStartArray(keyString);
                        break;
                    case 1:
                        for (int j = 0; j < numberOfItems; j++)
                            jsonUtf8.WriteString(keyUtf16, guids[j]);
                        jsonUtf8.WriteStartArray(keyUtf16);
                        break;
                    case 2:
                        for (int j = 0; j < numberOfItems; j++)
                            jsonUtf8.WriteString(keyUtf8, guids[j]);
                        jsonUtf8.WriteStartArray(keyUtf8);
                        break;
                }

                jsonUtf8.WriteStringValue(guids[0]);
                jsonUtf8.WriteStringValue(guids[1]);
                jsonUtf8.WriteEndArray();

                jsonUtf8.WriteEndObject();
                jsonUtf8.Flush();

                JsonTestHelper.AssertContents(expectedStr, output);
            }
        }

        [Theory]
        [MemberData(nameof(WriteValue_TestData))]
        public void WriteDateTimesValue(JsonWriterOptions options, string keyString)
        {
            var random = new Random(42);
            const int numberOfItems = 1_000;

            var start = new DateTime(1995, 1, 1);
            int range = (DateTime.Today - start).Days;

            var dates = new DateTime[numberOfItems];
            for (int i = 0; i < numberOfItems; i++)
                dates[i] = start.AddDays(random.Next(range));

            string expectedStr = GetDatesExpectedString(options, keyString, dates, escape: true);

            ReadOnlySpan<char> keyUtf16 = keyString.AsSpan();
            ReadOnlySpan<byte> keyUtf8 = Encoding.UTF8.GetBytes(keyString);

            for (int i = 0; i < 3; i++)
            {
                var output = new ArrayBufferWriter<byte>(1024);
                using var jsonUtf8 = new Utf8JsonWriter(output, options);

                jsonUtf8.WriteStartObject();

                switch (i)
                {
                    case 0:
                        for (int j = 0; j < numberOfItems; j++)
                            jsonUtf8.WriteString(keyString, dates[j]);
                        jsonUtf8.WriteStartArray(keyString);
                        break;
                    case 1:
                        for (int j = 0; j < numberOfItems; j++)
                            jsonUtf8.WriteString(keyUtf16, dates[j]);
                        jsonUtf8.WriteStartArray(keyUtf16);
                        break;
                    case 2:
                        for (int j = 0; j < numberOfItems; j++)
                            jsonUtf8.WriteString(keyUtf8, dates[j]);
                        jsonUtf8.WriteStartArray(keyUtf8);
                        break;
                }

                jsonUtf8.WriteStringValue(dates[0]);
                jsonUtf8.WriteStringValue(dates[1]);
                jsonUtf8.WriteEndArray();

                jsonUtf8.WriteEndObject();
                jsonUtf8.Flush();

                JsonTestHelper.AssertContents(expectedStr, output);
            }
        }

        [Theory]
        [MemberData(nameof(WriteValue_TestData))]
        public void WriteDateTimeOffsetsValue(JsonWriterOptions options, string keyString)
        {
            var random = new Random(42);
            const int numberOfItems = 1_000;

            var start = new DateTime(1995, 1, 1);
            int range = (DateTime.Today - start).Days;

            var dates = new DateTimeOffset[numberOfItems];
            for (int i = 0; i < numberOfItems; i++)
                dates[i] = new DateTimeOffset(start.AddDays(random.Next(range)));

            string expectedStr = GetDatesExpectedString(options, keyString, dates, escape: true);

            ReadOnlySpan<char> keyUtf16 = keyString.AsSpan();
            ReadOnlySpan<byte> keyUtf8 = Encoding.UTF8.GetBytes(keyString);

            for (int i = 0; i < 3; i++)
            {
                var output = new ArrayBufferWriter<byte>(1024);
                using var jsonUtf8 = new Utf8JsonWriter(output, options);

                jsonUtf8.WriteStartObject();

                switch (i)
                {
                    case 0:
                        for (int j = 0; j < numberOfItems; j++)
                            jsonUtf8.WriteString(keyString, dates[j]);
                        jsonUtf8.WriteStartArray(keyString);
                        break;
                    case 1:
                        for (int j = 0; j < numberOfItems; j++)
                            jsonUtf8.WriteString(keyUtf16, dates[j]);
                        jsonUtf8.WriteStartArray(keyUtf16);
                        break;
                    case 2:
                        for (int j = 0; j < numberOfItems; j++)
                            jsonUtf8.WriteString(keyUtf8, dates[j]);
                        jsonUtf8.WriteStartArray(keyUtf8);
                        break;
                }

                jsonUtf8.WriteStringValue(dates[0]);
                jsonUtf8.WriteStringValue(dates[1]);
                jsonUtf8.WriteEndArray();

                jsonUtf8.WriteEndObject();
                jsonUtf8.Flush();

                JsonTestHelper.AssertContents(expectedStr, output);
            }
        }

        // NOTE: WriteLargeKeyOrValue test is constrained to run on Windows and MacOSX because it causes
        //       problems on Linux due to the way deferred memory allocation works. On Linux, the allocation can
        //       succeed even if there is not enough memory but then the test may get killed by the OOM killer at the
        //       time the memory is accessed which triggers the full memory allocation.
        [PlatformSpecific(TestPlatforms.Windows | TestPlatforms.OSX)]
        [ConditionalTheory(typeof(Environment), nameof(Environment.Is64BitProcess))]
        [OuterLoop]
        [MemberData(nameof(JsonOptions_TestData))]
        public void WriteLargeKeyOrValue(JsonWriterOptions options)
        {
            try
            {
                byte[] key;
                byte[] value;

                key = new byte[MaxUnescapedTokenSize + 1];
                value = new byte[MaxUnescapedTokenSize + 1];

                key.AsSpan().Fill((byte)'a');
                value.AsSpan().Fill((byte)'b');

                {
                    var output = new ArrayBufferWriter<byte>(1024);
                    using var jsonUtf8 = new Utf8JsonWriter(output, options);
                    jsonUtf8.WriteStartObject();
                    Assert.Throws<ArgumentException>(() => jsonUtf8.WriteString(key, DateTimeTestHelpers.FixedDateTimeValue));
                    Assert.Equal(0, output.WrittenCount);
                }

                {
                    var output = new ArrayBufferWriter<byte>(1024);
                    using var jsonUtf8 = new Utf8JsonWriter(output, options);
                    jsonUtf8.WriteStartArray();
                    Assert.Throws<ArgumentException>(() => jsonUtf8.WriteStringValue(value));
                    Assert.Equal(0, output.WrittenCount);
                }
            }
            catch (OutOfMemoryException)
            {
                throw new SkipTestException("Out of memory allocating large objects");
            }
        }

        // NOTE: WriteLargeKeyValue test is constrained to run on Windows and MacOSX because it causes
        //       problems on Linux due to the way deferred memory allocation works. On Linux, the allocation can
        //       succeed even if there is not enough memory but then the test may get killed by the OOM killer at the
        //       time the memory is accessed which triggers the full memory allocation.
        [PlatformSpecific(TestPlatforms.Windows | TestPlatforms.OSX)]
        [ConditionalTheory(typeof(Environment), nameof(Environment.Is64BitProcess))]
        [OuterLoop]
        [MemberData(nameof(JsonOptions_TestData))]
        public void WriteLargeKeyValue(JsonWriterOptions options)
        {
            try
            {
                
                Span<byte> key;
                Span<byte> value;

                key = new byte[MaxUnescapedTokenSize + 1];
                value = new byte[MaxUnescapedTokenSize + 1];

                WriteTooLargeHelper(options, key, value);
                WriteTooLargeHelper(options, key.Slice(0, MaxUnescapedTokenSize), value);
                WriteTooLargeHelper(options, key, value.Slice(0, MaxUnescapedTokenSize));
                WriteTooLargeHelper(options, key.Slice(0, 10_000_000 / 3), value.Slice(0, 10_000_000 / 3), noThrow: true);
            }
            catch (OutOfMemoryException)
            {
                throw new SkipTestException("Out of memory allocating large objects");
            }
        }

        // NOTE: WriteLargeKeyEscapedValue test is constrained to run on Windows and MacOSX because it causes
        //       problems on Linux due to the way deferred memory allocation works. On Linux, the allocation can
        //       succeed even if there is not enough memory but then the test may get killed by the OOM killer at the
        //       time the memory is accessed which triggers the full memory allocation.
        [PlatformSpecific(TestPlatforms.Windows | TestPlatforms.OSX)]
        [ConditionalTheory(typeof(Environment), nameof(Environment.Is64BitProcess))]
        [OuterLoop]
        [MemberData(nameof(JsonOptions_TestData))]
        public void WriteLargeKeyEscapedValue(JsonWriterOptions options)
        {
            try
            {
                
                Span<byte> key;
                Span<byte> value;

                // Since the byte values are 0 they will be escaped and size > MaxUnescapedTokenSize but < MaxEscapedTokenSize.
                key = new byte[MaxUnescapedTokenSize / 2];
                value = new byte[MaxUnescapedTokenSize / 2];

                WriteTooLargeHelper(options, key, value, noThrow: true);
            }
            catch (OutOfMemoryException)
            {
                throw new SkipTestException("Out of memory allocating large objects");
            }
        }

        [Theory]
        [MemberData(nameof(JsonDateTimeTestData.DateTimeFractionTrimBaseTests), MemberType = typeof(JsonDateTimeTestData))]
        [MemberData(nameof(JsonDateTimeTestData.DateTimeFractionTrimUtcOffsetTests), MemberType = typeof(JsonDateTimeTestData))]
        public void WriteDateTime_TrimsFractionCorrectly(string testStr, string expectedStr)
        {
            var output = new ArrayBufferWriter<byte>(1024);
            using var jsonUtf8 = new Utf8JsonWriter(output);

            jsonUtf8.WriteStringValue(DateTime.ParseExact(testStr, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
            jsonUtf8.Flush();

            JsonTestHelper.AssertContents($"\"{expectedStr}\"", output);
        }

        [Theory]
        [MemberData(nameof(JsonDateTimeTestData.DateTimeOffsetFractionTrimTests), MemberType = typeof(JsonDateTimeTestData))]
        public void WriteDateTimeOffset_TrimsFractionCorrectly(string testStr, string expectedStr)
        {
            var output = new ArrayBufferWriter<byte>(1024);
            using var jsonUtf8 = new Utf8JsonWriter(output);

            jsonUtf8.WriteStringValue(DateTimeOffset.ParseExact(testStr, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
            jsonUtf8.Flush();

            JsonTestHelper.AssertContents($"\"{expectedStr}\"", output);
        }

        [Fact]
        public void WriteDateTime_TrimsFractionCorrectly_SerializerRoundtrip()
        {
            DateTime utcNow = DateTimeTestHelpers.FixedDateTimeValue;
            Assert.Equal(utcNow, JsonSerializer.Deserialize(JsonSerializer.SerializeToUtf8Bytes(utcNow), typeof(DateTime)));
        }

        private static void WriteTooLargeHelper(JsonWriterOptions options, ReadOnlySpan<byte> key, ReadOnlySpan<byte> value, bool noThrow = false)
        {
            // Resizing is too slow, even for outerloop tests, so initialize to a large output size up front.
            var output = new ArrayBufferWriter<byte>(noThrow ? 40_000_000 : 1024);
            using var jsonUtf8 = new Utf8JsonWriter(output, options);

            jsonUtf8.WriteStartObject();

            try
            {
                jsonUtf8.WriteString(key, value);

                if (!noThrow)
                {
                    Assert.Fail($"Expected ArgumentException for data too large wasn't thrown. KeyLength: {key.Length} | ValueLength: {value.Length}");
                }
            }
            catch (ArgumentException)
            {
                if (noThrow)
                {
                    Assert.Fail($"Expected writing large key/value to succeed. KeyLength: {key.Length} | ValueLength: {value.Length}");
                }
            }

            jsonUtf8.WriteEndObject();
            jsonUtf8.Flush();
        }

        [Fact]
        public static void WriteStringValue_JsonEncodedText_Default()
        {
            JsonEncodedText text = default;
            WriteStringValueHelper(text, "\"\"");
        }

        [Theory]
        [MemberData(nameof(JsonEncodedTextStrings))]
        public static void WriteStringValue_JsonEncodedText(string message, string expectedMessage)
        {
            JsonEncodedText text = JsonEncodedText.Encode(message);
            WriteStringValueHelper(text, expectedMessage);
        }

        private static void WriteStringValueHelper(JsonEncodedText text, string expectedMessage)
        {
            var output = new ArrayBufferWriter<byte>();
            using var jsonUtf8 = new Utf8JsonWriter(output);
            jsonUtf8.WriteStringValue(text);
            jsonUtf8.Flush();

            JsonTestHelper.AssertContents($"{expectedMessage}", output);
        }

        [Theory]
        [InlineData(100)]
        [InlineData(1_000)]
        [InlineData(10_000)]
        public static void WriteStringValue_JsonEncodedText_Large(int stringLength)
        {
            {
                var message = new string('a', stringLength);
                var builder = new StringBuilder();
                builder.Append("\"");
                for (int i = 0; i < stringLength; i++)
                {
                    builder.Append("a");
                }
                builder.Append("\"");
                string expectedMessage = builder.ToString();

                JsonEncodedText text = JsonEncodedText.Encode(message);

                var output = new ArrayBufferWriter<byte>();
                using var jsonUtf8 = new Utf8JsonWriter(output);
                jsonUtf8.WriteStringValue(text);
                jsonUtf8.Flush();

                JsonTestHelper.AssertContents(expectedMessage, output);
            }
            {
                var message = new string('>', stringLength);
                var builder = new StringBuilder();
                builder.Append("\"");
                for (int i = 0; i < stringLength; i++)
                {
                    builder.Append("\\u003e");
                }
                builder.Append("\"");
                string expectedMessage = builder.ToString();

                JsonEncodedText text = JsonEncodedText.Encode(message);
                var output = new ArrayBufferWriter<byte>();
                using var jsonUtf8 = new Utf8JsonWriter(output);
                jsonUtf8.WriteStringValue(text);
                jsonUtf8.Flush();

                JsonTestHelper.AssertContents(expectedMessage, output);
            }
        }

        [Fact]
        public static void WriteStartArrayObject_JsonEncodedText_Default()
        {
            JsonEncodedText text = default;
            WriteArrayObjectHelper(text, "\"\"");
        }

        [Theory]
        [MemberData(nameof(JsonEncodedTextStrings))]
        public static void WriteStartArrayObject_JsonEncodedText(string message, string expectedMessage)
        {
            JsonEncodedText text = JsonEncodedText.Encode(message);
            WriteArrayObjectHelper(text, expectedMessage);
        }

        private static void WriteArrayObjectHelper(JsonEncodedText text, string expectedMessage)
        {
            {
                var output = new ArrayBufferWriter<byte>();
                using var jsonUtf8 = new Utf8JsonWriter(output);
                jsonUtf8.WriteStartObject();
                jsonUtf8.WriteStartObject(text);
                jsonUtf8.Flush();

                JsonTestHelper.AssertContents($"{{{expectedMessage}:{{", output);
            }

            {
                var output = new ArrayBufferWriter<byte>();
                using var jsonUtf8 = new Utf8JsonWriter(output);
                jsonUtf8.WriteStartObject();
                jsonUtf8.WriteStartArray(text);
                jsonUtf8.Flush();

                JsonTestHelper.AssertContents($"{{{expectedMessage}:[", output);
            }
        }

        [Fact]
        public static void WriteLiteral_JsonEncodedText_Default()
        {
            JsonEncodedText text = default;
            WriteLiteralHelper(text, "\"\"");
        }

        [Theory]
        [MemberData(nameof(JsonEncodedTextStrings))]
        public static void WriteLiteral_JsonEncodedText(string message, string expectedMessage)
        {
            JsonEncodedText text = JsonEncodedText.Encode(message);
            WriteLiteralHelper(text, expectedMessage);
        }

        private static void WriteLiteralHelper(JsonEncodedText text, string expectedMessage)
        {
            {
                var output = new ArrayBufferWriter<byte>();
                using var jsonUtf8 = new Utf8JsonWriter(output);
                jsonUtf8.WriteStartObject();
                jsonUtf8.WriteBoolean(text, true);
                jsonUtf8.Flush();

                JsonTestHelper.AssertContents($"{{{expectedMessage}:true", output);
            }

            {
                var output = new ArrayBufferWriter<byte>();
                using var jsonUtf8 = new Utf8JsonWriter(output);
                jsonUtf8.WriteStartObject();
                jsonUtf8.WriteBoolean(text, false);
                jsonUtf8.Flush();

                JsonTestHelper.AssertContents($"{{{expectedMessage}:false", output);
            }

            {
                var output = new ArrayBufferWriter<byte>();
                using var jsonUtf8 = new Utf8JsonWriter(output);
                jsonUtf8.WriteStartObject();
                jsonUtf8.WriteNull(text);
                jsonUtf8.Flush();

                JsonTestHelper.AssertContents($"{{{expectedMessage}:null", output);
            }
        }

        [Fact]
        public static void WriteNumber_JsonEncodedText_Default()
        {
            JsonEncodedText text = default;
            WriteNumberHelper(text, "\"\"");
        }

        [Theory]
        [MemberData(nameof(JsonEncodedTextStrings))]
        public static void WriteNumber_JsonEncodedText(string message, string expectedMessage)
        {
            JsonEncodedText text = JsonEncodedText.Encode(message);
            WriteNumberHelper(text, expectedMessage);
        }

        private static void WriteNumberHelper(JsonEncodedText text, string expectedMessage)
        {
            {
                var output = new ArrayBufferWriter<byte>();
                using var jsonUtf8 = new Utf8JsonWriter(output);
                jsonUtf8.WriteStartObject();
                int value = 1;
                jsonUtf8.WriteNumber(text, value);
                jsonUtf8.Flush();

                JsonTestHelper.AssertContents($"{{{expectedMessage}:1", output);
            }

            {
                var output = new ArrayBufferWriter<byte>();
                using var jsonUtf8 = new Utf8JsonWriter(output);
                jsonUtf8.WriteStartObject();
                long value = 1;
                jsonUtf8.WriteNumber(text, value);
                jsonUtf8.Flush();

                JsonTestHelper.AssertContents($"{{{expectedMessage}:1", output);
            }

            {
                var output = new ArrayBufferWriter<byte>();
                using var jsonUtf8 = new Utf8JsonWriter(output);
                jsonUtf8.WriteStartObject();
                uint value = 1;
                jsonUtf8.WriteNumber(text, value);
                jsonUtf8.Flush();

                JsonTestHelper.AssertContents($"{{{expectedMessage}:1", output);
            }

            {
                var output = new ArrayBufferWriter<byte>();
                using var jsonUtf8 = new Utf8JsonWriter(output);
                jsonUtf8.WriteStartObject();
                ulong value = 1;
                jsonUtf8.WriteNumber(text, value);
                jsonUtf8.Flush();

                JsonTestHelper.AssertContents($"{{{expectedMessage}:1", output);
            }

            {
                var output = new ArrayBufferWriter<byte>();
                using var jsonUtf8 = new Utf8JsonWriter(output);
                jsonUtf8.WriteStartObject();
                float value = 1;
                jsonUtf8.WriteNumber(text, value);
                jsonUtf8.Flush();

                JsonTestHelper.AssertContents($"{{{expectedMessage}:1", output);
            }

            {
                var output = new ArrayBufferWriter<byte>();
                using var jsonUtf8 = new Utf8JsonWriter(output);
                jsonUtf8.WriteStartObject();
                double value = 1;
                jsonUtf8.WriteNumber(text, value);
                jsonUtf8.Flush();

                JsonTestHelper.AssertContents($"{{{expectedMessage}:1", output);
            }

            {
                var output = new ArrayBufferWriter<byte>();
                using var jsonUtf8 = new Utf8JsonWriter(output);
                jsonUtf8.WriteStartObject();
                decimal value = 1;
                jsonUtf8.WriteNumber(text, value);
                jsonUtf8.Flush();

                JsonTestHelper.AssertContents($"{{{expectedMessage}:1", output);
            }
        }

        [Fact]
        public static void WriteStringDateAndGuid_JsonEncodedText_Default()
        {
            JsonEncodedText text = default;
            WriteStringHelper(text, "\"\"");
        }

        [Theory]
        [MemberData(nameof(JsonEncodedTextStrings))]
        public static void WriteStringDateAndGuid_JsonEncodedText(string message, string expectedMessage)
        {
            JsonEncodedText text = JsonEncodedText.Encode(message);
            WriteStringHelper(text, expectedMessage);
        }

        private static void WriteStringHelper(JsonEncodedText text, string expectedMessage)
        {
            {
                var output = new ArrayBufferWriter<byte>();
                using var jsonUtf8 = new Utf8JsonWriter(output);
                jsonUtf8.WriteStartObject();
                DateTime value = new DateTime(2019, 1, 1);
                jsonUtf8.WriteString(text, value);
                jsonUtf8.Flush();

                JsonTestHelper.AssertContents($"{{{expectedMessage}:\"{value.ToString("yyyy-MM-ddTHH:mm:ss")}\"", output);
            }

            {
                var output = new ArrayBufferWriter<byte>();
                using var jsonUtf8 = new Utf8JsonWriter(output);
                jsonUtf8.WriteStartObject();
                DateTimeOffset value = new DateTime(2019, 1, 1);
                jsonUtf8.WriteString(text, value);
                jsonUtf8.Flush();

                JsonTestHelper.AssertContents($"{{{expectedMessage}:\"{value.ToString("yyyy-MM-ddTHH:mm:ssK")}\"", output);
            }

            {
                var output = new ArrayBufferWriter<byte>();
                using var jsonUtf8 = new Utf8JsonWriter(output);
                jsonUtf8.WriteStartObject();
                Guid value = Guid.NewGuid();
                jsonUtf8.WriteString(text, value);
                jsonUtf8.Flush();

                JsonTestHelper.AssertContents($"{{{expectedMessage}:\"{value.ToString()}\"", output);
            }
        }

        [ConditionalTheory(typeof(Environment), nameof(Environment.Is64BitProcess))]
        [OuterLoop]
        [MemberData(nameof(JsonOptions_TestData))]
        public void WriteTooLargeArguments(JsonWriterOptions options)
        {
            try
            {
                byte[] bytesTooLarge;
                char[] charsTooLarge;
                var bytes = new byte[5];
                var chars = new char[5];

                bytesTooLarge = new byte[400_000_000];
                charsTooLarge = new char[400_000_000];

                bytesTooLarge.AsSpan().Fill((byte)'a');
                charsTooLarge.AsSpan().Fill('a');
                bytes.AsSpan().Fill((byte)'a');
                chars.AsSpan().Fill('a');

                var pipe = new Pipe();
                PipeWriter output = pipe.Writer;
                using var jsonUtf8 = new Utf8JsonWriter(output, options);

                jsonUtf8.WriteStartArray();

                Assert.Throws<ArgumentException>(() => jsonUtf8.WriteStartObject(bytesTooLarge));
                Assert.Throws<ArgumentException>(() => jsonUtf8.WriteString(bytesTooLarge, bytes));
                Assert.Throws<ArgumentException>(() => jsonUtf8.WriteString(bytes, bytesTooLarge));
                Assert.Throws<ArgumentException>(() => jsonUtf8.WriteString(bytesTooLarge, chars));
                Assert.Throws<ArgumentException>(() => jsonUtf8.WriteString(chars, bytesTooLarge));
                Assert.Throws<ArgumentException>(() => jsonUtf8.WriteString(bytesTooLarge, new DateTime(2015, 11, 9)));
                Assert.Throws<ArgumentException>(() => jsonUtf8.WriteString(bytesTooLarge, new DateTimeOffset(new DateTime(2015, 11, 9))));
                Assert.Throws<ArgumentException>(() => jsonUtf8.WriteString(bytesTooLarge, Guid.NewGuid()));
                Assert.Throws<ArgumentException>(() => jsonUtf8.WriteStringValue(bytesTooLarge));
                Assert.Throws<ArgumentException>(() => jsonUtf8.WriteCommentValue(bytesTooLarge));
                Assert.Throws<ArgumentException>(() => jsonUtf8.WriteNumber(bytesTooLarge, 10m));
                Assert.Throws<ArgumentException>(() => jsonUtf8.WriteNumber(bytesTooLarge, 10.1));
                Assert.Throws<ArgumentException>(() => jsonUtf8.WriteNumber(bytesTooLarge, 10.1f));
                Assert.Throws<ArgumentException>(() => jsonUtf8.WriteNumber(bytesTooLarge, 12345678901));
                Assert.Throws<ArgumentException>(() => jsonUtf8.WriteNumber(bytesTooLarge, (ulong)12345678901));
                Assert.Throws<ArgumentException>(() => jsonUtf8.WriteBoolean(bytesTooLarge, true));
                Assert.Throws<ArgumentException>(() => jsonUtf8.WriteNull(bytesTooLarge));
                Assert.Throws<ArgumentException>(() => jsonUtf8.WritePropertyName(bytesTooLarge));

                Assert.Throws<ArgumentException>(() => jsonUtf8.WriteStartObject(charsTooLarge));
                Assert.Throws<ArgumentException>(() => jsonUtf8.WriteString(charsTooLarge, chars));
                Assert.Throws<ArgumentException>(() => jsonUtf8.WriteString(chars, charsTooLarge));
                Assert.Throws<ArgumentException>(() => jsonUtf8.WriteString(charsTooLarge, bytes));
                Assert.Throws<ArgumentException>(() => jsonUtf8.WriteString(bytes, charsTooLarge));
                Assert.Throws<ArgumentException>(() => jsonUtf8.WriteString(charsTooLarge, new DateTime(2015, 11, 9)));
                Assert.Throws<ArgumentException>(() => jsonUtf8.WriteString(charsTooLarge, new DateTimeOffset(new DateTime(2015, 11, 9))));
                Assert.Throws<ArgumentException>(() => jsonUtf8.WriteString(charsTooLarge, Guid.NewGuid()));
                Assert.Throws<ArgumentException>(() => jsonUtf8.WriteStringValue(charsTooLarge));
                Assert.Throws<ArgumentException>(() => jsonUtf8.WriteCommentValue(charsTooLarge));
                Assert.Throws<ArgumentException>(() => jsonUtf8.WriteNumber(charsTooLarge, 10m));
                Assert.Throws<ArgumentException>(() => jsonUtf8.WriteNumber(charsTooLarge, 10.1));
                Assert.Throws<ArgumentException>(() => jsonUtf8.WriteNumber(charsTooLarge, 10.1f));
                Assert.Throws<ArgumentException>(() => jsonUtf8.WriteNumber(charsTooLarge, 12345678901));
                Assert.Throws<ArgumentException>(() => jsonUtf8.WriteNumber(charsTooLarge, (ulong)12345678901));
                Assert.Throws<ArgumentException>(() => jsonUtf8.WriteBoolean(charsTooLarge, true));
                Assert.Throws<ArgumentException>(() => jsonUtf8.WriteNull(charsTooLarge));
                Assert.Throws<ArgumentException>(() => jsonUtf8.WritePropertyName(charsTooLarge));

                jsonUtf8.Flush();
                Assert.Equal(1, jsonUtf8.BytesCommitted);
            }
            catch (OutOfMemoryException)
            {
                throw new SkipTestException("Out of memory allocating large objects");
            }
        }

        [Fact]
        public static void WriteBase64String_NullPropertyName()
        {
            WriteNullPropertyName_Simple(
                new byte[] { 0x01, 0x00, 0x01 },
                "\"AQAB\"",
                (writer, name, value) => writer.WriteBase64String(name, value),
                (writer, name, value) => writer.WriteBase64String(name, value),
                (writer, name, value) => writer.WriteBase64String(name, value));
        }

        [Fact]
        public static void WriteBoolean_NullPropertyName()
        {
            WriteNullPropertyName_Simple(
                false,
                "false",
                (writer, name, value) => writer.WriteBoolean(name, value),
                (writer, name, value) => writer.WriteBoolean(name, value),
                (writer, name, value) => writer.WriteBoolean(name, value));
        }

        [Fact]
        public static void WriteNull_NullPropertyName()
        {
            WriteNullPropertyName_NoValue(
                "null",
                cleanupAction: null,
                (writer, name) => writer.WriteNull(name),
                (writer, name) => writer.WriteNull(name),
                (writer, name) => writer.WriteNull(name));
        }

        [Fact]
        public static void WriteNumber_NullPropertyName_Decimal()
        {
            decimal numericValue = 1.04m;

            WriteNullPropertyName_Simple(
                numericValue,
                "1.04",
                (writer, name, value) => writer.WriteNumber(name, value),
                (writer, name, value) => writer.WriteNumber(name, value),
                (writer, name, value) => writer.WriteNumber(name, value));
        }

        [Fact]
        public static void WriteNumber_NullPropertyName_Double()
        {
            double numericValue = 1.05d;

            WriteNullPropertyName_Simple(
                numericValue,
                "1.05",
                (writer, name, value) => writer.WriteNumber(name, value),
                (writer, name, value) => writer.WriteNumber(name, value),
                (writer, name, value) => writer.WriteNumber(name, value));
        }

        [Fact]
        public static void WriteNumber_NullPropertyName_Int32()
        {
            int numericValue = 1048576;

            WriteNullPropertyName_Simple(
                numericValue,
                "1048576",
                (writer, name, value) => writer.WriteNumber(name, value),
                (writer, name, value) => writer.WriteNumber(name, value),
                (writer, name, value) => writer.WriteNumber(name, value));
        }

        [Fact]
        public static void WriteNumber_NullPropertyName_Int64()
        {
            long numericValue = 0x0100_0000_0000;

            WriteNullPropertyName_Simple(
                numericValue,
                "1099511627776",
                (writer, name, value) => writer.WriteNumber(name, value),
                (writer, name, value) => writer.WriteNumber(name, value),
                (writer, name, value) => writer.WriteNumber(name, value));
        }

        [Fact]
        public static void WriteNumber_NullPropertyName_Single()
        {
            float numericValue = 1e3f;

            WriteNullPropertyName_Simple(
                numericValue,
                "1000",
                (writer, name, value) => writer.WriteNumber(name, value),
                (writer, name, value) => writer.WriteNumber(name, value),
                (writer, name, value) => writer.WriteNumber(name, value));
        }

        [Fact]
        public static void WriteNumber_NullPropertyName_UInt32()
        {
            uint numericValue = 0x8000_0000;

            WriteNullPropertyName_Simple(
                numericValue,
                "2147483648",
                (writer, name, value) => writer.WriteNumber(name, value),
                (writer, name, value) => writer.WriteNumber(name, value),
                (writer, name, value) => writer.WriteNumber(name, value));
        }

        [Fact]
        public static void WriteNumber_NullPropertyName_UInt64()
        {
            ulong numericValue = ulong.MaxValue;

            WriteNullPropertyName_Simple(
                numericValue,
                "18446744073709551615",
                (writer, name, value) => writer.WriteNumber(name, value),
                (writer, name, value) => writer.WriteNumber(name, value),
                (writer, name, value) => writer.WriteNumber(name, value));
        }

        [Fact]
        public static void WritePropertyName_NullPropertyName()
        {
            WriteNullPropertyName_NoValue(
                "null",
                writer => writer.WriteNullValue(),
                (writer, name) => writer.WritePropertyName(name),
                (writer, name) => writer.WritePropertyName(name),
                (writer, name) => writer.WritePropertyName(name));
        }

        [Fact]
        public static void WriteStartArray_NullPropertyName()
        {
            WriteNullPropertyName_NoValue(
                "[]",
                writer => writer.WriteEndArray(),
                (writer, name) => writer.WriteStartArray(name),
                (writer, name) => writer.WriteStartArray(name),
                (writer, name) => writer.WriteStartArray(name));
        }

        [Fact]
        public static void WriteStartObject_NullPropertyName()
        {
            WriteNullPropertyName_NoValue(
                "{}",
                writer => writer.WriteEndObject(),
                (writer, name) => writer.WriteStartObject(name),
                (writer, name) => writer.WriteStartObject(name),
                (writer, name) => writer.WriteStartObject(name));
        }

        [Fact]
        public static void WriteString_NullPropertyName_DateTime()
        {
            WriteNullPropertyName_Simple(
                DateTime.MinValue,
                "\"0001-01-01T00:00:00\"",
                (writer, name, value) => writer.WriteString(name, value),
                (writer, name, value) => writer.WriteString(name, value),
                (writer, name, value) => writer.WriteString(name, value));
        }

        [Fact]
        public static void WriteString_NullPropertyName_DateTimeOffset()
        {
            WriteNullPropertyName_Simple(
                DateTimeOffset.MinValue,
                "\"0001-01-01T00:00:00+00:00\"",
                (writer, name, value) => writer.WriteString(name, value),
                (writer, name, value) => writer.WriteString(name, value),
                (writer, name, value) => writer.WriteString(name, value));
        }

        [Fact]
        public static void WriteString_NullPropertyName_Guid()
        {
            WriteNullPropertyName_Simple(
                Guid.Empty,
                "\"00000000-0000-0000-0000-000000000000\"",
                (writer, name, value) => writer.WriteString(name, value),
                (writer, name, value) => writer.WriteString(name, value),
                (writer, name, value) => writer.WriteString(name, value));
        }

        [Fact]
        public static void WriteString_NullPropertyName_ReadOnlySpan_Byte()
        {
            WriteNullPropertyName_Simple(
                "utf8"u8.ToArray(),
                "\"utf8\"",
                (writer, name, value) => writer.WriteString(name, value),
                (writer, name, value) => writer.WriteString(name, value),
                (writer, name, value) => writer.WriteString(name, value));
        }

        [Fact]
        public static void WriteString_NullPropertyName_ReadOnlySpan_Char()
        {
            WriteNullPropertyName_Simple(
                "utf16",
                "\"utf16\"",
                (writer, name, value) => writer.WriteString(name, value.AsSpan()),
                (writer, name, value) => writer.WriteString(name, value.AsSpan()),
                (writer, name, value) => writer.WriteString(name, value.AsSpan()));
        }

        [Fact]
        public static void WriteString_NullPropertyName_String()
        {
            WriteNullPropertyName_Simple(
                "string",
                "\"string\"",
                (writer, name, value) => writer.WriteString(name, value),
                (writer, name, value) => writer.WriteString(name, value),
                (writer, name, value) => writer.WriteString(name, value));
        }

        [Fact]
        public static void WriteString_NullPropertyName_JsonEncodedText()
        {
            WriteNullPropertyName_Simple(
                JsonEncodedText.Encode("jet"),
                "\"jet\"",
                (writer, name, value) => writer.WriteString(name, value),
                (writer, name, value) => writer.WriteString(name, value),
                (writer, name, value) => writer.WriteString(name, value));
        }

        [Fact]
        public static void WriteCommentValue_NullString()
        {
            // WriteCommentValue is sufficiently different (no comma after a legal value)
            // that it doesn't warrant a helper for expansion.
            var output = new ArrayBufferWriter<byte>(1024);
            string nullString = null;

            using (var writer = new Utf8JsonWriter(output))
            {
                writer.WriteStartArray();

                AssertExtensions.Throws<ArgumentNullException>(
                    "value",
                    () => writer.WriteCommentValue(nullString));

                ReadOnlySpan<char> nullStringSpan = nullString.AsSpan();
                writer.WriteCommentValue(nullStringSpan);

                writer.WriteCommentValue(ReadOnlySpan<byte>.Empty);

                writer.WriteEndArray();
                writer.Flush();
            }

            JsonTestHelper.AssertContents("[/**//**/]", output);
        }

        [Fact]
        public static void WriteStringValue_NullString()
        {
            WriteNullValue_InArray(
                "\"\"",
                "null",
                (writer, value) => writer.WriteStringValue(value),
                (writer, value) => writer.WriteStringValue(value),
                (writer, value) => writer.WriteStringValue(value));
        }

        [Fact]
        public static void WriteStringValue_StringProperty_NullString()
        {
            WriteNullValue_InObject(
                "\"propStr\":\"\"",
                "\"propStr\":null",
                (writer, value) => writer.WriteString("propStr", value),
                (writer, value) => writer.WriteString("propStr", value),
                (writer, value) => writer.WriteString("propStr", value));
        }

        [Fact]
        public static void WriteStringValue_ReadOnlySpanCharProperty_NullString()
        {
            WriteNullValue_InObject(
                "\"propUtf16\":\"\"",
                "\"propUtf16\":null",
                (writer, value) => writer.WriteString("propUtf16".AsSpan(), value),
                (writer, value) => writer.WriteString("propUtf16".AsSpan(), value),
                (writer, value) => writer.WriteString("propUtf16".AsSpan(), value));
        }

        [Fact]
        public static void WriteStringValue_ReadOnlySpanBytesProperty_NullString()
        {
            byte[] propertyName = "propUtf8"u8.ToArray();

            WriteNullValue_InObject(
                "\"propUtf8\":\"\"",
                "\"propUtf8\":null",
                (writer, value) => writer.WriteString(propertyName, value),
                (writer, value) => writer.WriteString(propertyName, value),
                (writer, value) => writer.WriteString(propertyName, value));
        }

        [Fact]
        public static void WriteStringValue_JsonEncodedTextProperty_NullString()
        {
            JsonEncodedText jet = JsonEncodedText.Encode("propJet");

            WriteNullValue_InObject(
                "\"propJet\":\"\"",
                "\"propJet\":null",
                (writer, value) => writer.WriteString(jet, value),
                (writer, value) => writer.WriteString(jet, value),
                (writer, value) => writer.WriteString(jet, value));
        }

        [Fact]
        public static void WriteStringValue_IndentationOptions()
        {
            var options = new JsonWriterOptions();
            var expectedOutput = GetCustomExpectedString(options);

            options.IndentCharacter = '\t';
            options.IndentSize = 127;

            var output = GetCustomExpectedString(options);

            Assert.Equal(expectedOutput, output);
        }

        private delegate void WriteValueSpanAction<T>(
            Utf8JsonWriter writer,
            ReadOnlySpan<T> value);

        private delegate void WritePropertySpanAction<T>(
            Utf8JsonWriter writer,
            ReadOnlySpan<T> propertyName);

        private delegate void WritePropertySpanAction<T1, T2>(
            Utf8JsonWriter writer,
            ReadOnlySpan<T1> propertyName,
            T2 value);

        private static void WriteNullPropertyName_Simple<T>(
            T value,
            string wireValue,
            Action<Utf8JsonWriter, string, T> stringAction,
            WritePropertySpanAction<char, T> charSpanAction,
            WritePropertySpanAction<byte, T> byteSpanAction)
        {
            var output = new ArrayBufferWriter<byte>(1024);
            string nullString = null;

            using (var writer = new Utf8JsonWriter(output))
            {
                writer.WriteStartObject();

                AssertExtensions.Throws<ArgumentNullException>(
                    "propertyName",
                    () => stringAction(writer, nullString, value));

                writer.WriteEndObject();
                writer.Flush();
            }

            JsonTestHelper.AssertContents("{}", output);
            output.Clear();

            using (var writer = new Utf8JsonWriter(output))
            {
                writer.WriteStartObject();

                ReadOnlySpan<char> nullStringSpan = nullString.AsSpan();
                charSpanAction(writer, nullStringSpan, value);

                byteSpanAction(writer, ReadOnlySpan<byte>.Empty, value);

                writer.WriteEndObject();
                writer.Flush();
            }

            JsonTestHelper.AssertContents($"{{\"\":{wireValue},\"\":{wireValue}}}", output);
        }

        private static void WriteNullPropertyName_NoValue(
            string wireValue,
            Action<Utf8JsonWriter> cleanupAction,
            Action<Utf8JsonWriter, string> stringAction,
            WritePropertySpanAction<char> charSpanAction,
            WritePropertySpanAction<byte> byteSpanAction)
        {
            var output = new ArrayBufferWriter<byte>(1024);
            string nullString = null;

            using (var writer = new Utf8JsonWriter(output))
            {
                writer.WriteStartObject();

                AssertExtensions.Throws<ArgumentNullException>(
                    "propertyName",
                    () => stringAction(writer, nullString));

                writer.WriteEndObject();
                writer.Flush();
            }

            JsonTestHelper.AssertContents("{}", output);
            output.Clear();

            using (var writer = new Utf8JsonWriter(output))
            {
                writer.WriteStartObject();

                ReadOnlySpan<char> nullStringSpan = nullString.AsSpan();
                charSpanAction(writer, nullStringSpan);
                cleanupAction?.Invoke(writer);

                byteSpanAction(writer, ReadOnlySpan<byte>.Empty);
                cleanupAction?.Invoke(writer);

                writer.WriteEndObject();
                writer.Flush();
            }

            JsonTestHelper.AssertContents($"{{\"\":{wireValue},\"\":{wireValue}}}", output);
        }

        private static void WriteNullValue_InObject(
            string wireValue,
            string nullValue,
            Action<Utf8JsonWriter, string> stringAction,
            WriteValueSpanAction<char> charSpanAction,
            WriteValueSpanAction<byte> byteSpanAction)
        {
            var output = new ArrayBufferWriter<byte>(1024);
            string nullString = null;

            using (var writer = new Utf8JsonWriter(output))
            {
                writer.WriteStartObject();

                stringAction(writer, nullString);

                ReadOnlySpan<char> nullStringSpan = nullString.AsSpan();
                charSpanAction(writer, nullStringSpan);

                byteSpanAction(writer, ReadOnlySpan<byte>.Empty);

                writer.WriteEndObject();
                writer.Flush();
            }

            JsonTestHelper.AssertContents($"{{{nullValue},{wireValue},{wireValue}}}", output);
        }

        private static void WriteNullValue_InArray(
            string wireValue,
            string nullValue,
            Action<Utf8JsonWriter, string> stringAction,
            WriteValueSpanAction<char> charSpanAction,
            WriteValueSpanAction<byte> byteSpanAction)
        {
            var output = new ArrayBufferWriter<byte>(1024);
            string nullString = null;

            using (var writer = new Utf8JsonWriter(output))
            {
                writer.WriteStartArray();

                stringAction(writer, nullString);

                ReadOnlySpan<char> nullStringSpan = nullString.AsSpan();
                charSpanAction(writer, nullStringSpan);

                byteSpanAction(writer, ReadOnlySpan<byte>.Empty);

                writer.WriteEndArray();
                writer.Flush();
            }

            JsonTestHelper.AssertContents($"[{nullValue},{wireValue},{wireValue}]", output);
        }

        private static string GetHelloWorldExpectedString(JsonWriterOptions options, string propertyName, string value)
        {
            var ms = new MemoryStream();
            TextWriter streamWriter = new StreamWriter(ms, new UTF8Encoding(false), 1024, true);

            var json = new JsonTextWriter(streamWriter)
            {
                Formatting = options.Indented ? Formatting.Indented : Formatting.None,
                StringEscapeHandling = StringEscapeHandling.EscapeHtml
            };

            json.WriteStartObject();
            json.WritePropertyName(propertyName);
            json.WriteValue(value);
            json.WritePropertyName(propertyName);
            json.WriteValue(value);
            json.WriteEnd();

            json.Flush();

            return HandleFormatting(Encoding.UTF8.GetString(ms.ToArray()), options);
        }

        private static string GetBase64ExpectedString(JsonWriterOptions options, string propertyName, byte[] value)
        {
            var ms = new MemoryStream();
            TextWriter streamWriter = new StreamWriter(ms, new UTF8Encoding(false), 1024, true);

            var json = new JsonTextWriter(streamWriter)
            {
                Formatting = options.Indented ? Formatting.Indented : Formatting.None,
                StringEscapeHandling = StringEscapeHandling.EscapeHtml
            };

            json.WriteStartObject();
            json.WritePropertyName(propertyName);
            json.WriteValue(value);
            json.WritePropertyName(propertyName);
            json.WriteValue(value);
            json.WritePropertyName("array");
            json.WriteStartArray();
            json.WriteValue(new byte[] { 1, 2 });
            json.WriteValue(new byte[] { 3, 4 });
            json.WriteEndArray();
            json.WriteEnd();

            json.Flush();

            return HandleFormatting(Encoding.UTF8.GetString(ms.ToArray()), options);
        }

        private static string GetCommentInArrayExpectedString(JsonWriterOptions options, string comment)
        {
            var ms = new MemoryStream();
            TextWriter streamWriter = new StreamWriter(ms, new UTF8Encoding(false), 1024, true);

            var json = new JsonTextWriter(streamWriter)
            {
                Formatting = options.Indented ? Formatting.Indented : Formatting.None,
                StringEscapeHandling = StringEscapeHandling.EscapeHtml,
            };

            json.WriteComment(comment);

            CompensateNewLine(options.Indented, json, streamWriter);
            json.WriteStartArray();
            for (int j = 0; j < 10; j++)
                json.WriteComment(comment);
            json.WriteValue(comment);
            json.WriteComment(comment);
            json.WriteStartArray();
            json.WriteEndArray();
            json.WriteComment(comment);
            json.WriteStartObject();
            json.WriteEndObject();
            json.WriteComment(comment);
            json.WriteEnd();

            CompensateNewLine(options.Indented, json, streamWriter);
            json.WriteComment(comment);
            json.Flush();

            return HandleFormatting(Encoding.UTF8.GetString(ms.ToArray()), options);
        }

        private static string GetCommentInObjectExpectedString(JsonWriterOptions options, string comment)
        {
            var ms = new MemoryStream();
            TextWriter streamWriter = new StreamWriter(ms, new UTF8Encoding(false), 1024, true);

            var json = new JsonTextWriter(streamWriter)
            {
                Formatting = options.Indented ? Formatting.Indented : Formatting.None,
                StringEscapeHandling = StringEscapeHandling.EscapeHtml,
            };

            json.WriteComment(comment);

            CompensateNewLine(options.Indented, json, streamWriter);
            json.WriteStartObject();

            CompensateNewLine(options.Indented, json, streamWriter);
            CompensateWhitespaces(options.Indented, json, streamWriter, 2);
            json.WriteComment(comment);

            json.WritePropertyName("property1");

            CompensateWhitespaces(options.Indented, json, streamWriter); 
            CompensateNewLine(options.Indented, json, streamWriter);
            CompensateWhitespaces(options.Indented, json, streamWriter);

            json.WriteComment(comment);

            CompensateNewLine(options.Indented, json, streamWriter);
            CompensateWhitespaces(options.Indented, json, streamWriter);

            json.WriteStartArray();
            json.WriteEndArray();

            CompensateNewLine(options.Indented, json, streamWriter);
            CompensateWhitespaces(options.Indented, json, streamWriter, 2);

            json.WriteComment(comment);

            json.WritePropertyName("property2");

            CompensateWhitespaces(options.Indented, json, streamWriter);
            CompensateNewLine(options.Indented, json, streamWriter);
            CompensateWhitespaces(options.Indented, json, streamWriter);

            json.WriteComment(comment);

            CompensateNewLine(options.Indented, json, streamWriter);
            CompensateWhitespaces(options.Indented, json, streamWriter);

            json.WriteStartObject();
            json.WriteEndObject();

            CompensateNewLine(options.Indented, json, streamWriter);
            CompensateWhitespaces(options.Indented, json, streamWriter, 2);

            json.WriteComment(comment);
            json.WriteEnd();

            CompensateNewLine(options.Indented, json, streamWriter);
            json.WriteComment(comment);
            json.Flush();

            return HandleFormatting(Encoding.UTF8.GetString(ms.ToArray()), options);
        }

        private static string GetStringsExpectedString(JsonWriterOptions options, string value)
        {
            var ms = new MemoryStream();
            TextWriter streamWriter = new StreamWriter(ms, new UTF8Encoding(false), 1024, true);

            var json = new JsonTextWriter(streamWriter)
            {
                Formatting = options.Indented ? Formatting.Indented : Formatting.None
            };

            json.WriteStartArray();
            for (int j = 0; j < 10; j++)
                json.WriteValue(value);
            json.WriteEnd();

            json.Flush();

            return HandleFormatting(Encoding.UTF8.GetString(ms.ToArray()), options);
        }

        private static string GetEscapedExpectedString(JsonWriterOptions options, string propertyName, string value, StringEscapeHandling escaping, bool escape = true)
        {
            using (TextWriter stringWriter = new StringWriter())
            using (var json = new JsonTextWriter(stringWriter)
            {
                Formatting = options.Indented ? Formatting.Indented : Formatting.None,
                StringEscapeHandling = escaping
            })
            {
                json.WriteStartObject();
                json.WritePropertyName(propertyName, escape);
                json.WriteValue(value);
                json.WriteEnd();

                json.Flush();
                return HandleFormatting(stringWriter.ToString(), options);
            }
        }

        private static string GetCustomExpectedString(JsonWriterOptions options)
        {
            var ms = new MemoryStream();
            TextWriter streamWriter = new StreamWriter(ms, new UTF8Encoding(false), 1024, true);

            var json = new JsonTextWriter(streamWriter)
            {
                Formatting = options.Indented ? Formatting.Indented : Formatting.None
            };

            json.WriteStartObject();
            for (int i = 0; i < 1_000; i++)
            {
                json.WritePropertyName("message");
                json.WriteValue("Hello, World!");
            }
            json.WriteEnd();

            json.Flush();

            return HandleFormatting(Encoding.UTF8.GetString(ms.ToArray()), options);
        }

        private static string GetStartEndExpectedString(JsonWriterOptions options)
        {
            var ms = new MemoryStream();
            TextWriter streamWriter = new StreamWriter(ms, new UTF8Encoding(false), 1024, true);

            var json = new JsonTextWriter(streamWriter)
            {
                Formatting = options.Indented ? Formatting.Indented : Formatting.None
            };

            json.WriteStartArray();
            json.WriteStartObject();
            json.WriteEnd();
            json.WriteEnd();

            json.Flush();

            return HandleFormatting(Encoding.UTF8.GetString(ms.ToArray()), options);
        }

        private static string GetStartEndWithPropertyArrayExpectedString(JsonWriterOptions options)
        {
            var ms = new MemoryStream();
            TextWriter streamWriter = new StreamWriter(ms, new UTF8Encoding(false), 1024, true);

            var json = new JsonTextWriter(streamWriter)
            {
                Formatting = options.Indented ? Formatting.Indented : Formatting.None
            };

            json.WriteStartObject();
            json.WritePropertyName("property name");
            json.WriteStartArray();
            json.WriteEnd();
            json.WriteEnd();

            json.Flush();

            return HandleFormatting(Encoding.UTF8.GetString(ms.ToArray()), options);
        }

        private static string GetStartEndWithPropertyArrayExpectedString(string key, JsonWriterOptions options, bool escape = false)
        {
            var ms = new MemoryStream();
            TextWriter streamWriter = new StreamWriter(ms, new UTF8Encoding(false), 1024, true);

            var json = new JsonTextWriter(streamWriter)
            {
                Formatting = options.Indented ? Formatting.Indented : Formatting.None,
                StringEscapeHandling = StringEscapeHandling.EscapeHtml
            };

            json.WriteStartObject();
            json.WritePropertyName(key, escape);
            json.WriteStartArray();
            json.WriteEnd();
            json.WriteEnd();

            json.Flush();

            return HandleFormatting(Encoding.UTF8.GetString(ms.ToArray()), options);
        }

        private static string GetStartEndWithPropertyObjectExpectedString(JsonWriterOptions options)
        {
            var ms = new MemoryStream();
            TextWriter streamWriter = new StreamWriter(ms, new UTF8Encoding(false), 1024, true);

            var json = new JsonTextWriter(streamWriter)
            {
                Formatting = options.Indented ? Formatting.Indented : Formatting.None
            };

            json.WriteStartObject();
            json.WritePropertyName("property name");
            json.WriteStartObject();
            json.WriteEnd();
            json.WriteEnd();

            json.Flush();

            return HandleFormatting(Encoding.UTF8.GetString(ms.ToArray()), options);
        }

        private static string GetStartEndWithPropertyObjectExpectedString(string key, JsonWriterOptions options, bool escape = false)
        {
            var ms = new MemoryStream();
            TextWriter streamWriter = new StreamWriter(ms, new UTF8Encoding(false), 1024, true);

            var json = new JsonTextWriter(streamWriter)
            {
                Formatting = options.Indented ? Formatting.Indented : Formatting.None,
                StringEscapeHandling = StringEscapeHandling.EscapeHtml
            };

            json.WriteStartObject();
            json.WritePropertyName(key, escape);
            json.WriteStartObject();
            json.WriteEnd();
            json.WriteEnd();

            json.Flush();

            return HandleFormatting(Encoding.UTF8.GetString(ms.ToArray()), options);
        }

        private static string GetArrayWithPropertyExpectedString(JsonWriterOptions options)
        {
            var ms = new MemoryStream();
            TextWriter streamWriter = new StreamWriter(ms, new UTF8Encoding(false), 1024, true);

            var json = new JsonTextWriter(streamWriter)
            {
                Formatting = options.Indented ? Formatting.Indented : Formatting.None
            };

            json.WriteStartObject();
            json.WritePropertyName("message");
            json.WriteStartArray();
            json.WriteEndArray();
            json.WriteEndObject();
            json.Flush();

            return HandleFormatting(Encoding.UTF8.GetString(ms.ToArray()), options);
        }

        private static string GetBooleanExpectedString(JsonWriterOptions options, string keyString, bool value, bool escape = false)
        {
            var ms = new MemoryStream();
            TextWriter streamWriter = new StreamWriter(ms, new UTF8Encoding(false), 1024, true);

            var json = new JsonTextWriter(streamWriter)
            {
                Formatting = options.Indented ? Formatting.Indented : Formatting.None,
                StringEscapeHandling = StringEscapeHandling.EscapeHtml,
            };

            json.WriteStartObject();
            json.WritePropertyName(keyString, escape);
            json.WriteValue(value);

            json.WritePropertyName("temp");
            json.WriteStartArray();
            json.WriteValue(true);
            json.WriteValue(true);
            json.WriteValue(false);
            json.WriteValue(false);
            json.WriteEnd();

            json.WriteEnd();

            json.Flush();

            return HandleFormatting(Encoding.UTF8.GetString(ms.ToArray()), options);
        }

        private static string GetNullExpectedString(JsonWriterOptions options, string keyString, bool escape = false)
        {
            var ms = new MemoryStream();
            TextWriter streamWriter = new StreamWriter(ms, new UTF8Encoding(false), 1024, true);

            var json = new JsonTextWriter(streamWriter)
            {
                Formatting = options.Indented ? Formatting.Indented : Formatting.None,
                StringEscapeHandling = StringEscapeHandling.EscapeHtml,
            };

            json.WriteStartObject();
            json.WritePropertyName(keyString, escape);
            json.WriteNull();
            json.WritePropertyName(keyString, escape);
            json.WriteNull();

            json.WritePropertyName("temp");
            json.WriteStartArray();
            json.WriteValue((string)null);
            json.WriteValue((string)null);
            json.WriteEnd();

            json.WriteEnd();

            json.Flush();

            return HandleFormatting(Encoding.UTF8.GetString(ms.ToArray()), options);
        }

        private static string GetPropertyExpectedString<T>(JsonWriterOptions options, T value)
        {
            var sb = new StringBuilder();
            StringWriter stringWriter = new StringWriter(sb);

            var json = new JsonTextWriter(stringWriter)
            {
                Formatting = options.Indented ? Formatting.Indented : Formatting.None
            };

            json.WriteStartObject();
            json.WritePropertyName("message");
            json.WriteValue(value);
            json.WriteEnd();

            json.Flush();

            return HandleFormatting(sb.ToString(), options);
        }

        private static string GetNumbersExpectedString(JsonWriterOptions options, string keyString, int[] ints, uint[] uints, long[] longs, ulong[] ulongs, float[] floats, double[] doubles, decimal[] decimals, bool escape = false)
        {
            var ms = new MemoryStream();
            TextWriter streamWriter = new StreamWriter(ms, new UTF8Encoding(false), 1024, true);

            var json = new JsonTextWriter(streamWriter)
            {
                Formatting = options.Indented ? Formatting.Indented : Formatting.None
            };

            json.WriteStartObject();

            for (int i = 0; i < floats.Length; i++)
            {
                json.WritePropertyName(keyString, escape);
                json.WriteValue(floats[i]);
            }
            for (int i = 0; i < ints.Length; i++)
            {
                json.WritePropertyName(keyString, escape);
                json.WriteValue(ints[i]);
            }
            for (int i = 0; i < uints.Length; i++)
            {
                json.WritePropertyName(keyString, escape);
                json.WriteValue(uints[i]);
            }
            for (int i = 0; i < doubles.Length; i++)
            {
                json.WritePropertyName(keyString, escape);
                json.WriteValue(doubles[i]);
            }
            for (int i = 0; i < longs.Length; i++)
            {
                json.WritePropertyName(keyString, escape);
                json.WriteValue(longs[i]);
            }
            for (int i = 0; i < ulongs.Length; i++)
            {
                json.WritePropertyName(keyString, escape);
                json.WriteValue(ulongs[i]);
            }
            for (int i = 0; i < decimals.Length; i++)
            {
                json.WritePropertyName(keyString, escape);
                json.WriteValue(decimals[i]);
            }

            json.WritePropertyName(keyString, escape);
            json.WriteStartArray();
            json.WriteValue(floats[0]);
            json.WriteValue(ints[0]);
            json.WriteValue(uints[0]);
            json.WriteValue(doubles[0]);
            json.WriteValue(longs[0]);
            json.WriteValue(ulongs[0]);
            json.WriteValue(decimals[0]);
            json.WriteEndArray();

            json.WriteEnd();

            json.Flush();

            return HandleFormatting(Encoding.UTF8.GetString(ms.ToArray()), options);
        }

        private static string GetExpectedString_RelaxedEscaping(bool prettyPrint, string keyString)
        {
            var ms = new MemoryStream();
            TextWriter streamWriter = new StreamWriter(ms, new UTF8Encoding(false), 1024, true);

            var json = new JsonTextWriter(streamWriter)
            {
                Formatting = prettyPrint ? Formatting.Indented : Formatting.None,
            };

            json.WriteStartObject();

            json.WritePropertyName(keyString, escape: true);
            json.WriteValue(keyString);

            json.WritePropertyName(keyString, escape: true);
            json.WriteStartArray();
            json.WriteValue(keyString);
            json.WriteValue(keyString);
            json.WriteEnd();

            json.WriteEnd();

            json.Flush();

            return Encoding.UTF8.GetString(ms.ToArray());
        }

        private static string GetGuidsExpectedString(JsonWriterOptions options, string keyString, Guid[] guids, bool escape = false)
        {
            var ms = new MemoryStream();
            TextWriter streamWriter = new StreamWriter(ms, new UTF8Encoding(false), 1024, true);

            var json = new JsonTextWriter(streamWriter)
            {
                Formatting = options.Indented ? Formatting.Indented : Formatting.None,
                StringEscapeHandling = StringEscapeHandling.EscapeHtml
            };

            json.WriteStartObject();

            for (int i = 0; i < guids.Length; i++)
            {
                json.WritePropertyName(keyString, escape);
                json.WriteValue(guids[i]);
            }

            json.WritePropertyName(keyString, escape);
            json.WriteStartArray();
            json.WriteValue(guids[0]);
            json.WriteValue(guids[1]);
            json.WriteEnd();

            json.WriteEnd();

            json.Flush();

            return HandleFormatting(Encoding.UTF8.GetString(ms.ToArray()), options);
        }

        private static string GetNumbersExpectedString<T>(JsonWriterOptions options, int numberOfElements, T value)
        {
            var sb = new StringBuilder();
            StringWriter stringWriter = new StringWriter(sb);

            var json = new JsonTextWriter(stringWriter)
            {
                Formatting = options.Indented ? Formatting.Indented : Formatting.None,
            };

            json.WriteStartArray();
            for (int i = 0; i < numberOfElements; i++)
            {
                json.WriteValue(value);
            }
            json.WriteEnd();

            json.Flush();

            return HandleFormatting(sb.ToString(), options);
        }

        private static string GetDatesExpectedString(JsonWriterOptions options, string keyString, DateTime[] dates, bool escape = false)
        {
            var ms = new MemoryStream();
            TextWriter streamWriter = new StreamWriter(ms, new UTF8Encoding(false), 1024, true);

            var json = new JsonTextWriter(streamWriter)
            {
                Formatting = options.Indented ? Formatting.Indented : Formatting.None,
                StringEscapeHandling = StringEscapeHandling.EscapeHtml,
            };

            json.WriteStartObject();

            for (int i = 0; i < dates.Length; i++)
            {
                json.WritePropertyName(keyString, escape);
                json.WriteValue(dates[i]);
            }

            json.WritePropertyName(keyString, escape);
            json.WriteStartArray();
            json.WriteValue(dates[0]);
            json.WriteValue(dates[1]);
            json.WriteEnd();

            json.WriteEnd();

            json.Flush();

            return HandleFormatting(Encoding.UTF8.GetString(ms.ToArray()), options);
        }

        private static string GetDatesExpectedString(JsonWriterOptions options, string keyString, DateTimeOffset[] dates, bool escape = false)
        {
            var ms = new MemoryStream();
            TextWriter streamWriter = new StreamWriter(ms, new UTF8Encoding(false), 1024, true);

            var json = new JsonTextWriter(streamWriter)
            {
                Formatting = options.Indented ? Formatting.Indented : Formatting.None,
                StringEscapeHandling = StringEscapeHandling.EscapeHtml,
            };

            json.WriteStartObject();

            for (int i = 0; i < dates.Length; i++)
            {
                json.WritePropertyName(keyString, escape);
                json.WriteValue(dates[i]);
            }

            json.WritePropertyName(keyString, escape);
            json.WriteStartArray();
            json.WriteValue(dates[0]);
            json.WriteValue(dates[1]);
            json.WriteEnd();

            json.WriteEnd();

            json.Flush();

            return HandleFormatting(Encoding.UTF8.GetString(ms.ToArray()), options);
        }

        private static void CompensateWhitespaces(bool prettyPrint, JsonTextWriter json, TextWriter streamWriter, int whitespaceCount = 1)
        {
            if (prettyPrint)
            {
                json.Flush();
                streamWriter.Write(new string(' ', whitespaceCount));
            }
        }

        private static void CompensateNewLine(bool prettyPrint, JsonTextWriter json, TextWriter streamWriter)
        {
            if (prettyPrint)
            {
                json.Flush();
                streamWriter.WriteLine();
            }
        }

        private static string HandleFormatting(string text, JsonWriterOptions options)
        {
            var normalized = text.Replace("  ", GetIndentText(options));

            if (options.NewLine != Environment.NewLine)
            {
                normalized = normalized.Replace(Environment.NewLine, options.NewLine);
            }

            return normalized;
        }

        private static string GetIndentText(JsonWriterOptions options) => new(options.IndentCharacter, options.IndentSize);

        public static IEnumerable<object[]> JsonEncodedTextStrings
        {
            get
            {
                return new List<object[]>
                {
                    new object[] {"", "\"\"" },
                    new object[] { "message", "\"message\"" },
                    new object[] { "mess\"age", "\"mess\\u0022age\"" },
                    new object[] { "mess\\u0022age", "\"mess\\\\u0022age\"" },
                    new object[] { ">>>>>", "\"\\u003E\\u003E\\u003E\\u003E\\u003E\"" },
                    new object[] { "\\u003E\\u003E\\u003E\\u003E\\u003E", "\"\\\\u003E\\\\u003E\\\\u003E\\\\u003E\\\\u003E\"" },
                };
            }
        }

        private static IEnumerable<JsonWriterOptions> JsonOptions()
        {
            return from indented in new[] { true, false }
                   from skipValidation in new[] { true, false }
                   from indentCharacter in indented ? new char?[] { null, ' ', '\t' } : []
                   from indentSize in indented ? new int?[] { null, 0, 1, 2, 3 } : []
                   from newLine in indented ? new string?[] { null, "\n", "\r\n" } : []
                   select CreateOptions(indented, indentCharacter, indentSize, skipValidation, newLine);

            static JsonWriterOptions CreateOptions(bool indented, char? indentCharacter, int? indentSize, bool skipValidation, string? newLine)
            {
                var options = new JsonWriterOptions { Indented = indented, SkipValidation = skipValidation };
                if (indentCharacter is not null) options.IndentCharacter = (char)indentCharacter;
                if (indentSize is not null) options.IndentSize = (int)indentSize;
                if (newLine is not null) options.NewLine = newLine;
                return options;
            }
        }

        private static IEnumerable<object[]> JsonOptionsWith<T>(IEnumerable<T> others) =>
            from options in JsonOptions()
            from inputValue in others
            select new object[] { options, inputValue };

        private static IEnumerable<object[]> JsonOptionsWith<T, U>(IEnumerable<T> others, IEnumerable<U> anothers) =>
            from options in JsonOptions()
            from inputValue in others
            from anotherValue in anothers
            select new object[] { options, inputValue, anotherValue };

    }

    public static class WriterHelpers
    {
        // Normalize comparisons against Json.NET.
        // The following is performed unless skipSpecialRules is true:
        // * Uppercases the \u escaped hex characters.
        // * Escapes forward slash, greater than, and less than.
        // * Ignores ".0" for decimal values.
        public static string NormalizeToJsonNetFormat(this string json, bool skipSpecialRules)
        {
            var sb = new StringBuilder(json.Length);
            int i = 0;
            while (i < json.Length)
            {
                if (!skipSpecialRules)
                {
                    if (json[i] == '\\')
                    {
                        sb.Append(json[i++]);

                        if (i < json.Length - 1 && json[i] == 'u')
                        {
                            sb.Append(json[i++]);

                            if (i < json.Length - 4)
                            {
                                string temp = json.Substring(i, 4).ToLowerInvariant();
                                sb.Append(temp);
                                i += 4;
                            }
                        }
                        if (i < json.Length - 1 && json[i] == '/')
                        {
                            // Convert / to u002f
                            i++;
                            sb.Append("u002f");
                        }
                    }
                    // Convert > to \u003e
                    else if (json[i] == '>')
                    {
                        i++;
                        sb.Append("\\u003e");
                    }
                    // Convert < to \u003c
                    else if (json[i] == '<')
                    {
                        i++;
                        sb.Append("\\u003c");
                    }
                    // Remove .0
                    else if (json[i] == '.' && json[i + 1] == '0')
                    {
                        // Verify that token after .0 is a delimiter.
                        if (json[i + 2] == ',' || json[i + 2] == ']' || json[i + 2] == '}' ||
                            json[i + 2] == ' ' || json[i + 2] == '\r' || json[i + 2] == '\n')
                        {
                            i += 2;
                        }
                        else
                        {
                            sb.Append(json[i++]);
                        }
                    }
                    else
                    {
                        sb.Append(json[i++]);
                    }
                }
                else
                {
                    sb.Append(json[i++]);
                }
            }

            return sb.ToString();
        }

        public static async Task FlushAsync(this Utf8JsonWriter writer, bool useAsync)
        {
            if (useAsync)
            {
                await writer.FlushAsync();
            }
            else
            {
                writer.Flush();
            }
        }

        public static async Task DisposeAsync(this Utf8JsonWriter writer, bool useAsync)
        {
            if (useAsync)
            {
                await writer.DisposeAsync();
            }
            else
            {
                writer.Dispose();
            }
        }
    }
}
