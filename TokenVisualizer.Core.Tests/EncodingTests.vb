Imports Tokenizers.Internal

Namespace TokenVisualizer.Core.Tests

    ''' <summary>
    ''' Ports the Rust <c>tokenizer/encoding.rs</c> unit tests: merge, truncate (incl. stride,
    ''' empty and left), the offset/word/char mappings, and padding.
    ''' </summary>
    <TestClass>
    Public Class EncodingTests

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

        <TestMethod>
        Public Sub MergeEncodings_GrowingOffsetsShifts()
            Dim a As Encoding = MakeEncoding(
                {1}, {0}, {"Hello "}, {0}, {(0, 6)}, {0}, {1})
            Dim b As Encoding = MakeEncoding(
                {2}, {1}, {"World!"}, {0}, {(0, 6)}, {0}, {1})

            a.MergeWith(b, True)

            Dim expected As Encoding = MakeEncoding(
                {1, 2}, {0, 1}, {"Hello ", "World!"}, {0, 0}, {(0, 6), (6, 12)}, {0, 0}, {1, 1})
            AssertEncodingEquals(expected, a)
        End Sub

        <TestMethod>
        Public Sub MergeEncodings_NotGrowingOffsets()
            Dim a As Encoding = MakeEncoding({1}, {0}, {"Hello "}, {0}, {(0, 6)}, {0}, {1})
            Dim b As Encoding = MakeEncoding({2}, {1}, {"World!"}, {0}, {(0, 6)}, {0}, {1})

            a.MergeWith(b, False)

            Dim expected As Encoding = MakeEncoding(
                {1, 2}, {0, 1}, {"Hello ", "World!"}, {0, 0}, {(0, 6), (0, 6)}, {0, 0}, {1, 1})
            AssertEncodingEquals(expected, a)
        End Sub

        <TestMethod>
        Public Sub Truncate_Right()
            Dim a As Encoding = MakeEncoding(
                {1, 2, 3}, {0, 0, 0}, {"Hello", "World", "!"}, {0, 1, 2}, {(0, 5), (6, 11), (11, 12)}, {0, 0, 0}, {1, 1, 1})

            a.Truncate(2, 0, TruncationDirection.Right)

            Dim expected As Encoding = MakeEncoding(
                {1, 2}, {0, 0}, {"Hello", "World"}, {0, 1}, {(0, 5), (6, 11)}, {0, 0}, {1, 1})
            expected.Overflowing.Add(MakeEncoding(
                {3}, {0}, {"!"}, {2}, {(11, 12)}, {0}, {1}))
            AssertEncodingEquals(expected, a)
        End Sub

        <TestMethod>
        Public Sub TruncateToEmpty()
            Dim a As Encoding = MakeEncoding(
                {1, 2, 3}, {0, 0, 0}, {"Hello", "World", "!"}, {0, 1, 2}, {(0, 5), (6, 11), (11, 12)}, {0, 0, 0}, {1, 1, 1})

            a.Truncate(0, 0, TruncationDirection.Right)

            Dim expected As Encoding = MakeEncoding(
                {}, {}, {}, {}, {}, {}, {})
            expected.Overflowing.Add(MakeEncoding(
                {1, 2, 3}, {0, 0, 0}, {"Hello", "World", "!"}, {0, 1, 2}, {(0, 5), (6, 11), (11, 12)}, {0, 0, 0}, {1, 1, 1}))
            AssertEncodingEquals(expected, a)
        End Sub

        <TestMethod>
        Public Sub TruncateOverflowWithStride()
            Dim a As Encoding = MakeEncoding(
                {1, 2, 3, 4, 5}, {0, 0, 0, 0, 0}, {"42", "is", "the", "answer", "!"}, {0, 1, 2, 3, 4}, {(0, 2), (2, 4), (4, 7), (7, 13), (13, 14)}, {0, 0, 0, 0, 0}, {1, 1, 1, 1, 1})

            a.Truncate(4, 2, TruncationDirection.Right)

            Dim expected As Encoding = MakeEncoding(
                {1, 2, 3, 4}, {0, 0, 0, 0}, {"42", "is", "the", "answer"}, {0, 1, 2, 3}, {(0, 2), (2, 4), (4, 7), (7, 13)}, {0, 0, 0, 0}, {1, 1, 1, 1})
            expected.Overflowing.Add(MakeEncoding(
                {3, 4, 5}, {0, 0, 0}, {"the", "answer", "!"}, {2, 3, 4}, {(4, 7), (7, 13), (13, 14)}, {0, 0, 0}, {1, 1, 1}))
            AssertEncodingEquals(expected, a)
        End Sub

        <TestMethod>
        Public Sub Truncate_Left()
            Dim a As Encoding = MakeEncoding(
                {1, 2, 3}, {0, 0, 0}, {"Hello", "World", "!"}, {0, 1, 2}, {(0, 5), (6, 11), (11, 12)}, {0, 0, 0}, {1, 1, 1})

            a.Truncate(2, 0, TruncationDirection.Left)

            Dim expected As Encoding = MakeEncoding(
                {2, 3}, {0, 0}, {"World", "!"}, {1, 2}, {(6, 11), (11, 12)}, {0, 0}, {1, 1})
            expected.Overflowing.Add(MakeEncoding(
                {1}, {0}, {"Hello"}, {0}, {(0, 5)}, {0}, {1}))
            AssertEncodingEquals(expected, a)
        End Sub

        <TestMethod>
        Public Sub Mappings_WordToTokensAndChars()
            Dim e As New Encoding()
            e.Ids = New List(Of Integer)()
            For i As Integer = 0 To 10
                e.Ids.Add(0)
            Next
            e.Tokens = New List(Of String) From {
                "He", "llo", "won", "der", "ful", "friend", "!",
                "How", "are", "you", "?"}
            e.Offsets = New List(Of (Integer, Integer)) From {
                (0, 2), (2, 5), (7, 10), (10, 13), (13, 16), (17, 23), (23, 24),
                (0, 3), (4, 7), (8, 11), (11, 12)}
            e.Words = New List(Of Integer?) From {
                0, 0, 1, 1, 1, 2, 3,
                0, 1, 2, 3}
            e.SequenceRanges = New Dictionary(Of Integer, (Integer, Integer)) From {
                {0, (0, 7)},
                {1, (7, 11)}}

            Assert.AreEqual((0, 2), e.WordToTokens(0, 0))
            Assert.AreEqual((2, 5), e.WordToTokens(1, 0))
            Assert.AreEqual((5, 6), e.WordToTokens(2, 0))
            Assert.AreEqual((6, 7), e.WordToTokens(3, 0))
            Assert.AreEqual((7, 8), e.WordToTokens(0, 1))
            Assert.AreEqual((8, 9), e.WordToTokens(1, 1))
            Assert.AreEqual((9, 10), e.WordToTokens(2, 1))
            Assert.AreEqual((10, 11), e.WordToTokens(3, 1))

            Assert.AreEqual((0, 5), e.WordToChars(0, 0))
            Assert.AreEqual((7, 16), e.WordToChars(1, 0))
            Assert.AreEqual((0, 3), e.WordToChars(0, 1))
            Assert.AreEqual((4, 7), e.WordToChars(1, 1))

            Assert.AreEqual((0, (0, 2)), e.TokenToChars(0))
            Assert.AreEqual((0, (2, 5)), e.TokenToChars(1))
            Assert.AreEqual((1, (0, 3)), e.TokenToChars(7))
            Assert.AreEqual((1, (8, 11)), e.TokenToChars(9))

            Assert.AreEqual((0, 0), e.TokenToWord(1))
            Assert.AreEqual((0, 1), e.TokenToWord(2))
            Assert.AreEqual((1, 0), e.TokenToWord(7))
            Assert.AreEqual((1, 2), e.TokenToWord(9))
            Assert.IsNull(e.TokenToWord(11))

            Assert.AreEqual(1, e.CharToToken(3, 0))
            Assert.AreEqual(2, e.CharToToken(8, 0))
            Assert.IsNull(e.CharToToken(16, 0))
            Assert.AreEqual(6, e.CharToToken(23, 0))
            Assert.AreEqual(7, e.CharToToken(2, 1))
            Assert.AreEqual(9, e.CharToToken(9, 1))

            Assert.AreEqual(0, e.CharToWord(3, 0))
            Assert.AreEqual(1, e.CharToWord(8, 0))
            Assert.IsNull(e.CharToWord(16, 0))
            Assert.AreEqual(3, e.CharToWord(23, 0))
            Assert.AreEqual(0, e.CharToWord(2, 1))
            Assert.AreEqual(2, e.CharToWord(9, 1))
        End Sub

        <TestMethod>
        Public Sub Padding_LeftShiftsSequenceRanges()
            Dim a As New Encoding()
            a.Ids = New List(Of Integer) From {1}
            a.TypeIds = New List(Of Integer) From {0}
            a.Tokens = New List(Of String) From {"Hello "}
            a.Words = New List(Of Integer?) From {0}
            a.Offsets = New List(Of (Integer, Integer)) From {(0, 6)}
            a.SpecialTokensMask = New List(Of Integer) From {0}
            a.AttentionMask = New List(Of Integer) From {1}
            a.SequenceRanges = New Dictionary(Of Integer, (Integer, Integer)) From {{0, (0, 1)}}

            a.Pad(2, 99, 0, "[PAD]", PaddingDirection.Left)

            Assert.HasCount(1, a.SequenceRanges)
            Assert.AreEqual((1, 2), a.SequenceRanges(0))
        End Sub

    End Class

End Namespace
