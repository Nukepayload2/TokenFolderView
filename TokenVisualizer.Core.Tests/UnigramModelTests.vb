Imports System.Collections.Generic
Imports Tokenizers.Internal
Imports Tokenizers.Models

Namespace TokenVisualizer.Core.Tests

    ''' <summary>
    ''' Tests for the Unigram model port. The fixtures are taken verbatim from the Rust
    ''' <c>models/unigram/model.rs</c> unit tests.
    ''' </summary>
    <TestClass>
    Public Class UnigramModelTests

        Private Shared Sub AssertPieces(actual As List(Of String), ParamArray expected() As String)
            Assert.HasCount(expected.Length, actual, $"expected {expected.Length} pieces, got {actual.Count}")
            For i As Integer = 0 To expected.Length - 1
                Assert.AreEqual(expected(i), actual(i), $"piece[{i}]")
            Next
        End Sub

        Private Shared Function TestEncode2Vocab() As List(Of (String, Double))
            Return New List(Of (String, Double))() From {
                ("<unk>", 0.0), ("ab", 0.0), ("cd", -0.1), ("abc", -0.2), ("a", -0.3),
                ("b", -0.4), ("c", -0.5), ("ABC", -0.5), ("abcdabcd", 20.0),
                ("q", 20.5), ("r", 20.5), ("qr", -0.5)
            }
        End Function

        <TestMethod>
        Public Sub TestPopulateNodesUnk()
            Dim pieces As New List(Of (String, Double))() From {("<unk>", 0.0)}
            Dim model As New UnigramModel(pieces, 0, False)

            Dim lattice As New Lattice("abc", model.BosId, model.EosId)
            model.PopulateNodes(lattice)

            Assert.HasCount(1, lattice.BeginNodes(0))
            Assert.HasCount(1, lattice.BeginNodes(1))
            Assert.HasCount(1, lattice.BeginNodes(2))
            Assert.AreEqual(0, lattice.BeginNodes(0)(0).Id)
            Assert.AreEqual(0, lattice.BeginNodes(1)(0).Id)
            Assert.AreEqual(0, lattice.BeginNodes(2)(0).Id)
            Assert.AreEqual(2, lattice.BeginNodes(0)(0).NodeId)
            Assert.AreEqual(3, lattice.BeginNodes(1)(0).NodeId)
            Assert.AreEqual(4, lattice.BeginNodes(2)(0).NodeId)
        End Sub

        <TestMethod>
        Public Sub TestPopulateNodes()
            Dim pieces As New List(Of (String, Double))() From {
                ("<unk>", 0.0), ("a", 0.1), ("b", 0.2), ("ab", 0.3), ("bc", 0.4)
            }
            Dim model As New UnigramModel(pieces, 0, False)

            Dim lattice As New Lattice("abc", model.BosId, model.EosId)
            model.PopulateNodes(lattice)

            Assert.HasCount(2, lattice.BeginNodes(0)) ' a, ab
            Assert.HasCount(2, lattice.BeginNodes(1)) ' b, bc
            Assert.HasCount(1, lattice.BeginNodes(2)) ' c(unk)

            Assert.AreEqual(1, lattice.BeginNodes(0)(0).Id)
            Assert.AreEqual(3, lattice.BeginNodes(0)(1).Id)
            Assert.AreEqual(2, lattice.BeginNodes(1)(0).Id)
            Assert.AreEqual(4, lattice.BeginNodes(1)(1).Id)
            Assert.AreEqual(0, lattice.BeginNodes(2)(0).Id)

            Assert.AreEqual(2, lattice.BeginNodes(0)(0).NodeId)
            Assert.AreEqual(3, lattice.BeginNodes(0)(1).NodeId)
            Assert.AreEqual(4, lattice.BeginNodes(1)(0).NodeId)
            Assert.AreEqual(5, lattice.BeginNodes(1)(1).NodeId)
            Assert.AreEqual(6, lattice.BeginNodes(2)(0).NodeId)
        End Sub

        <TestMethod>
        Public Sub TestEncode()
            Dim pieces As New List(Of (String, Double))() From {
                ("<unk>", 0.0), ("a", 0.0), ("b", 0.0), ("c", 0.0), ("d", 0.0),
                ("cd", 1.0), ("ab", 2.0), ("abc", 5.0), ("abcd", 10.0)
            }
            Dim model As New UnigramModel(pieces, 0, False)

            AssertPieces(model.Encode("abcd"), "abcd")
        End Sub

        <TestMethod>
        Public Sub TestEncode2()
            Dim pieces As List(Of (String, Double)) = TestEncode2Vocab()
            Dim model As New UnigramModel(pieces, 0, False)

            For Each optimized As Boolean In {True, False}
                model.IsOptimized = optimized
                AssertPieces(model.Encode("abc"), "abc")
                AssertPieces(model.Encode("AB"), "AB")

                model.FuseUnk = False
                AssertPieces(model.Encode("AB"), "A", "B")
                model.FuseUnk = True
                AssertPieces(model.Encode("AB"), "AB")

                AssertPieces(model.Encode("abcd"), "ab", "cd")
                AssertPieces(model.Encode("abcc"), "abc", "c")
                AssertPieces(model.Encode("xabcabaabcdd"), "x", "abc", "ab", "a", "ab", "cd", "d")

                model.FuseUnk = False
                AssertPieces(model.Encode("xyz東京"), "x", "y", "z", "東", "京")
                model.FuseUnk = True
                AssertPieces(model.Encode("xyz東京"), "xyz東京")

                AssertPieces(model.Encode("ABC"), "ABC")
                AssertPieces(model.Encode("abABCcd"), "ab", "ABC", "cd")
                AssertPieces(model.Encode("ababcdabcdcd"), "ab", "abcdabcd", "cd")
                AssertPieces(model.Encode("abqrcd"), "ab", "q", "r", "cd")
            Next
        End Sub

        <TestMethod>
        Public Sub TestUnigramByteFallback()
            Dim pieces As New List(Of (String, Double))() From {
                ("<unk>", 0.0), ("<0xC3>", -0.01), ("<0xA9>", -0.03)
            }
            Dim unigram As New UnigramModel(pieces, 0, True)

            Dim tokens As List(Of Token) = unigram.Tokenize("é")
            Assert.HasCount(2, tokens)
            Assert.AreEqual(New Token(1, "<0xC3>", (0, 2)), tokens(0))
            Assert.AreEqual(New Token(2, "<0xA9>", (0, 2)), tokens(1))

            Dim tokens2 As List(Of Token) = unigram.Tokenize("?é")
            Assert.AreEqual(0, tokens2(0).Id)
        End Sub

        <TestMethod>
        Public Sub TestSamplingProducesValidPaths()
            Dim rng As New Random(42)
            Dim model As New UnigramModel(TestEncode2Vocab(), 0, False, seededRandom:=rng)
            model.IsOptimized = False

            ' Plain theta-sampling: every sampled path must concatenate back to the sentence.
            model.Alpha = 1.0
            model.NbestSize = Nothing
            For Each sentence As String In {"abc", "abcd", "abqrcd", "xyz東京"}
                Dim result As List(Of String) = model.Encode(sentence)
                Assert.AreEqual(sentence, String.Join("", result), $"sample path for '{sentence}'")
            Next

            ' n-best sampling: also structurally valid.
            model.NbestSize = 5
            For Each sentence As String In {"abc", "abcd", "abqrcd", "ababcdabcdcd"}
                Dim result As List(Of String) = model.Encode(sentence)
                Assert.AreEqual(sentence, String.Join("", result), $"sample_nbest path for '{sentence}'")
            Next
        End Sub

        <TestMethod>
        Public Sub TestNbestSize1IsDeterministicViterbi()
            Dim model As New UnigramModel(TestEncode2Vocab(), 0, False)
            model.IsOptimized = False

            model.Alpha = 1.0
            model.NbestSize = 1
            Dim sampled As List(Of String) = model.Encode("ababcdabcdcd")

            model.Alpha = Nothing
            model.NbestSize = Nothing
            Dim viterbi As List(Of String) = model.Encode("ababcdabcdcd")

            AssertPieces(sampled, viterbi.ToArray())
            AssertPieces(sampled, "ab", "abcdabcd", "cd")
        End Sub

        <TestMethod>
        Public Sub TestFromValidation()
            ' Empty vocabulary with unk_id set -> EmptyVocabulary.
            Dim emptyVocab As New List(Of (String, Double))()
            Assert.ThrowsExactly(Of InvalidOperationException)(
                Function()
                    Return New UnigramModel(emptyVocab, 0, False)
                End Function)

            ' unk_id >= vocab length -> UnkIdNotInVocabulary.
            Dim pieces As New List(Of (String, Double))() From {("<unk>", 0.0)}
            Assert.ThrowsExactly(Of InvalidOperationException)(
                Function()
                    Return New UnigramModel(pieces, 5, False)
                End Function)
        End Sub

        <TestMethod>
        Public Sub TestAccessors()
            Dim pieces As New List(Of (String, Double))() From {
                ("<unk>", 0.0), ("ab", -0.1), ("abc", -0.2)
            }
            Dim model As New UnigramModel(pieces, 0, False)

            Assert.AreEqual(3, model.VocabSize)
            Assert.AreEqual(-0.2, model.MinScore)
            Assert.AreEqual(0, model.UnkId)
            Assert.AreEqual(4, model.BosId)
            Assert.AreEqual(5, model.EosId)
            Assert.AreEqual(1, model.TokenToId("ab"))
            Assert.IsFalse(model.TokenToId("z").HasValue)
            Assert.AreEqual("ab", model.IdToToken(1))
            Assert.AreEqual("abc", model.IdToToken(2))
            Assert.IsNull(model.IdToToken(99))

            Dim gv As Dictionary(Of String, Integer) = model.GetVocab()
            Assert.HasCount(3, gv)
        End Sub

    End Class

End Namespace
