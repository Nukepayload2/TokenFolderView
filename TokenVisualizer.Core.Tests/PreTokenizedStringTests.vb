Imports System.Text
Imports Tokenizers.Internal
Imports Tokenizers.Models

Namespace TokenVisualizer.Core.Tests

    ''' <summary>
    ''' Tests for <see cref="PreTokenizedString"/>: empty pieces are dropped, token-carrying
    ''' splits pass through, and splits compose in order.
    ''' </summary>
    <TestClass>
    Public Class PreTokenizedStringTests

        <TestMethod>
        Public Sub Split_DropsEmptyPieces()
            ' An empty normalized string yields an empty split that must be dropped.
            Dim pts As PreTokenizedString = PreTokenizedString.FromString("")
            pts.SplitBy(New StringPattern("a"), SplitDelimiterBehavior.Removed)
            Assert.HasCount(0, pts.Splits)
        End Sub

        <TestMethod>
        Public Sub Split_DropsEmptyPiecesAfterRemoval()
            ' A split whose entire content is removed leaves no pieces.
            Dim pts As PreTokenizedString = PreTokenizedString.FromString("   ")
            pts.SplitBy(New PredicatePattern(Function(r As Rune) Rune.IsWhiteSpace(r)), SplitDelimiterBehavior.Removed)
            Assert.HasCount(0, pts.Splits)
        End Sub

        <TestMethod>
        Public Sub Split_TokenCarryingSplitsPassThrough()
            Dim pts As PreTokenizedString = PreTokenizedString.FromString("Hello world")
            pts.SplitBy(New StringPattern(" "), SplitDelimiterBehavior.Removed)
            Assert.HasCount(2, pts.Splits)
            Assert.AreEqual("Hello", pts.Splits(0).Normalized.Get)
            Assert.AreEqual("world", pts.Splits(1).Normalized.Get)

            ' Attach tokens to the second split; a later split must not touch it.
            pts.Splits(1).Tokens = New List(Of Token) From {New Token(0, "world", (6, 11))}
            pts.SplitBy(New PredicatePattern(Function(r As Rune) Rune.IsWhiteSpace(r)), SplitDelimiterBehavior.Removed)

            Assert.HasCount(2, pts.Splits)
            Assert.AreEqual("Hello", pts.Splits(0).Normalized.Get)
            Assert.IsNull(pts.Splits(0).Tokens, "untokenized split stays untokenized")
            Assert.IsNotNull(pts.Splits(1).Tokens, "token-carrying split passes through unchanged")
            Assert.AreEqual("world", pts.Splits(1).Normalized.Get)
            Assert.HasCount(1, pts.Splits(1).Tokens)
        End Sub

        <TestMethod>
        Public Sub Normalize_OnlyAppliesToUntokenizedSplits()
            Dim pts As PreTokenizedString = PreTokenizedString.FromString("Hello")
            pts.Splits(0).Tokens = New List(Of Token) From {New Token(0, "Hello", (0, 5))}
            Dim called As Integer = 0
            pts.Normalize(Sub(n) called += 1)
            Assert.AreEqual(0, called, "normalizer must not run on token-carrying splits")

            Dim pts2 As PreTokenizedString = PreTokenizedString.FromString("Hello")
            pts2.Normalize(Sub(n) n.Lowercase())
            Assert.AreEqual("hello", pts2.Splits(0).Normalized.Get)
        End Sub

        <TestMethod>
        Public Sub SplitsComposeInOrder()
            ' Two sequential splits on the same PreTokenizedString behave like a Sequence.
            Dim pts As PreTokenizedString = PreTokenizedString.FromString("Hey, man!")
            pts.SplitBy(New PredicatePattern(Function(r As Rune) Rune.IsWhiteSpace(r)), SplitDelimiterBehavior.Removed)
            pts.SplitBy(New PredicatePattern(Function(r As Rune) Rune.IsPunctuation(r)), SplitDelimiterBehavior.Isolated)

            Assert.HasCount(4, pts.Splits)
            Assert.AreEqual("Hey", pts.Splits(0).Normalized.Get)
            Assert.AreEqual(",", pts.Splits(1).Normalized.Get)
            Assert.AreEqual("man", pts.Splits(2).Normalized.Get)
            Assert.AreEqual("!", pts.Splits(3).Normalized.Get)
        End Sub

    End Class

End Namespace
