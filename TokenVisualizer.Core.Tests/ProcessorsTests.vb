Imports Tokenizers.Internal
Imports Tokenizers.Models
Imports Tokenizers.Processors

Namespace TokenVisualizer.Core.Tests

    ''' <summary>
    ''' Ports the Rust processor unit tests: Bert, Roberta, ByteLevel (offset trimming), Template
    ''' processing (incl. overflowing recombination) and Sequence (process chain).
    ''' </summary>
    <TestClass>
    Public Class ProcessorsTests

        ''' <summary>Compares every field of two encodings, including the overflowing list and sequence ranges.</summary>
        Private Shared Sub AssertEncodingEquals(expected As Encoding, actual As Encoding)
            CollectionAssert.AreEqual(expected.Ids, actual.Ids, "Ids")
            CollectionAssert.AreEqual(expected.TypeIds, actual.TypeIds, "TypeIds")
            CollectionAssert.AreEqual(expected.Tokens, actual.Tokens, "Tokens")
            CollectionAssert.AreEqual(expected.Words, actual.Words, "Words")
            CollectionAssert.AreEqual(expected.Offsets, actual.Offsets, "Offsets")
            CollectionAssert.AreEqual(expected.SpecialTokensMask, actual.SpecialTokensMask, "SpecialTokensMask")
            CollectionAssert.AreEqual(expected.AttentionMask, actual.AttentionMask, "AttentionMask")
            Assert.HasCount(expected.SequenceRanges.Count, actual.SequenceRanges, "SequenceRanges count")
            For Each kv In expected.SequenceRanges
                Assert.IsTrue(actual.SequenceRanges.ContainsKey(kv.Key), $"missing sequence range {kv.Key}")
                Assert.AreEqual(kv.Value, actual.SequenceRanges(kv.Key), $"sequence range {kv.Key}")
            Next
            Assert.HasCount(expected.Overflowing.Count, actual.Overflowing, "Overflowing count")
            For i As Integer = 0 To expected.Overflowing.Count - 1
                AssertEncodingEquals(expected.Overflowing(i), actual.Overflowing(i))
            Next
        End Sub

        Private Shared Function MakeEncoding(ids As Integer(), typeIds As Integer(), tokens As String(), words As Integer?(), offsets As (Integer, Integer)(), special As Integer(), attention As Integer()) As Encoding
            Dim e As New Encoding()
            e.Ids = ids.ToList()
            e.TypeIds = typeIds.ToList()
            e.Tokens = tokens.ToList()
            e.Words = words.ToList()
            e.Offsets = offsets.ToList()
            e.SpecialTokensMask = special.ToList()
            e.AttentionMask = attention.ToList()
            Return e
        End Function

        Private Shared Function MakeHelloEncoding() As Encoding
            Return Encoding.FromTokens(
                New List(Of Token) From {
                    New Token(12, "Hello", (0, 5)),
                    New Token(14, "there", (6, 11))
                },
                0)
        End Function

        Private Shared Function MakePairEncoding() As Encoding
            Return Encoding.FromTokens(
                New List(Of Token) From {New Token(15, "pair", (0, 4))},
                0)
        End Function

        ' ------------------------------------------------------------------
        ' Bert
        ' ------------------------------------------------------------------

        <TestMethod>
        Public Sub Bert_BertProcessing()
            Dim processor As New BertProcessing()
            Assert.AreEqual(2, processor.GetAddedTokens(False))
            Assert.AreEqual(3, processor.GetAddedTokens(True))

            Dim encoding As Encoding = MakeHelloEncoding()
            Dim pair As Encoding = MakePairEncoding()

            Dim singleEncoding As Encoding = processor.Process(encoding.Clone(), Nothing, True)
            Dim expectedSingle As Encoding = MakeEncoding(
                {101, 12, 14, 102}, {0, 0, 0, 0},
                {"[CLS]", "Hello", "there", "[SEP]"}, {Nothing, Nothing, Nothing, Nothing},
                {(0, 0), (0, 5), (6, 11), (0, 0)}, {1, 0, 0, 1}, {1, 1, 1, 1})
            expectedSingle.SequenceRanges(0) = (1, 3)
            AssertEncodingEquals(expectedSingle, singleEncoding)
            Assert.IsTrue(singleEncoding.TokenToSequence(2).HasValue AndAlso singleEncoding.TokenToSequence(2).Value = 0)
            Assert.IsFalse(singleEncoding.TokenToSequence(3).HasValue)

            Dim pairEncoding As Encoding = processor.Process(encoding.Clone(), pair.Clone(), True)
            Dim expectedPair As Encoding = MakeEncoding(
                {101, 12, 14, 102, 15, 102}, {0, 0, 0, 0, 1, 1},
                {"[CLS]", "Hello", "there", "[SEP]", "pair", "[SEP]"}, {Nothing, Nothing, Nothing, Nothing, Nothing, Nothing},
                {(0, 0), (0, 5), (6, 11), (0, 0), (0, 4), (0, 0)}, {1, 0, 0, 1, 0, 1}, {1, 1, 1, 1, 1, 1})
            expectedPair.SequenceRanges(0) = (1, 3)
            expectedPair.SequenceRanges(1) = (4, 5)
            AssertEncodingEquals(expectedPair, pairEncoding)
            Assert.IsTrue(pairEncoding.TokenToSequence(2).HasValue AndAlso pairEncoding.TokenToSequence(2).Value = 0)
            Assert.IsFalse(pairEncoding.TokenToSequence(3).HasValue)
            Assert.IsTrue(pairEncoding.TokenToSequence(4).HasValue AndAlso pairEncoding.TokenToSequence(4).Value = 1)
            Assert.IsFalse(pairEncoding.TokenToSequence(5).HasValue)

            ' No special tokens
            Dim noSpecial As Encoding = processor.Process(encoding, pair, False)
            Dim expectedNoSpecial As Encoding = MakeEncoding(
                {12, 14, 15}, {0, 0, 1},
                {"Hello", "there", "pair"}, {Nothing, Nothing, Nothing},
                {(0, 5), (6, 11), (0, 4)}, {0, 0, 0}, {1, 1, 1})
            expectedNoSpecial.SequenceRanges(0) = (0, 2)
            expectedNoSpecial.SequenceRanges(1) = (2, 3)
            AssertEncodingEquals(expectedNoSpecial, noSpecial)
            Assert.AreEqual(0, noSpecial.TokenToSequence(0))
            Assert.AreEqual(0, noSpecial.TokenToSequence(1))
            Assert.AreEqual(1, noSpecial.TokenToSequence(2))
        End Sub

        ' ------------------------------------------------------------------
        ' Roberta
        ' ------------------------------------------------------------------

        <TestMethod>
        Public Sub Roberta_RobertaProcessing()
            Dim processor As New RobertaProcessing()
            Assert.AreEqual(2, processor.GetAddedTokens(False))
            Assert.AreEqual(4, processor.GetAddedTokens(True))

            Dim encoding As Encoding = MakeHelloEncoding()
            Dim pair As Encoding = MakePairEncoding()

            Dim singleEncoding As Encoding = processor.Process(encoding.Clone(), Nothing, True)
            Dim expectedSingle As Encoding = MakeEncoding(
                {0, 12, 14, 2}, {0, 0, 0, 0},
                {"<s>", "Hello", "there", "</s>"}, {Nothing, Nothing, Nothing, Nothing},
                {(0, 0), (0, 5), (6, 11), (0, 0)}, {1, 0, 0, 1}, {1, 1, 1, 1})
            expectedSingle.SequenceRanges(0) = (1, 3)
            AssertEncodingEquals(expectedSingle, singleEncoding)
            Assert.AreEqual(0, singleEncoding.TokenToSequence(2))
            Assert.IsNull(singleEncoding.TokenToSequence(3))

            Dim pairEncoding As Encoding = processor.Process(encoding.Clone(), pair.Clone(), True)
            Dim expectedPair As Encoding = MakeEncoding(
                {0, 12, 14, 2, 2, 15, 2}, {0, 0, 0, 0, 0, 0, 0},
                {"<s>", "Hello", "there", "</s>", "</s>", "pair", "</s>"}, {Nothing, Nothing, Nothing, Nothing, Nothing, Nothing, Nothing},
                {(0, 0), (0, 5), (6, 11), (0, 0), (0, 0), (0, 4), (0, 0)}, {1, 0, 0, 1, 1, 0, 1}, {1, 1, 1, 1, 1, 1, 1})
            expectedPair.SequenceRanges(0) = (1, 3)
            expectedPair.SequenceRanges(1) = (5, 6)
            AssertEncodingEquals(expectedPair, pairEncoding)
            Assert.AreEqual(0, pairEncoding.TokenToSequence(2))
            Assert.IsNull(pairEncoding.TokenToSequence(3))
            Assert.IsNull(pairEncoding.TokenToSequence(4))
            Assert.AreEqual(1, pairEncoding.TokenToSequence(5))
            Assert.IsNull(pairEncoding.TokenToSequence(6))

            ' No special tokens
            Dim noSpecial As Encoding = processor.Process(encoding, pair, False)
            Dim expectedNoSpecial As Encoding = MakeEncoding(
                {12, 14, 15}, {0, 0, 0},
                {"Hello", "there", "pair"}, {Nothing, Nothing, Nothing},
                {(0, 5), (6, 11), (0, 4)}, {0, 0, 0}, {1, 1, 1})
            expectedNoSpecial.SequenceRanges(0) = (0, 2)
            expectedNoSpecial.SequenceRanges(1) = (2, 3)
            AssertEncodingEquals(expectedNoSpecial, noSpecial)
            Assert.AreEqual(0, noSpecial.TokenToSequence(0))
            Assert.AreEqual(0, noSpecial.TokenToSequence(1))
            Assert.AreEqual(1, noSpecial.TokenToSequence(2))
        End Sub

        ' ------------------------------------------------------------------
        ' ByteLevel
        ' ------------------------------------------------------------------

        <TestMethod>
        Public Sub ByteLevel_ProcessorTrimsOffsets()
            Dim start As Encoding = MakeEncoding(
                {0, 0, 0, 0, 0}, {}, {"Ġ", "ĠĠĠĠHelloĠĠ", "ĠĠHello", "HelloĠĠ", "ĠĠĠĠ"}, {},
                {(0, 1), (0, 11), (11, 18), (18, 25), (25, 29)}, {}, {})

            Dim bytelevel As New ByteLevelProcessing()   ' add_prefix_space=true, trim_offsets=true, use_regex=true

            Dim expected As Encoding = MakeEncoding(
                {0, 0, 0, 0, 0}, {0, 0, 0, 0, 0}, {"Ġ", "ĠĠĠĠHelloĠĠ", "ĠĠHello", "HelloĠĠ", "ĠĠĠĠ"}, {},
                {(0, 0), (4, 9), (13, 18), (18, 23), (29, 29)}, {}, {})
            expected.SequenceRanges(0) = (0, 5)
            AssertEncodingEquals(expected, bytelevel.Process(start.Clone(), Nothing, False))

            Dim pairExpected As Encoding = MakeEncoding(
                {0, 0, 0, 0, 0, 0, 0, 0, 0, 0}, {0, 0, 0, 0, 0, 1, 1, 1, 1, 1},
                {"Ġ", "ĠĠĠĠHelloĠĠ", "ĠĠHello", "HelloĠĠ", "ĠĠĠĠ", "Ġ", "ĠĠĠĠHelloĠĠ", "ĠĠHello", "HelloĠĠ", "ĠĠĠĠ"}, {},
                {(0, 0), (4, 9), (13, 18), (18, 23), (29, 29), (0, 0), (4, 9), (13, 18), (18, 23), (29, 29)}, {}, {})
            pairExpected.SequenceRanges(0) = (0, 5)
            pairExpected.SequenceRanges(1) = (5, 10)
            AssertEncodingEquals(pairExpected, bytelevel.Process(start.Clone(), start.Clone(), False))
        End Sub

        <TestMethod>
        Public Sub ByteLevel_ProcessorTrimsOffsetsPreTokenized()
            ' If user uses `is_pretokenized=True` we might have offsets that begin at the start
            ' of the string but are NOT the first token: offsets.0 == 0 must not strip the single
            ' leading space glyph.
            Dim encoding As Encoding = MakeEncoding(
                {0, 0, 0, 0, 0}, {}, {"Ġl", "ove", "Ġl", "ove"}, {},
                {(0, 1), (1, 4), (0, 1), (1, 4)}, {}, {})
            ByteLevelProcessing.ProcessOffsets(encoding, True)

            Dim expected As Encoding = MakeEncoding(
                {0, 0, 0, 0, 0}, {}, {"Ġl", "ove", "Ġl", "ove"}, {},
                {(0, 1), (1, 4), (0, 1), (1, 4)}, {}, {})
            AssertEncodingEquals(expected, encoding)
        End Sub

        ' ------------------------------------------------------------------
        ' Template
        ' ------------------------------------------------------------------

        Private Shared Function GetBertTemplate() As TemplateProcessing
            Dim specialTokens As New Dictionary(Of String, (List(Of Integer), List(Of String)))()
            specialTokens("[CLS]") = (New List(Of Integer) From {1}, New List(Of String) From {"[CLS]"})
            specialTokens("[SEP]") = (New List(Of Integer) From {0}, New List(Of String) From {"[SEP]"})
            Return New TemplateProcessing("[CLS] $0 [SEP]", "[CLS]:0 $A:0 [SEP]:0 $B:1 [SEP]:1", specialTokens)
        End Function

        <TestMethod>
        Public Sub Template_PieceParsing()
            Dim noTokens As New Dictionary(Of String, (List(Of Integer), List(Of String)))()
            Dim pair As String = "$A:0 $B:1"

            Dim p1 As TemplatePiece = TemplateProcessing.ParsePiece("$")
            Assert.IsTrue(p1.IsSequence AndAlso p1.SequenceId = "A"c AndAlso p1.TypeId = 0)

            Dim p2 As TemplatePiece = TemplateProcessing.ParsePiece("$B")
            Assert.IsTrue(p2.IsSequence AndAlso p2.SequenceId = "B"c AndAlso p2.TypeId = 0)

            Dim p3 As TemplatePiece = TemplateProcessing.ParsePiece("$1")
            Assert.IsTrue(p3.IsSequence AndAlso p3.SequenceId = "A"c AndAlso p3.TypeId = 1)

            Dim p4 As TemplatePiece = TemplateProcessing.ParsePiece("$B:2")
            Assert.IsTrue(p4.IsSequence AndAlso p4.SequenceId = "B"c AndAlso p4.TypeId = 2)

            Dim p5 As TemplatePiece = TemplateProcessing.ParsePiece("$:1")
            Assert.IsTrue(p5.IsSequence AndAlso p5.SequenceId = "A"c AndAlso p5.TypeId = 1)

            Dim p6 As TemplatePiece = TemplateProcessing.ParsePiece("[CLS]:0")
            Assert.IsFalse(p6.IsSequence)
            Assert.AreEqual("[CLS]", p6.TokenId)
            Assert.AreEqual(0, p6.TypeId)

            Assert.ThrowsExactly(Of ArgumentException)(
                Sub() TemplateProcessing.ParsePiece("$C:1"))
            Assert.ThrowsExactly(Of ArgumentException)(
                Sub() TemplateProcessing.ParsePiece("$A:"))

            ' Building validates that pair uses both sequences.
            Dim valid As New TemplateProcessing("$1", pair, noTokens)
            Assert.AreEqual(0, valid.GetAddedTokens(False))
            Assert.AreEqual(0, valid.GetAddedTokens(True))
        End Sub

        <TestMethod>
        Public Sub Template_TemplateProcessing()
            Dim processor As TemplateProcessing = GetBertTemplate()
            Assert.AreEqual(2, processor.GetAddedTokens(False))
            Assert.AreEqual(3, processor.GetAddedTokens(True))

            Dim encoding As Encoding = MakeHelloEncoding()
            Dim pair As Encoding = MakePairEncoding()

            Dim singleEncoding As Encoding = processor.Process(encoding.Clone(), Nothing, True)
            Dim expectedSingle As Encoding = MakeEncoding(
                {1, 12, 14, 0}, {0, 0, 0, 0},
                {"[CLS]", "Hello", "there", "[SEP]"}, {Nothing, Nothing, Nothing, Nothing},
                {(0, 0), (0, 5), (6, 11), (0, 0)}, {1, 0, 0, 1}, {1, 1, 1, 1})
            expectedSingle.SequenceRanges(0) = (1, 3)
            AssertEncodingEquals(expectedSingle, singleEncoding)
            Assert.AreEqual(0, singleEncoding.TokenToSequence(2))
            Assert.IsNull(singleEncoding.TokenToSequence(3))

            Dim pairEncoding As Encoding = processor.Process(encoding, pair, True)
            Dim expectedPair As Encoding = MakeEncoding(
                {1, 12, 14, 0, 15, 0}, {0, 0, 0, 0, 1, 1},
                {"[CLS]", "Hello", "there", "[SEP]", "pair", "[SEP]"}, {Nothing, Nothing, Nothing, Nothing, Nothing, Nothing},
                {(0, 0), (0, 5), (6, 11), (0, 0), (0, 4), (0, 0)}, {1, 0, 0, 1, 0, 1}, {1, 1, 1, 1, 1, 1})
            expectedPair.SequenceRanges(0) = (1, 3)
            expectedPair.SequenceRanges(1) = (4, 5)
            AssertEncodingEquals(expectedPair, pairEncoding)
            Assert.AreEqual(0, pairEncoding.TokenToSequence(2))
            Assert.IsNull(pairEncoding.TokenToSequence(3))
            Assert.AreEqual(1, pairEncoding.TokenToSequence(4))
            Assert.IsNull(pairEncoding.TokenToSequence(5))
        End Sub

        <TestMethod>
        Public Sub Template_TemplateProcessingOverflowing()
            Dim processor As TemplateProcessing = GetBertTemplate()

            Dim encoding As Encoding = MakeHelloEncoding()
            Dim overflowing As Encoding = Encoding.FromTokens(
                New List(Of Token) From {New Token(13, "you", (12, 15))}, 0)
            encoding.Overflowing.Add(overflowing)

            Dim pair As Encoding = Encoding.FromTokens(
                New List(Of Token) From {
                    New Token(15, "pair", (0, 4)),
                    New Token(16, "with", (5, 9))
                }, 0)
            Dim pairOverflowing As Encoding = Encoding.FromTokens(
                New List(Of Token) From {New Token(17, "info", (10, 14))}, 0)
            pair.Overflowing.Add(pairOverflowing)

            ' Single sequence.
            Dim singleEncoding As Encoding = processor.Process(encoding.Clone(), Nothing, True)
            Dim expectedSingle As Encoding = MakeEncoding(
                {1, 12, 14, 0}, {0, 0, 0, 0},
                {"[CLS]", "Hello", "there", "[SEP]"}, {Nothing, Nothing, Nothing, Nothing},
                {(0, 0), (0, 5), (6, 11), (0, 0)}, {1, 0, 0, 1}, {1, 1, 1, 1})
            expectedSingle.SequenceRanges(0) = (1, 3)
            Dim expectedSingleOverflow As Encoding = MakeEncoding(
                {1, 13, 0}, {0, 0, 0},
                {"[CLS]", "you", "[SEP]"}, {Nothing, Nothing, Nothing},
                {(0, 0), (12, 15), (0, 0)}, {1, 0, 1}, {1, 1, 1})
            expectedSingleOverflow.SequenceRanges(0) = (1, 2)
            expectedSingle.Overflowing.Add(expectedSingleOverflow)
            AssertEncodingEquals(expectedSingle, singleEncoding)

            ' Pair sequence, with the nested overflowing recombination.
            Dim pairEncoding As Encoding = processor.Process(encoding, pair, True)
            Dim expectedPair As Encoding = MakeEncoding(
                {1, 12, 14, 0, 15, 16, 0}, {0, 0, 0, 0, 1, 1, 1},
                {"[CLS]", "Hello", "there", "[SEP]", "pair", "with", "[SEP]"},
                {Nothing, Nothing, Nothing, Nothing, Nothing, Nothing, Nothing},
                {(0, 0), (0, 5), (6, 11), (0, 0), (0, 4), (5, 9), (0, 0)},
                {1, 0, 0, 1, 0, 0, 1}, {1, 1, 1, 1, 1, 1, 1})
            expectedPair.SequenceRanges(0) = (1, 3)
            expectedPair.SequenceRanges(1) = (4, 6)

            ' Overflowing 1: first seq overflowing + whole pair, with its own nested overflowing.
            Dim expP1 As Encoding = MakeEncoding(
                {1, 13, 0, 15, 16, 0}, {0, 0, 0, 1, 1, 1},
                {"[CLS]", "you", "[SEP]", "pair", "with", "[SEP]"},
                {Nothing, Nothing, Nothing, Nothing, Nothing, Nothing},
                {(0, 0), (12, 15), (0, 0), (0, 4), (5, 9), (0, 0)},
                {1, 0, 1, 0, 0, 1}, {1, 1, 1, 1, 1, 1})
            expP1.SequenceRanges(0) = (1, 2)
            expP1.SequenceRanges(1) = (3, 5)
            Dim expP1Nested As Encoding = MakeEncoding(
                {1, 13, 0, 17, 0}, {0, 0, 0, 0, 1},
                {"[CLS]", "you", "[SEP]", "info", "[SEP]"},
                {Nothing, Nothing, Nothing, Nothing, Nothing},
                {(0, 0), (12, 15), (0, 0), (10, 14), (0, 0)},
                {1, 0, 1, 0, 1}, {1, 1, 1, 1, 1})
            expP1Nested.SequenceRanges(0) = (1, 2)
            expP1Nested.SequenceRanges(1) = (3, 4)
            expP1.Overflowing.Add(expP1Nested)
            expectedPair.Overflowing.Add(expP1)

            ' Overflowing 2: first seq overflowing + second seq overflowing.
            Dim expP2 As Encoding = MakeEncoding(
                {1, 13, 0, 17, 0}, {0, 0, 0, 0, 1},
                {"[CLS]", "you", "[SEP]", "info", "[SEP]"},
                {Nothing, Nothing, Nothing, Nothing, Nothing},
                {(0, 0), (12, 15), (0, 0), (10, 14), (0, 0)},
                {1, 0, 1, 0, 1}, {1, 1, 1, 1, 1})
            expP2.SequenceRanges(0) = (1, 2)
            expP2.SequenceRanges(1) = (3, 4)
            expectedPair.Overflowing.Add(expP2)

            ' Overflowing 3: first seq + second seq overflowing.
            Dim expP3 As Encoding = MakeEncoding(
                {1, 12, 14, 0, 17, 0}, {0, 0, 0, 0, 0, 1},
                {"[CLS]", "Hello", "there", "[SEP]", "info", "[SEP]"},
                {Nothing, Nothing, Nothing, Nothing, Nothing, Nothing},
                {(0, 0), (0, 5), (6, 11), (0, 0), (10, 14), (0, 0)},
                {1, 0, 0, 1, 0, 1}, {1, 1, 1, 1, 1, 1})
            expP3.SequenceRanges(0) = (1, 3)
            expP3.SequenceRanges(1) = (4, 5)
            Dim expP3Nested As Encoding = MakeEncoding(
                {1, 13, 0, 17, 0}, {0, 0, 0, 0, 1},
                {"[CLS]", "you", "[SEP]", "info", "[SEP]"},
                {Nothing, Nothing, Nothing, Nothing, Nothing},
                {(0, 0), (12, 15), (0, 0), (10, 14), (0, 0)},
                {1, 0, 1, 0, 1}, {1, 1, 1, 1, 1})
            expP3Nested.SequenceRanges(0) = (1, 2)
            expP3Nested.SequenceRanges(1) = (3, 4)
            expP3.Overflowing.Add(expP3Nested)
            expectedPair.Overflowing.Add(expP3)

            AssertEncodingEquals(expectedPair, pairEncoding)
            Assert.AreEqual(0, pairEncoding.TokenToSequence(2))
            Assert.IsNull(pairEncoding.TokenToSequence(3))
            Assert.AreEqual(1, pairEncoding.TokenToSequence(4))
            Assert.AreEqual(1, pairEncoding.TokenToSequence(5))
            Assert.IsNull(pairEncoding.TokenToSequence(6))
        End Sub

        <TestMethod>
        Public Sub Template_MissingSpecialTokens()
            Dim noTokens As New Dictionary(Of String, (List(Of Integer), List(Of String)))()
            Assert.ThrowsExactly(Of ArgumentException)(
                Sub()
                    Dim p As New TemplateProcessing("[CLS] $0 [SEP]", "[CLS] $A:0 [SEP] $B:1 [SEP]", noTokens)
                End Sub)
        End Sub

        <TestMethod>
        Public Sub Template_PairMustUseBothSequences()
            Dim noTokens As New Dictionary(Of String, (List(Of Integer), List(Of String)))()
            Assert.ThrowsExactly(Of ArgumentException)(
                Sub()
                    Dim p As New TemplateProcessing("$0", "$0 $1", noTokens)
                End Sub)
        End Sub

        ' ------------------------------------------------------------------
        ' Sequence
        ' ------------------------------------------------------------------

        <TestMethod>
        Public Sub Sequence_ProcessChain()
            Dim start As Encoding = MakeEncoding(
                {0, 0, 0, 0, 0}, {0, 0, 0, 0, 0},
                {"Ġ", "ĠĠĠĠHelloĠĠ", "ĠĠHello", "HelloĠĠ", "ĠĠĠĠ"}, {},
                {(0, 1), (0, 11), (11, 18), (18, 25), (25, 29)}, {}, {})

            Dim bytelevel As New ByteLevelProcessing(True, True, True)
            Dim sequence As New ProcessorSequence(New List(Of IPostProcessor) From {bytelevel})

            Dim expected As Encoding = MakeEncoding(
                {0, 0, 0, 0, 0}, {0, 0, 0, 0, 0},
                {"Ġ", "ĠĠĠĠHelloĠĠ", "ĠĠHello", "HelloĠĠ", "ĠĠĠĠ"}, {},
                {(0, 0), (4, 9), (13, 18), (18, 23), (29, 29)}, {}, {})
            expected.SequenceRanges(0) = (0, 5)

            AssertEncodingEquals(expected, bytelevel.Process(start.Clone(), Nothing, False))
            AssertEncodingEquals(expected, sequence.Process(start.Clone(), Nothing, False))

            Dim pairExpected As Encoding = MakeEncoding(
                {0, 0, 0, 0, 0, 0, 0, 0, 0, 0}, {0, 0, 0, 0, 0, 1, 1, 1, 1, 1},
                {"Ġ", "ĠĠĠĠHelloĠĠ", "ĠĠHello", "HelloĠĠ", "ĠĠĠĠ", "Ġ", "ĠĠĠĠHelloĠĠ", "ĠĠHello", "HelloĠĠ", "ĠĠĠĠ"}, {},
                {(0, 0), (4, 9), (13, 18), (18, 23), (29, 29), (0, 0), (4, 9), (13, 18), (18, 23), (29, 29)}, {}, {})
            pairExpected.SequenceRanges(0) = (0, 5)
            pairExpected.SequenceRanges(1) = (5, 10)

            AssertEncodingEquals(pairExpected, bytelevel.Process(start.Clone(), start.Clone(), False))
            AssertEncodingEquals(pairExpected, sequence.Process(start.Clone(), start.Clone(), False))
        End Sub

    End Class

End Namespace
