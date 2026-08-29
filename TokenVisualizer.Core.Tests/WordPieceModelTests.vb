Imports System.Collections.Generic
Imports Tokenizers.Models

Namespace TokenVisualizer.Core.Tests

    ''' <summary>
    ''' Tests for the WordPiece model port. Fixtures mirror the semantics of the Rust
    ''' <c>models/wordpiece/mod.rs</c> tokenize algorithm.
    ''' </summary>
    <TestClass>
    Public Class WordPieceModelTests

        <TestMethod>
        Public Sub TestGreedyLongestPrefix()
            Dim vocab As New Dictionary(Of String, Integer)() From {
                {"un", 0}, {"##aff", 1}, {"##able", 2}
            }
            Dim wp As New WordPieceModel(vocab, unkToken:="[UNK]")

            Dim tokens As List(Of Token) = wp.Tokenize("unaffable")
            Assert.HasCount(3, tokens)
            Assert.AreEqual(New Token(0, "un", (0, 2)), tokens(0))
            Assert.AreEqual(New Token(1, "##aff", (2, 5)), tokens(1))
            Assert.AreEqual(New Token(2, "##able", (5, 9)), tokens(2))
        End Sub

        <TestMethod>
        Public Sub TestContinuingPrefixOnlyAfterFirstSubword()
            ' The first subword is emitted unprefixed; later subwords carry the "##" prefix.
            Dim vocab As New Dictionary(Of String, Integer)() From {
                {"hello", 0}, {"##world", 1}, {"world", 2}
            }
            Dim wp As New WordPieceModel(vocab, unkToken:="[UNK]")

            Dim tokens As List(Of Token) = wp.Tokenize("helloworld")
            Assert.HasCount(2, tokens)
            Assert.AreEqual(New Token(0, "hello", (0, 5)), tokens(0))
            Assert.AreEqual(New Token(1, "##world", (5, 10)), tokens(1))
        End Sub

        <TestMethod>
        Public Sub TestMaxInputCharsPerWord_WholeWordUnk()
            Dim vocab As New Dictionary(Of String, Integer)() From {
                {"[UNK]", 0}, {"a", 1}
            }
            Dim wp As New WordPieceModel(vocab, unkToken:="[UNK]", maxInputCharsPerWord:=3)

            Dim tokens As List(Of Token) = wp.Tokenize("abcd")
            Assert.HasCount(1, tokens)
            Assert.AreEqual(New Token(0, "[UNK]", (0, 4)), tokens(0))
        End Sub

        <TestMethod>
        Public Sub TestUnsegmentableChar_WholeWordUnk()
            ' 'c' is not in the vocab and cannot be segmented, so the WHOLE word is replaced.
            Dim vocab As New Dictionary(Of String, Integer)() From {
                {"[UNK]", 0}, {"a", 1}, {"b", 2}
            }
            Dim wp As New WordPieceModel(vocab, unkToken:="[UNK]")

            Dim tokens As List(Of Token) = wp.Tokenize("abc")
            Assert.HasCount(1, tokens)
            Assert.AreEqual(New Token(0, "[UNK]", (0, 3)), tokens(0))
        End Sub

        <TestMethod>
        Public Sub TestOffsetsExcludeContinuingPrefix()
            Dim vocab As New Dictionary(Of String, Integer)() From {
                {"a", 0}, {"##b", 1}
            }
            Dim wp As New WordPieceModel(vocab, unkToken:="[UNK]")

            Dim tokens As List(Of Token) = wp.Tokenize("ab")
            Assert.HasCount(2, tokens)
            Assert.AreEqual(New Token(0, "a", (0, 1)), tokens(0))
            ' The value includes "##", but the offsets cover only the raw "b" bytes.
            Assert.AreEqual(New Token(1, "##b", (1, 2)), tokens(1))
        End Sub

        <TestMethod>
        Public Sub TestMultibyteCharBoundaryShrink_WholeWordUnk()
            ' "é" (2 bytes) cannot be segmented; char-aware shrinking must never split it.
            Dim vocab As New Dictionary(Of String, Integer)() From {
                {"[UNK]", 0}, {"a", 1}
            }
            Dim wp As New WordPieceModel(vocab, unkToken:="[UNK]")

            Dim tokens As List(Of Token) = wp.Tokenize("aé")
            Assert.HasCount(1, tokens)
            Assert.AreEqual(New Token(0, "[UNK]", (0, 3)), tokens(0))
        End Sub

        <TestMethod>
        Public Sub TestMultibyteContinuingSubword()
            ' A multi-byte subword with the prefix: value "##東" (6 chars in .NET? no - 3 UTF-16 units),
            ' offsets cover only the 3 raw bytes.
            Dim vocab As New Dictionary(Of String, Integer)() From {
                {"a", 0}, {"##東", 1}
            }
            Dim wp As New WordPieceModel(vocab, unkToken:="[UNK]")

            Dim tokens As List(Of Token) = wp.Tokenize("a東")
            Assert.HasCount(2, tokens)
            Assert.AreEqual(New Token(0, "a", (0, 1)), tokens(0))
            Assert.AreEqual(New Token(1, "##東", (1, 4)), tokens(1))
        End Sub

        <TestMethod>
        Public Sub TestMissingUnkToken_Throws()
            Dim vocab As New Dictionary(Of String, Integer)() From {
                {"a", 0}
            }
            Dim wp As New WordPieceModel(vocab, unkToken:="[UNK]")
            Assert.ThrowsExactly(Of InvalidOperationException)(Sub() wp.Tokenize("z"))

            ' Exceeding max chars also needs the unk token.
            Dim wp2 As New WordPieceModel(vocab, unkToken:="[UNK]", maxInputCharsPerWord:=1)
            Assert.ThrowsExactly(Of InvalidOperationException)(Sub() wp2.Tokenize("abc"))
        End Sub

        <TestMethod>
        Public Sub TestAccessors()
            Dim vocab As New Dictionary(Of String, Integer)() From {
                {"[UNK]", 0}, {"a", 1}, {"##b", 2}
            }
            Dim wp As New WordPieceModel(vocab, unkToken:="[UNK]")

            Assert.AreEqual(3, wp.VocabSize)
            Assert.AreEqual(1, wp.TokenToId("a"))
            Assert.AreEqual(2, wp.TokenToId("##b"))
            Assert.IsFalse(wp.TokenToId("z").HasValue)
            Assert.AreEqual("a", wp.IdToToken(1))
            Assert.AreEqual("##b", wp.IdToToken(2))
            Assert.IsNull(wp.IdToToken(999))
        End Sub

    End Class

End Namespace
