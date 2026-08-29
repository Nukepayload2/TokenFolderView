Imports Tokenizers.Internal
Imports Tokenizers.Models

Namespace TokenVisualizer.Core.Tests

    ''' <summary>
    ''' Tests for the P7 <c>PreTokenizedString</c> extension methods: <c>Tokenize</c>,
    ''' <c>TokenizeWithLimit</c> and <c>IntoEncoding</c> (Byte/Char/None offsets, word ids =
    ''' split index).
    ''' </summary>
    <TestClass>
    Public Class PreTokenizedStringIntoTests

        Private Shared Function MakePreTokenizedSequence() As PreTokenizedString
            Dim pts As New PreTokenizedString()
            pts.Original = "My name is Anthonino"
            pts.Splits = New List(Of Split) From {
                New Split(NormalizedString.FromString("My"), New List(Of Token) From {New Token(0, "My", (0, 2))}),
                New Split(NormalizedString.FromString("name"), New List(Of Token) From {New Token(1, "name", (0, 4))}),
                New Split(NormalizedString.FromString("is"), New List(Of Token) From {New Token(2, "is", (0, 2))}),
                New Split(NormalizedString.FromString("Anthonino"), New List(Of Token) From {
                    New Token(3, "Anth", (0, 4)),
                    New Token(4, "on", (4, 6)),
                    New Token(5, "ino", (6, 9))})}
            Return pts
        End Function

        <TestMethod>
        Public Sub IntoEncoding_ByteOffsetsAndWordIds()
            Dim pts As PreTokenizedString = MakePreTokenizedSequence()
            Dim enc As Encoding = pts.IntoEncoding(Nothing, 0, OffsetType.Byte)

            ' Offsets are relative to each pre-tokenized word (offsets.rs byte_level_pre_tokenized_sequence).
            CollectionAssert.AreEqual(
                New (Integer, Integer)() {(0, 2), (0, 4), (0, 2), (0, 4), (4, 6), (6, 9)},
                enc.Offsets)
            ' Word ids are the split index: the multi-token word "Anthonino" shares id 3.
            CollectionAssert.AreEqual(
                New Integer?() {0, 1, 2, 3, 3, 3},
                enc.Words)
            CollectionAssert.AreEqual(
                New String() {"My", "name", "is", "Anth", "on", "ino"},
                enc.Tokens)
            CollectionAssert.AreEqual(
                New Integer() {0, 1, 2, 3, 4, 5},
                enc.Ids)
        End Sub

        <TestMethod>
        Public Sub IntoEncoding_WordIdxOverride()
            Dim pts As PreTokenizedString = MakePreTokenizedSequence()
            Dim enc As Encoding = pts.IntoEncoding(5, 0, OffsetType.Byte)

            CollectionAssert.AreEqual(
                New Integer?() {5, 5, 5, 5, 5, 5},
                enc.Words)
            CollectionAssert.AreEqual(
                New Integer() {0, 0, 0, 0, 0, 0},
                enc.TypeIds)
        End Sub

        <TestMethod>
        Public Sub IntoEncoding_None()
            Dim pts As PreTokenizedString = MakePreTokenizedSequence()
            Dim enc As Encoding = pts.IntoEncoding(Nothing, 0, OffsetType.None)

            CollectionAssert.AreEqual(New Integer() {0, 1, 2, 3, 4, 5}, enc.Ids)
            CollectionAssert.AreEqual(New String() {"", "", "", "", "", ""}, enc.Tokens)
            CollectionAssert.AreEqual(New (Integer, Integer)() {(0, 0), (0, 0), (0, 0), (0, 0), (0, 0), (0, 0)}, enc.Offsets)
            CollectionAssert.AreEqual(New Integer?() {Nothing, Nothing, Nothing, Nothing, Nothing, Nothing}, enc.Words)
            CollectionAssert.AreEqual(New Integer() {0, 0, 0, 0, 0, 0}, enc.TypeIds)
        End Sub

        <TestMethod>
        Public Sub IntoEncoding_CharOffsets()
            Dim pts As New PreTokenizedString()
            pts.Original = "héllo"
            pts.Splits = New List(Of Split) From {
                New Split(NormalizedString.FromString("héllo"), New List(Of Token) From {
                    New Token(0, "hé", (0, 3)),
                    New Token(1, "llo", (3, 6))})}

            Dim enc As Encoding = pts.IntoEncoding(Nothing, 0, OffsetType.Char)

            ' Byte offsets (0,3)/(3,6) map to char offsets (0,2)/(2,5).
            CollectionAssert.AreEqual(
                New (Integer, Integer)() {(0, 2), (2, 5)},
                enc.Offsets)
            CollectionAssert.AreEqual(New String() {"hé", "llo"}, enc.Tokens)
        End Sub

        <TestMethod>
        Public Sub IntoEncoding_UntokenizedSplitThrows()
            Dim pts As PreTokenizedString = PreTokenizedString.FromString("Hello world")
            Assert.ThrowsExactly(Of InvalidOperationException)(
                Sub()
                    pts.IntoEncoding(Nothing, 0, OffsetType.Byte)
                End Sub)
        End Sub

        <TestMethod>
        Public Sub Tokenize_AttachesTokensToUntokenizedSplits()
            Dim pts As PreTokenizedString = PreTokenizedString.FromString("Hello world")
            pts.SplitBy(New StringPattern(" "), SplitDelimiterBehavior.Removed)
            pts.Tokenize(Function(n) New List(Of Token) From {New Token(0, n.Get, (0, 0))})

            Assert.HasCount(2, pts.Splits)
            Assert.IsNotNull(pts.Splits(0).Tokens)
            Assert.IsNotNull(pts.Splits(1).Tokens)
            Assert.HasCount(1, pts.Splits(0).Tokens)
            Assert.AreEqual("Hello", pts.Splits(0).Tokens(0).Value)
            Assert.AreEqual("world", pts.Splits(1).Tokens(0).Value)
        End Sub

        Private Shared Function SplitEachInto(tokensPerSplit As Integer) As Func(Of NormalizedString, List(Of Token))
            Return Function(n)
                       Dim result As New List(Of Token)()
                       For k As Integer = 0 To tokensPerSplit - 1
                           result.Add(New Token(k, n.Get, (0, 0)))
                       Next
                       Return result
                   End Function
        End Function

        <TestMethod>
        Public Sub TokenizeWithLimit_RightTruncatesTrailing()
            ' Four splits, each producing 2 tokens; max 5 -> keep the first 3 splits.
            Dim pts As PreTokenizedString = PreTokenizedString.FromString("a b c d")
            pts.SplitBy(New StringPattern(" "), SplitDelimiterBehavior.Removed)
            pts.TokenizeWithLimit(SplitEachInto(2), 5, TruncationDirection.Right)

            Assert.HasCount(3, pts.Splits)
            Assert.AreEqual("a", pts.Splits(0).Normalized.Get)
            Assert.AreEqual("b", pts.Splits(1).Normalized.Get)
            Assert.AreEqual("c", pts.Splits(2).Normalized.Get)
            For Each s In pts.Splits
                Assert.IsNotNull(s.Tokens)
                Assert.HasCount(2, s.Tokens)
            Next
        End Sub

        <TestMethod>
        Public Sub TokenizeWithLimit_LeftDrainsLeading()
            ' Four splits, each producing 2 tokens; max 5 -> drop the first split.
            Dim pts As PreTokenizedString = PreTokenizedString.FromString("a b c d")
            pts.SplitBy(New StringPattern(" "), SplitDelimiterBehavior.Removed)
            pts.TokenizeWithLimit(SplitEachInto(2), 5, TruncationDirection.Left)

            Assert.HasCount(3, pts.Splits)
            Assert.AreEqual("b", pts.Splits(0).Normalized.Get)
            Assert.AreEqual("c", pts.Splits(1).Normalized.Get)
            Assert.AreEqual("d", pts.Splits(2).Normalized.Get)
            For Each s In pts.Splits
                Assert.IsNotNull(s.Tokens)
                Assert.HasCount(2, s.Tokens)
            Next
        End Sub

        <TestMethod>
        Public Sub TokenizeWithLimit_RightRespectsExistingTokens()
            ' First split already carries 2 tokens (id 9, must NOT be re-tokenized); the count
            ' includes them, so the loop stops after the second split.
            Dim pts As PreTokenizedString = PreTokenizedString.FromString("a b c")
            pts.SplitBy(New StringPattern(" "), SplitDelimiterBehavior.Removed)
            pts.Splits(0).Tokens = New List(Of Token) From {New Token(9, "a", (0, 0)), New Token(9, "a", (0, 0))}
            pts.TokenizeWithLimit(SplitEachInto(2), 3, TruncationDirection.Right)

            Assert.HasCount(2, pts.Splits)
            Assert.AreEqual("a", pts.Splits(0).Normalized.Get)
            Assert.AreEqual("b", pts.Splits(1).Normalized.Get)
            ' Existing tokens preserved (not re-tokenized).
            Assert.HasCount(2, pts.Splits(0).Tokens)
            Assert.AreEqual(9, pts.Splits(0).Tokens(0).Id)
            ' Second split tokenized normally.
            Assert.HasCount(2, pts.Splits(1).Tokens)
            Assert.AreEqual(0, pts.Splits(1).Tokens(0).Id)
        End Sub

    End Class

End Namespace
