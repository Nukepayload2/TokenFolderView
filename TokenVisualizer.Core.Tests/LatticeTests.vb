Imports System.Collections.Generic
Imports Tokenizers.Internal

Namespace TokenVisualizer.Core.Tests

    ''' <summary>
    ''' Tests for the Unigram lattice port, mirroring the Rust <c>models/unigram/lattice.rs</c>
    ''' unit tests.
    ''' </summary>
    <TestClass>
    Public Class LatticeTests

        Private Shared Sub AssertPieces(actual As List(Of String), ParamArray expected() As String)
            Assert.HasCount(expected.Length, actual, $"expected {expected.Length} pieces, got {actual.Count}")
            For i As Integer = 0 To expected.Length - 1
                Assert.AreEqual(expected(i), actual(i), $"piece[{i}]")
            Next
        End Sub

        <TestMethod>
        Public Sub SetSentence()
            Dim lattice As New Lattice("", 1, 2)
            Assert.AreEqual(0, lattice.Len)
            Assert.AreEqual("", lattice.Sentence)
            Assert.AreEqual("", lattice.Surface(0))

            lattice = New Lattice("test", 1, 2)
            Assert.AreEqual(4, lattice.Len)
            Assert.AreEqual("test", lattice.Sentence)
            Assert.AreEqual("test", lattice.Surface(0))
            Assert.AreEqual("est", lattice.Surface(1))
            Assert.AreEqual("st", lattice.Surface(2))
            Assert.AreEqual("t", lattice.Surface(3))

            Dim bos As LatticeNode = lattice.BosNode
            Dim eos As LatticeNode = lattice.EosNode
            Assert.AreEqual(1, bos.Id)
            Assert.AreEqual(2, eos.Id)
            Assert.AreEqual(1, lattice.EndNodes(0)(0).Id)
            Assert.AreEqual(2, lattice.BeginNodes(4)(0).Id)

            lattice = New Lattice("テストab", 1, 2)
            Assert.AreEqual(11, lattice.Len)
            Assert.AreEqual("テストab", lattice.Sentence)
            Assert.AreEqual("テストab", lattice.Surface(0))
            Assert.AreEqual("ストab", lattice.Surface(1))
            Assert.AreEqual("トab", lattice.Surface(2))
            Assert.AreEqual("ab", lattice.Surface(3))
            Assert.AreEqual("b", lattice.Surface(4))
        End Sub

        <TestMethod>
        Public Sub InsertTest()
            Dim lattice As New Lattice("ABあい", 1, 2)

            lattice.Insert(0, 1, 0.0, 3)
            lattice.Insert(1, 1, 0.0, 4)
            lattice.Insert(2, 3, 0.0, 5)
            lattice.Insert(5, 3, 0.0, 6)
            lattice.Insert(0, 2, 0.0, 7)
            lattice.Insert(1, 4, 0.0, 8)
            lattice.Insert(2, 6, 0.0, 9)

            ' 0 & 1 are bos and eos.
            Dim node0 As LatticeNode = lattice.Nodes(2)
            Dim node1 As LatticeNode = lattice.Nodes(3)
            Dim node2 As LatticeNode = lattice.Nodes(4)
            Dim node3 As LatticeNode = lattice.Nodes(5)
            Dim node4 As LatticeNode = lattice.Nodes(6)
            Dim node5 As LatticeNode = lattice.Nodes(7)
            Dim node6 As LatticeNode = lattice.Nodes(8)

            Assert.AreEqual("A", lattice.Piece(node0))
            Assert.AreEqual("B", lattice.Piece(node1))
            Assert.AreEqual("あ", lattice.Piece(node2))
            Assert.AreEqual("い", lattice.Piece(node3))
            Assert.AreEqual("AB", lattice.Piece(node4))
            Assert.AreEqual("Bあ", lattice.Piece(node5))
            Assert.AreEqual("あい", lattice.Piece(node6))

            Assert.AreEqual(0, node0.Pos)
            Assert.AreEqual(1, node1.Pos)
            Assert.AreEqual(2, node2.Pos)
            Assert.AreEqual(5, node3.Pos)
            Assert.AreEqual(0, node4.Pos)
            Assert.AreEqual(1, node5.Pos)
            Assert.AreEqual(2, node6.Pos)

            Assert.AreEqual(1, node0.Length)
            Assert.AreEqual(1, node1.Length)
            Assert.AreEqual(3, node2.Length)
            Assert.AreEqual(3, node3.Length)
            Assert.AreEqual(2, node4.Length)
            Assert.AreEqual(4, node5.Length)
            Assert.AreEqual(6, node6.Length)

            Assert.AreEqual(1, lattice.BosNode.Id)
            Assert.AreEqual(2, lattice.EosNode.Id)
            Assert.AreEqual(3, node0.Id)
            Assert.AreEqual(4, node1.Id)
            Assert.AreEqual(5, node2.Id)
            Assert.AreEqual(6, node3.Id)
            Assert.AreEqual(7, node4.Id)
            Assert.AreEqual(8, node5.Id)
            Assert.AreEqual(9, node6.Id)

            Assert.HasCount(2, lattice.BeginNodes(0))
            Assert.HasCount(2, lattice.BeginNodes(1))
            Assert.HasCount(2, lattice.BeginNodes(2))
            Assert.HasCount(1, lattice.BeginNodes(5))
            Assert.HasCount(1, lattice.BeginNodes(8))

            Assert.HasCount(1, lattice.EndNodes(0))
            Assert.HasCount(1, lattice.EndNodes(1))
            Assert.HasCount(2, lattice.EndNodes(2))
            Assert.HasCount(2, lattice.EndNodes(5))
            Assert.HasCount(2, lattice.EndNodes(8))

            Assert.AreEqual(node0.Id, lattice.BeginNodes(0)(0).Id)
            Assert.AreEqual(node4.Id, lattice.BeginNodes(0)(1).Id)
            Assert.AreEqual(node1.Id, lattice.BeginNodes(1)(0).Id)
            Assert.AreEqual(node5.Id, lattice.BeginNodes(1)(1).Id)
            Assert.AreEqual(node2.Id, lattice.BeginNodes(2)(0).Id)
            Assert.AreEqual(node6.Id, lattice.BeginNodes(2)(1).Id)
            Assert.AreEqual(node3.Id, lattice.BeginNodes(5)(0).Id)
            Assert.AreEqual(lattice.EosNode.Id, lattice.BeginNodes(8)(0).Id)

            Assert.AreEqual(lattice.BosNode.Id, lattice.EndNodes(0)(0).Id)
            Assert.AreEqual(node0.Id, lattice.EndNodes(1)(0).Id)
            Assert.AreEqual(node1.Id, lattice.EndNodes(2)(0).Id)
            Assert.AreEqual(node4.Id, lattice.EndNodes(2)(1).Id)
            Assert.AreEqual(node2.Id, lattice.EndNodes(5)(0).Id)
            Assert.AreEqual(node5.Id, lattice.EndNodes(5)(1).Id)
            Assert.AreEqual(node3.Id, lattice.EndNodes(8)(0).Id)
            Assert.AreEqual(node6.Id, lattice.EndNodes(8)(1).Id)
        End Sub

        <TestMethod>
        Public Sub TestViterbi()
            Dim lattice As New Lattice("ABC", 1, 2)
            Assert.HasCount(0, lattice.Viterbi())

            ' Still incomplete (no node begins at position 1).
            lattice.Insert(0, 1, 0.0, 3)
            Assert.HasCount(0, lattice.Viterbi())

            lattice.Insert(1, 1, 0.0, 4)
            lattice.Insert(2, 1, 0.0, 5)
            Assert.HasCount(3, lattice.Viterbi())
        End Sub

        <TestMethod>
        Public Sub TestViterbi2()
            Dim lattice As New Lattice("ABC", 1, 2)

            lattice.Insert(0, 1, 0.0, 3)
            lattice.Insert(1, 1, 0.0, 4)
            lattice.Insert(2, 1, 0.0, 5)

            AssertPieces(lattice.Tokens(), "A", "B", "C")

            lattice.Insert(0, 2, 2.0, 6)
            AssertPieces(lattice.Tokens(), "AB", "C")

            lattice.Insert(1, 2, 5.0, 7)
            AssertPieces(lattice.Tokens(), "A", "BC")

            lattice.Insert(0, 3, 10.0, 8)
            AssertPieces(lattice.Tokens(), "ABC")
        End Sub

        <TestMethod>
        Public Sub TestNbest()
            Dim lattice As New Lattice("ABC", 1, 2)
            lattice.Insert(0, 1, 0.0, 3)
            lattice.Insert(1, 1, 0.0, 4)
            lattice.Insert(2, 1, 0.0, 5)
            lattice.Insert(0, 2, 2.0, 6)
            lattice.Insert(1, 2, 5.0, 7)
            lattice.Insert(0, 3, 10.0, 8)

            Dim nbests As List(Of List(Of String)) = lattice.NbestTokens(10)
            Assert.HasCount(4, nbests)
            AssertPieces(nbests(0), "ABC")
            AssertPieces(nbests(1), "A", "BC")
            AssertPieces(nbests(2), "AB", "C")
            AssertPieces(nbests(3), "A", "B", "C")

            Assert.HasCount(0, lattice.NbestTokens(0))

            Dim singlePath As List(Of List(Of String)) = lattice.NbestTokens(1)
            Assert.HasCount(1, singlePath)
            AssertPieces(singlePath(0), "ABC")
        End Sub

        <TestMethod>
        Public Sub TestLogSumExp()
            Dim x As Double = 0.0
            Dim v As Double() = {1.0, 2.0, 3.0}
            For i As Integer = 0 To v.Length - 1
                x = Lattice.LogSumExp(x, v(i), i = 0)
            Next
            Dim expected As Double = Math.Log(Math.Exp(1.0) + Math.Exp(2.0) + Math.Exp(3.0))
            Assert.AreEqual(expected, x, 0.001)
        End Sub

        <TestMethod>
        Public Sub TestPopulate()
            Dim lattice As New Lattice("ABC", 1, 2)
            lattice.Insert(0, 1, 1.0, 3) ' A
            lattice.Insert(1, 1, 1.2, 4) ' B
            lattice.Insert(2, 1, 2.5, 5) ' C
            lattice.Insert(0, 2, 3.0, 6) ' AB
            lattice.Insert(1, 2, 4.0, 7) ' BC
            lattice.Insert(0, 3, 2.0, 8) ' ABC

            Dim probs As Double() = New Double(8) {}
            Dim p1 As Double = Math.Exp(1.0 + 1.2 + 2.5)
            Dim p2 As Double = Math.Exp(3.0 + 2.5)
            Dim p3 As Double = Math.Exp(1.0 + 4.0)
            Dim p4 As Double = Math.Exp(2.0)
            Dim z As Double = p1 + p2 + p3 + p4

            Dim logZ As Double = lattice.PopulateMarginal(1.0, probs)

            Assert.AreEqual(z, Math.Exp(logZ), 0.001)
            Assert.AreEqual(0.0, probs(0), 0.001)
            Assert.AreEqual(0.0, probs(1), 0.001)
            Assert.AreEqual(0.0, probs(2), 0.001)
            Assert.AreEqual((p1 + p3) / z, probs(3), 0.001)
            Assert.AreEqual(p1 / z, probs(4), 0.001)
            Assert.AreEqual((p1 + p2) / z, probs(5), 0.001)
            Assert.AreEqual(p2 / z, probs(6), 0.001)
            Assert.AreEqual(p3 / z, probs(7), 0.001)
            Assert.AreEqual(p4 / z, probs(8), 0.001)
        End Sub

    End Class

End Namespace
