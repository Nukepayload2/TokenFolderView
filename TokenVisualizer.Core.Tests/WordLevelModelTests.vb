Imports System.Collections.Generic
Imports Tokenizers.Models

Namespace TokenVisualizer.Core.Tests

    ''' <summary>
    ''' Tests for the WordLevel model port, mirroring the Rust <c>models/wordlevel/mod.rs</c>
    ''' unit tests.
    ''' </summary>
    <TestClass>
    Public Class WordLevelModelTests

        <TestMethod>
        Public Sub TestWholeStringLookup()
            Dim vocab As New Dictionary(Of String, Integer)() From {
                {"<unk>", 0}, {"a", 1}, {"b", 2}
            }
            Dim wl As New WordLevelModel(vocab, unkToken:="<unk>")

            Dim tokens As List(Of Token) = wl.Tokenize("a")
            Assert.HasCount(1, tokens)
            Assert.AreEqual(New Token(1, "a", (0, 1)), tokens(0))
        End Sub

        <TestMethod>
        Public Sub TestUnkFallback_ValueAndOffsets()
            Dim vocab As New Dictionary(Of String, Integer)() From {
                {"<unk>", 0}, {"a", 1}, {"b", 2}
            }
            Dim wl As New WordLevelModel(vocab, unkToken:="<unk>")

            Dim tokens As List(Of Token) = wl.Tokenize("c")
            Assert.HasCount(1, tokens)
            Assert.AreEqual(New Token(0, "<unk>", (0, 1)), tokens(0))
        End Sub

        <TestMethod>
        Public Sub TestUnkFallback_MultiByteOffsets()
            ' The unk token carries the WHOLE word's byte offsets.
            Dim vocab As New Dictionary(Of String, Integer)() From {
                {"<unk>", 0}, {"ab", 1}
            }
            Dim wl As New WordLevelModel(vocab, unkToken:="<unk>")

            Dim tokens As List(Of Token) = wl.Tokenize("東京")
            Assert.HasCount(1, tokens)
            Assert.AreEqual(New Token(0, "<unk>", (0, 6)), tokens(0))
        End Sub

        <TestMethod>
        Public Sub TestMissingUnkToken_Throws()
            Dim vocab As New Dictionary(Of String, Integer)() From {
                {"a", 0}, {"b", 1}
            }
            Dim wl As New WordLevelModel(vocab, unkToken:="<unk>")

            Dim tokens As List(Of Token) = wl.Tokenize("a")
            Assert.HasCount(1, tokens)
            Assert.AreEqual(New Token(0, "a", (0, 1)), tokens(0))

            Assert.ThrowsExactly(Of InvalidOperationException)(Sub() wl.Tokenize("c"))
        End Sub

        <TestMethod>
        Public Sub TestAccessors()
            Dim vocab As New Dictionary(Of String, Integer)() From {
                {"<unk>", 0}, {"a", 1}
            }
            Dim wl As New WordLevelModel(vocab, unkToken:="<unk>")

            Assert.AreEqual(2, wl.VocabSize)
            Assert.AreEqual(1, wl.TokenToId("a"))
            Assert.IsFalse(wl.TokenToId("z").HasValue)
            Assert.AreEqual("<unk>", wl.IdToToken(0))
            Assert.AreEqual("a", wl.IdToToken(1))
            Assert.IsNull(wl.IdToToken(5))
        End Sub

    End Class

End Namespace
