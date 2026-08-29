Imports Tokenizers.Decoders
Imports Tokenizers.PreTokenizers

Namespace TokenVisualizer.Core.Tests

    ''' <summary>
    ''' Ports of the Rust decoder unit tests (tokenizers/src/decoders/*.rs and the decoder
    ''' halves of pre_tokenizers/byte_level.rs, pre_tokenizers/metaspace.rs, normalizers/replace.rs).
    ''' </summary>
    <TestClass>
    Public Class DecodersTests

        ' ------------------------------------------------------------------
        ' BPE (port of decoders/bpe.rs)
        ' ------------------------------------------------------------------

        <TestMethod>
        Public Sub BpeDecoder_Decode()
            Dim decoder As IDecoder = New BpeDecoder()
            Assert.AreEqual("hello world", decoder.Decode(New String() {"hello</w>", "world</w>"}))
        End Sub

        <TestMethod>
        Public Sub BpeDecoder_DecodeChain_SpacesBetween_NoneAfterLast()
            Dim decoder As New BpeDecoder()
            CollectionAssert.AreEqual(
                New String() {"hello ", "world"},
                decoder.DecodeChain(New String() {"hello</w>", "world</w>"}))
        End Sub

        ' ------------------------------------------------------------------
        ' ByteFallback (port of decoders/byte_fallback.rs)
        ' ------------------------------------------------------------------

        <TestMethod>
        Public Sub ByteFallbackDecoder_Decode()
            Dim decoder As New ByteFallbackDecoder()

            CollectionAssert.AreEqual(
                New String() {"Hey", "friend!"},
                decoder.DecodeChain(New String() {"Hey", "friend!"}))

            ' <0x61> -> a
            CollectionAssert.AreEqual(
                New String() {"a"},
                decoder.DecodeChain(New String() {"<0x61>"}))

            ' Lone 0xE5 is invalid UTF-8 -> one replacement char
            CollectionAssert.AreEqual(
                New String() {"�"},
                decoder.DecodeChain(New String() {"<0xE5>"}))

            ' 0xE5 0x8F is truncated -> one replacement char per byte
            CollectionAssert.AreEqual(
                New String() {"�", "�"},
                decoder.DecodeChain(New String() {"<0xE5>", "<0x8F>"}))

            ' 0xE5 0x8F 0xAB is valid UTF-8 for 叫
            CollectionAssert.AreEqual(
                New String() {"叫"},
                decoder.DecodeChain(New String() {"<0xE5>", "<0x8F>", "<0xAB>"}))

            ' Valid run flushed before a non-byte token
            CollectionAssert.AreEqual(
                New String() {"叫", "a"},
                decoder.DecodeChain(New String() {"<0xE5>", "<0x8F>", "<0xAB>", "a"}))

            ' Invalid run flushed before a non-byte token
            CollectionAssert.AreEqual(
                New String() {"�", "�", "a"},
                decoder.DecodeChain(New String() {"<0xE5>", "<0x8F>", "a"}))

            ' Flush at end
            CollectionAssert.AreEqual(
                New String() {"叫"},
                decoder.DecodeChain(New String() {"<0xE5>", "<0x8F>", "<0xAB>"}))
        End Sub

        ' ------------------------------------------------------------------
        ' ByteLevel (port of pre_tokenizers/byte_level.rs decoder tests)
        ' ------------------------------------------------------------------

        <TestMethod>
        Public Sub ByteLevelDecoder_Decode_RoundTrip()
            Dim decoder As IDecoder = New ByteLevelDecoder()
            Assert.AreEqual(" Hello my", decoder.Decode(New String() {"ĠHello", "Ġmy"}))
        End Sub

        <TestMethod>
        Public Sub ByteLevelDecoder_Decoding()
            Dim decoder As New ByteLevelDecoder()
            CollectionAssert.AreEqual(
                New String() {"Hello my friend, how is your day going?"},
                decoder.DecodeChain(New String() {
                    "Hello", "Ġmy", "Ġfriend", ",", "Ġhow", "Ġis", "Ġyour", "Ġday", "Ġgoing", "?"
                }))
        End Sub

        <TestMethod>
        Public Sub ByteLevelDecoder_Decode_UnknownCharacters()
            ' "[PA D]" contains a plain space (U+0020) which is not in the byte table, so the
            ' whole token falls back to its raw UTF-8 bytes.
            Dim decoder As New ByteLevelDecoder()
            CollectionAssert.AreEqual(
                New String() {"Hello there dear friend! [PA D]"},
                decoder.DecodeChain(New String() {"Hello", "Ġthere", "Ġdear", "Ġfriend!", "Ġ", "[PA D]"}))
        End Sub

        <TestMethod>
        Public Sub ByteLevelDecoder_Decode_WorksOnSeparatedTokens()
            Dim sample As String = "A Nuskhuri abbreviation of იესუ ქრისტე ( iesu kriste ) "" Jesus Christ """
            Dim separated As New List(Of String)()
            For Each p In TestHelpers.ByteLevelTransform(sample)
                separated.Add(p.Item1)
            Next
            Dim decoder As IDecoder = New ByteLevelDecoder()
            Assert.AreEqual(sample, decoder.Decode(separated))
        End Sub

        ' ------------------------------------------------------------------
        ' CTC (port of decoders/ctc.rs)
        ' ------------------------------------------------------------------

        <TestMethod>
        Public Sub CtcDecoder_HandmadeSample()
            Dim decoder As New CtcDecoder()
            Dim tokens As String() = "<pad> <pad> h e e l l <pad> l o o o <pad>".Split(" "c)
            CollectionAssert.AreEqual(
                New String() {"h", "e", "l", "l", "o"},
                decoder.DecodeChain(tokens))
        End Sub

        <TestMethod>
        Public Sub CtcDecoder_HandmadeWithDelimiterSample()
            Dim decoder As New CtcDecoder()
            Dim tokens As String() =
                "<pad> <pad> h e e l l <pad> l o o o <pad> <pad> | <pad> w o o o r <pad> <pad> l l d <pad> <pad> <pad> <pad>".Split(" "c)
            CollectionAssert.AreEqual(
                New String() {"h", "e", "l", "l", "o", " ", "w", "o", "r", "l", "d"},
                decoder.DecodeChain(tokens))
        End Sub

        <TestMethod>
        Public Sub CtcDecoder_LibrispeechStyleSample()
            Dim decoder As IDecoder = New CtcDecoder()
            Dim tokens As String() =
                "<pad> <pad> <pad> T T <pad> H H E E <pad> | | <pad> <pad> U U N N <pad> I I V V E E R R S S E E <pad> <pad> <pad>".Split(" "c)
            Assert.AreEqual("THE UNIVERSE", decoder.Decode(tokens))
        End Sub

        ' ------------------------------------------------------------------
        ' Fuse (port of decoders/fuse.rs)
        ' ------------------------------------------------------------------

        <TestMethod>
        Public Sub FuseDecoder_Decode()
            Dim decoder As IDecoder = New FuseDecoder()
            Assert.AreEqual("Hey friend!", decoder.Decode(New String() {"Hey", " friend!"}))
        End Sub

        ' ------------------------------------------------------------------
        ' Metaspace (port of pre_tokenizers/metaspace.rs decoder)
        ' ------------------------------------------------------------------

        <TestMethod>
        Public Sub MetaspaceDecoder_Always_DropsLeadingReplacement()
            Dim decoder As New MetaspaceDecoder()
            CollectionAssert.AreEqual(
                New String() {"Hey", " friend!"},
                decoder.DecodeChain(New String() {"▁Hey", "▁friend!"}))
        End Sub

        <TestMethod>
        Public Sub MetaspaceDecoder_Never_KeepsLeadingReplacementAsSpace()
            Dim decoder As New MetaspaceDecoder("▁"c, PrependScheme.Never)
            CollectionAssert.AreEqual(
                New String() {" Hey", " friend!"},
                decoder.DecodeChain(New String() {"▁Hey", "▁friend!"}))
        End Sub

        ' ------------------------------------------------------------------
        ' Replace (port of normalizers/replace.rs decoder)
        ' ------------------------------------------------------------------

        <TestMethod>
        Public Sub ReplaceDecoder_Decode()
            Dim decoder As New ReplaceDecoder("String", "_", " ")
            CollectionAssert.AreEqual(
                New String() {"hello", " hello"},
                decoder.DecodeChain(New String() {"hello", "_hello"}))
        End Sub

        ' ------------------------------------------------------------------
        ' Sequence (port of decoders/sequence.rs)
        ' ------------------------------------------------------------------

        <TestMethod>
        Public Sub SequenceDecoder_Decode()
            Dim decoders As New List(Of IDecoder) From {
                New CtcDecoder(),
                New MetaspaceDecoder()
            }
            Dim decoder As IDecoder = New DecoderSequence(decoders)
            Assert.AreEqual(
                "Hi you",
                decoder.Decode(New String() {"▁", "▁", "H", "H", "i", "i", "▁", "y", "o", "u"}))
        End Sub

        ' ------------------------------------------------------------------
        ' Strip (port of decoders/strip.rs)
        ' ------------------------------------------------------------------

        <TestMethod>
        Public Sub StripDecoder_Decode()
            Dim decoder As New StripDecoder("H"c, 1, 0)
            CollectionAssert.AreEqual(
                New String() {"ey", " friend!", "HH"},
                decoder.DecodeChain(New String() {"Hey", " friend!", "HHH"}))

            Dim decoder2 As New StripDecoder("y"c, 0, 1)
            CollectionAssert.AreEqual(
                New String() {"He", " friend!"},
                decoder2.DecodeChain(New String() {"Hey", " friend!"}))
        End Sub

        ' ------------------------------------------------------------------
        ' WordPiece (port of decoders/wordpiece.rs)
        ' ------------------------------------------------------------------

        <TestMethod>
        Public Sub WordPieceDecoder_Decode_NoCleanup()
            Dim decoder As IDecoder = New WordPieceDecoder("##", False)
            Assert.AreEqual(
                "##uelo Araújo Noguera",
                decoder.Decode(New String() {"##uelo", "Ara", "##új", "##o", "No", "##guera"}))
        End Sub

        <TestMethod>
        Public Sub WordPieceDecoder_Decode_Cleanup()
            Dim decoder As IDecoder = New WordPieceDecoder("##", True)
            Assert.AreEqual("helloworld!", decoder.Decode(New String() {"hello", "##world", "##!"}))
            ' " n't" -> "n't" after the continuation space is prepended.
            Assert.AreEqual("don't", decoder.Decode(New String() {"do", "n't"}))
        End Sub

        <TestMethod>
        Public Sub WordPieceDecoder_CleanupTable()
            Assert.AreEqual("I don't know.", WordPieceDecoder.Cleanup("I do not know ."))
            Assert.AreEqual("Hello, world!", WordPieceDecoder.Cleanup("Hello , world !"))
            Assert.AreEqual("It's John's car.", WordPieceDecoder.Cleanup("It ' s John ' s car ."))
            Assert.AreEqual("I'm fine.", WordPieceDecoder.Cleanup("I ' m fine ."))
            Assert.AreEqual("You're going.", WordPieceDecoder.Cleanup("You ' re going ."))
            Assert.AreEqual("I've got.", WordPieceDecoder.Cleanup("I ' ve got ."))
        End Sub

    End Class

End Namespace
