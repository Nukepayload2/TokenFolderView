Imports System.Collections.Generic
Imports Tokenizers
Imports Tokenizers.Decoders
Imports Tokenizers.Internal
Imports Tokenizers.Models
Imports Tokenizers.Normalizers
Imports Tokenizers.PreTokenizers
Imports Tokenizers.Processors

Namespace TokenVisualizer.Core.Tests

    ''' <summary>
    ''' End-to-end pipeline tests for the <see cref="Tokenizer"/> facade: quicktour-style
    ''' encoding, template processing, padding, truncation, batch, pre-tokenized input,
    ''' decode round-trip, decode stream and the GUI helper methods. Mirrors the Rust
    ''' <c>tests/documentation.rs</c> quicktour and <c>tokenizer/mod.rs</c> unit tests.
    ''' </summary>
    <TestClass>
    Public NotInheritable Class TokenizerTests

        ' ------------------------------------------------------------------
        ' Fixtures
        ' ------------------------------------------------------------------

        ''' <summary>
        ''' A small GPT-2-like fixture: word-level model with BertPreTokenizer and a
        ''' TemplateProcessing that wraps single/pair sequences in [CLS]/[SEP].
        ''' </summary>
        Private Shared Function CreateQuicktour() As Tokenizers.Tokenizer
            Dim vocab As New Dictionary(Of String, Integer) From {
                {"Hello", 0}, {",", 1}, {"y", 2}, {"'", 3}, {"all", 4}, {"!", 5},
                {"How", 6}, {"are", 7}, {"you", 8}, {"[UNK]", 9}, {"?", 10}
            }
            Dim model As New WordLevelModel(vocab, "[UNK]")
            Dim tokenizer As New Tokenizer(model)
            tokenizer.WithPreTokenizer(New BertPreTokenizer())
            tokenizer.AddSpecialTokens(New List(Of AddedToken) From {
                AddedToken.From("[CLS]", True),
                AddedToken.From("[SEP]", True)
            })
            Dim clsId As Integer = tokenizer.TokenToId("[CLS]").Value
            Dim sepId As Integer = tokenizer.TokenToId("[SEP]").Value
            Dim special As New Dictionary(Of String, (List(Of Integer), List(Of String))) From {
                {"[CLS]", (New List(Of Integer) From {clsId}, New List(Of String) From {"[CLS]"})},
                {"[SEP]", (New List(Of Integer) From {sepId}, New List(Of String) From {"[SEP]"})}
            }
            tokenizer.WithPostProcessor(New TemplateProcessing("[CLS] $A [SEP]",
                                                               "[CLS] $A [SEP] $B:1 [SEP]:1",
                                                               special))
            Return tokenizer
        End Function

        ''' <summary>
        ''' A tiny word-level model over "a".."j" + "&lt;unk&gt;" with a WhitespaceSplit
        ''' pre-tokenizer (mirrors the Rust <c>test_tokenizer</c> in mod.rs).
        ''' </summary>
        Private Shared Function CreateTruncation() As Tokenizers.Tokenizer
            Dim vocab As New Dictionary(Of String, Integer)()
            For i As Integer = 0 To 9
                vocab(ChrW(AscW("a"c) + i).ToString()) = i
            Next
            vocab("<unk>") = 10
            Dim model As New WordLevelModel(vocab, "<unk>")
            Dim tokenizer As New Tokenizer(model)
            tokenizer.WithPreTokenizer(New WhitespaceSplitPreTokenizer())
            Return tokenizer
        End Function

        ''' <summary>
        ''' A byte-level BPE over the full GPT-2 byte-to-char table (no merges, no added space),
        ''' used for exact decode round-trips.
        ''' </summary>
        Private Shared Function CreateByteLevel() As Tokenizers.Tokenizer
            Dim byteToChar As IReadOnlyDictionary(Of Byte, Char) = BytesToUnicodeTable.GetBytesToChar()
            Dim vocab As New Dictionary(Of String, Integer)()
            For b As Integer = 0 To 255
                vocab(byteToChar(CByte(b)).ToString()) = b
            Next
            Dim model As New BpeModel(vocab, New List(Of String)())
            Dim tokenizer As New Tokenizer(model)
            tokenizer.WithPreTokenizer(New ByteLevelPreTokenizer(False, True, False))
            tokenizer.WithPostProcessor(New ByteLevelProcessing(False, False, False))
            tokenizer.WithDecoder(New ByteLevelDecoder(False, True, False))
            Return tokenizer
        End Function

        ''' <summary>
        ''' A byte-fallback BPE used by the DecodeStream tests (mirrors the Rust doc example).
        ''' </summary>
        Private Shared Function CreateByteFallback() As Tokenizers.Tokenizer
            Dim vocab As New Dictionary(Of String, Integer) From {
                {"<0x20>", 0}, {"<0xC3>", 1}, {"<0xA9>", 2}, {" This", 3}
            }
            Dim model As New BpeModel(vocab, New List(Of String)(), byteFallback:=True)
            Dim tokenizer As New Tokenizer(model)
            tokenizer.WithDecoder(New ByteFallbackDecoder())
            Return tokenizer
        End Function

        ' ------------------------------------------------------------------
        ' Quicktour-style encode
        ' ------------------------------------------------------------------

        <TestMethod>
        Public Sub Quicktour_Encode_ExactTokensAndOffsets()
            Dim tokenizer As Tokenizer = CreateQuicktour()
            Dim output As Encoding = tokenizer.Encode("Hello, y'all! How are you 😁 ?", True)

            CollectionAssert.AreEqual(
                New List(Of String) From {"[CLS]", "Hello", ",", "y", "'", "all", "!", "How", "are", "you", "[UNK]", "?", "[SEP]"},
                output.Tokens)

            ' The [UNK] (for 😁) sits at index 10; its byte offsets span the 4 UTF-8 bytes of 😁.
            Assert.AreEqual((26, 30), output.Offsets(10))
            Assert.AreEqual(4, output.Offsets(10).Item2 - output.Offsets(10).Item1)
            Assert.AreEqual("[UNK]", output.Tokens(10))

            ' Type ids are all 0 for a single sequence.
            Assert.IsTrue(output.TypeIds.All(Function(t) t = 0))
        End Sub

        <TestMethod>
        Public Sub Quicktour_TemplateProcessing_Pair_TypeIds()
            Dim tokenizer As Tokenizer = CreateQuicktour()
            Dim output As Encoding = tokenizer.EncodePair("Hello, y'all!", "How are you 😁 ?", True)

            CollectionAssert.AreEqual(
                New List(Of String) From {"[CLS]", "Hello", ",", "y", "'", "all", "!", "[SEP]", "How", "are", "you", "[UNK]", "?", "[SEP]"},
                output.Tokens)
            CollectionAssert.AreEqual(
                New List(Of Integer) From {0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1},
                output.TypeIds)
        End Sub

        <TestMethod>
        Public Sub Quicktour_Padding_AttentionMask()
            Dim tokenizer As Tokenizer = CreateQuicktour()
            Dim pad As New PaddingParams()
            pad.PadId = 3
            pad.PadToken = "[PAD]"
            tokenizer.SetPadding(pad)

            Dim texts As String() = {"Hello, y'all!", "How are you 😁 ?"}
            Dim batch As List(Of Encoding) = tokenizer.EncodeBatch(texts, True)
            Assert.AreEqual(8, batch(0).Length)
            CollectionAssert.AreEqual(
                New List(Of String) From {"[CLS]", "How", "are", "you", "[UNK]", "?", "[SEP]", "[PAD]"},
                batch(1).Tokens)
            CollectionAssert.AreEqual(
                New List(Of Integer) From {1, 1, 1, 1, 1, 1, 1, 0},
                batch(1).AttentionMask)
        End Sub

        <TestMethod>
        Public Sub Quicktour_Batch_EqualsPerItem()
            Dim tokenizer As Tokenizer = CreateQuicktour()
            Dim texts As String() = {"Hello, y'all!", "How are you 😁 ?"}
            Dim batch As List(Of Encoding) = tokenizer.EncodeBatch(texts, False)
            Assert.HasCount(2, batch)
            For i As Integer = 0 To batch.Count - 1
                Dim singleEnc As Encoding = tokenizer.Encode(If(i = 0, texts(0), texts(1)), False)
                CollectionAssert.AreEqual(singleEnc.Ids, batch(i).Ids)
                CollectionAssert.AreEqual(singleEnc.Tokens, batch(i).Tokens)
            Next
        End Sub

        <TestMethod>
        Public Sub TokenToId_FindsAddedSpecialTokens()
            Dim tokenizer As Tokenizer = CreateQuicktour()
            Assert.IsTrue(tokenizer.TokenToId("[SEP]").HasValue)
            Assert.IsTrue(tokenizer.TokenToId("[CLS]").HasValue)
            Assert.AreEqual(11, tokenizer.GetVocabSize(False))
            Assert.AreEqual(13, tokenizer.GetVocabSize(True))
        End Sub

        ' ------------------------------------------------------------------
        ' Truncation
        ' ------------------------------------------------------------------

        <TestMethod>
        Public Sub RightTruncation_MatchesFullPrefix()
            Dim tokenizer As Tokenizer = CreateTruncation()
            Dim full As Encoding = tokenizer.Encode("a b c d e f g h i j", False)
            CollectionAssert.AreEqual(New List(Of Integer) From {0, 1, 2, 3, 4, 5, 6, 7, 8, 9}, full.Ids)

            tokenizer.SetTruncation(3, 0, TruncationStrategy.LongestFirst, TruncationDirection.Right)
            Dim truncated As Encoding = tokenizer.Encode("a b c d e f g h i j", False)
            CollectionAssert.AreEqual(New List(Of Integer) From {0, 1, 2}, truncated.Ids)
        End Sub

        <TestMethod>
        Public Sub LeftTruncation_KeepsTail()
            Dim tokenizer As Tokenizer = CreateTruncation()
            tokenizer.SetTruncation(3, 0, TruncationStrategy.LongestFirst, TruncationDirection.Left)
            Dim truncated As Encoding = tokenizer.Encode("a b c d e f g h i j", False)
            CollectionAssert.AreEqual(New List(Of Integer) From {7, 8, 9}, truncated.Ids)
        End Sub

        <TestMethod>
        Public Sub PairRightTruncation_LongestFirst_ExactVector()
            ' Ported from the Rust pair_right_truncation_longest_first test.
            Dim tokenizer As Tokenizer = CreateTruncation()
            tokenizer.SetTruncation(6, 0, TruncationStrategy.LongestFirst, TruncationDirection.Right)
            Dim truncated As Encoding = tokenizer.EncodePair("a b c d e f g h i j", "a b c d e", False)
            CollectionAssert.AreEqual(New List(Of Integer) From {0, 1, 2, 0, 1, 2}, truncated.Ids)
        End Sub

        <TestMethod>
        Public Sub PairOnlySecond_DoesNotTruncateFirst()
            Dim tokenizer As Tokenizer = CreateTruncation()
            tokenizer.SetTruncation(8, 0, TruncationStrategy.OnlySecond, TruncationDirection.Right)
            Dim truncated As Encoding = tokenizer.EncodePair("a b c d e", "a b c d e f g h i j", False)
            Assert.AreEqual(8, truncated.Length)
            CollectionAssert.AreEqual(New List(Of Integer) From {0, 1, 2, 3, 4}, truncated.Ids.GetRange(0, 5))
        End Sub

        ' ------------------------------------------------------------------
        ' Pre-tokenized input
        ' ------------------------------------------------------------------

        <TestMethod>
        Public Sub EncodePretokenized_SetsWordIds()
            Dim vocab As New Dictionary(Of String, Integer) From {
                {"a", 0}, {"b", 1}, {"c", 2}, {"<unk>", 3}
            }
            Dim tokenizer As New Tokenizer(New WordLevelModel(vocab, "<unk>"))
            Dim output As Encoding = tokenizer.EncodePretokenized(New List(Of String) From {"a", "b", "c"}, False)
            CollectionAssert.AreEqual(New List(Of Integer) From {0, 1, 2}, output.Ids)
            ' Every token's word id is the index of its input element.
            CollectionAssert.AreEqual(New List(Of Integer?) From {0, 1, 2}, output.Words)
        End Sub

        <TestMethod>
        Public Sub EncodePretokenized_SingleElement()
            Dim vocab As New Dictionary(Of String, Integer) From {
                {"a", 0}, {"<unk>", 1}
            }
            Dim tokenizer As New Tokenizer(New WordLevelModel(vocab, "<unk>"))
            Dim output As Encoding = tokenizer.EncodePretokenized(New List(Of String) From {"a"}, False)
            CollectionAssert.AreEqual(New List(Of Integer) From {0}, output.Ids)
        End Sub

        ' ------------------------------------------------------------------
        ' Decode
        ' ------------------------------------------------------------------

        <TestMethod>
        Public Sub Decode_RoundTrip_ByteLevel()
            Dim tokenizer As Tokenizer = CreateByteLevel()
            Dim output As Encoding = tokenizer.Encode("hello world", False)
            Assert.AreEqual("hello world", tokenizer.Decode(output.Ids, True))
            Assert.AreEqual("hello world", tokenizer.Decode(output.Ids, False))
        End Sub

        <TestMethod>
        Public Sub Decode_SkipsSpecialTokens()
            Dim tokenizer As Tokenizer = CreateQuicktour()
            Dim output As Encoding = tokenizer.Encode("Hello, y'all!", True)
            Dim decoded As String = tokenizer.Decode(output.Ids, True)
            Assert.DoesNotContain("[CLS]", decoded)
            Assert.DoesNotContain("[SEP]", decoded)
            Assert.Contains("Hello", decoded)
        End Sub

        <TestMethod>
        Public Sub DecodeBatch_MatchesPerItem()
            Dim tokenizer As Tokenizer = CreateByteLevel()
            Dim enc1 As Encoding = tokenizer.Encode("one two", False)
            Dim enc2 As Encoding = tokenizer.Encode("three", False)
            Dim batch As List(Of String) = tokenizer.DecodeBatch(New List(Of List(Of Integer)) From {enc1.Ids, enc2.Ids}, True)
            CollectionAssert.AreEqual(New List(Of String) From {"one two", "three"}, batch)
        End Sub

        <TestMethod>
        Public Sub DecodeStream_ByteFallback_Incremental()
            ' Ported from the Rust DecodeStream doc example.
            Dim tokenizer As Tokenizer = CreateByteFallback()
            Dim stream As StreamDecoder = tokenizer.DecodeStream(False)

            ' Single byte 0x20 is valid UTF-8.
            Assert.AreEqual(" ", stream.Step(0))
            ' 0xC3 alone is an incomplete sequence -> no progress.
            Assert.IsNull(stream.Step(1))
            ' 0xC3 0xA9 completes to é.
            Assert.AreEqual("é", stream.Step(2))
        End Sub

        <TestMethod>
        Public Sub DecodeStream_MatchesFullDecode()
            Dim tokenizer As Tokenizer = CreateByteLevel()
            Dim ids As New List(Of Integer) From {104, 101, 108, 108, 111, 32, 119, 111, 114, 108, 100}
            Dim expected As String = tokenizer.Decode(ids, True)

            Dim stream As StreamDecoder = tokenizer.DecodeStream(True)
            Dim builder As New Global.System.Text.StringBuilder()
            For Each id As Integer In ids
                Dim chunk As String = stream.Step(id)
                If chunk IsNot Nothing Then builder.Append(chunk)
            Next
            Assert.AreEqual(expected, builder.ToString())
        End Sub

        ' ------------------------------------------------------------------
        ' GUI additions
        ' ------------------------------------------------------------------

        <TestMethod>
        Public Sub EncodeCount_MatchesEncodeLength()
            Dim tokenizer As Tokenizer = CreateTruncation()
            Assert.AreEqual(tokenizer.Encode("a b c d e", False).Length, tokenizer.EncodeCount("a b c d e", False))
            Assert.AreEqual(3, tokenizer.EncodeCount("a b c", False))
        End Sub

        <TestMethod>
        Public Sub EncodeWithSpans_SlicesOriginalText()
            Dim tokenizer As Tokenizer = CreateQuicktour()
            Dim text As String = "Hello, y'all!"
            Dim spans As List(Of (Integer, Integer, Integer)) = tokenizer.EncodeWithSpans(text)

            ' For this all-ASCII sentence the char offsets match the byte offsets.
            Assert.HasCount(6, spans)
            Assert.AreEqual((0, 0, 5), spans(0))
            Assert.AreEqual((1, 5, 6), spans(1))
            Assert.AreEqual((2, 7, 8), spans(2))
            Assert.AreEqual((3, 8, 9), spans(3))
            Assert.AreEqual((4, 9, 12), spans(4))
            Assert.AreEqual((5, 12, 13), spans(5))

            ' Every non-empty span slices the original text to a real token.
            For Each span As (Integer, Integer, Integer) In spans
                If span.Item3 > span.Item2 Then
                    Assert.AreEqual(text.Substring(span.Item2, span.Item3 - span.Item2).Length, span.Item3 - span.Item2)
                End If
            Next
        End Sub

        <TestMethod>
        Public Sub EncodeWithSpans_SlicesSupplementaryCharsAsUtf16()
            ' Regression: char offsets are scalar-based; Substring needs UTF-16 boundaries.
            ' "😁" (U+1F601) is a surrogate pair => 2 UTF-16 code units but 1 scalar.
            Dim tokenizer As Tokenizer = CreateQuicktour()
            Dim text As String = "a 😁 c"
            Dim spans As List(Of (Integer, Integer, Integer)) = tokenizer.EncodeWithSpans(text)

            Dim concat As New Text.StringBuilder()
            For Each span As (Integer, Integer, Integer) In spans
                If span.Item3 > span.Item2 Then
                    concat.Append(text.Substring(span.Item2, span.Item3 - span.Item2))
                End If
            Next

            ' The reconstructed token spans must cover every non-space scalar exactly once.
            Assert.AreEqual("a😁c", concat.ToString())
            ' And the emoji slice must be the emoji itself (not a lone surrogate).
            Dim emojiSpan As (Integer, Integer, Integer) = spans.First(Function(s) s.Item3 - s.Item2 >= 2)
            Assert.AreEqual("😁", text.Substring(emojiSpan.Item2, emojiSpan.Item3 - emojiSpan.Item2))
        End Sub

        <TestMethod>
        Public Sub EncodeWithSpans_WordLevelWholeWord()
            Dim tokenizer As Tokenizer = CreateTruncation()
            Dim spans As List(Of (Integer, Integer, Integer)) = tokenizer.EncodeWithSpans("a b c")
            Assert.HasCount(3, spans)
            Assert.AreEqual((0, 0, 1), spans(0))
            Assert.AreEqual((1, 2, 3), spans(1))
            Assert.AreEqual((2, 4, 5), spans(2))
        End Sub

    End Class

End Namespace
