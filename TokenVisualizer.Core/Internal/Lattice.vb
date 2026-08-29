Imports System.Collections.Generic
Imports System.Linq
Imports System.Text

Namespace Internal

    ''' <summary>
    ''' A single node in a <see cref="Lattice"/>. Mirrors the Rust <c>Node</c> struct in
    ''' <c>models/unigram/lattice.rs</c>. <see cref="Prev"/> is the index into
    ''' <see cref="Lattice.Nodes"/> of the best predecessor (set by Viterbi), or <c>Nothing</c>
    ''' when unset. Reference semantics (a Class) mirror Rust's <c>Rc&lt;RefCell&lt;Node&gt;&gt;</c>:
    ''' mutations made through one alias are visible through every alias.
    ''' </summary>
    Public Class LatticeNode
        ''' <summary>Vocabulary id of the token this node represents.</summary>
        Public Id As Integer
        ''' <summary>Local lattice identifier (index into <see cref="Lattice.Nodes"/>).</summary>
        Public NodeId As Integer
        ''' <summary>Byte offset where this node begins in the sentence.</summary>
        Public Pos As Integer
        ''' <summary>Byte length of this node.</summary>
        Public Length As Integer
        ''' <summary>Index into <see cref="Lattice.Nodes"/> of the best predecessor, or <c>Nothing</c>.</summary>
        Public Prev As Integer?
        ''' <summary>Total score of the best path from bos to this node (filled by Viterbi).</summary>
        Public BacktraceScore As Double
        ''' <summary>Local score of this node.</summary>
        Public Score As Double

        Public Sub New(id As Integer, nodeId As Integer, pos As Integer, length As Integer, score As Double)
            Me.Id = id
            Me.NodeId = nodeId
            Me.Pos = pos
            Me.Length = length
            Me.Score = score
            Me.Prev = Nothing
            Me.BacktraceScore = 0.0
        End Sub
    End Class

    ''' <summary>
    ''' Sentence-piece lattice used by the Unigram model. Faithful port of the Rust
    ''' <c>Lattice</c> in <c>models/unigram/lattice.rs</c>: supports Viterbi decoding, n-best
    ''' enumeration via A*, marginal (forward/backward) computation, and theta-sampling.
    ''' All offsets are UTF-8 byte offsets into <see cref="Sentence"/>.
    ''' </summary>
    Public NotInheritable Class Lattice

        Private Const KMinusLogEpsilon As Double = 50.0
        Private Const KMaxAgendaSize As Integer = 100000
        Private Const KMinAgendaSize As Integer = 512

        ''' <summary>One A* agenda entry: a node plus accumulated scores and a back-link.</summary>
        Private Class Hypothesis
            Public Node As LatticeNode
            Public NextHyp As Hypothesis
            Public Fx As Double
            Public Gx As Double

            Public Sub New(node As LatticeNode, nextHyp As Hypothesis, fx As Double, gx As Double)
                Me.Node = node
                Me.NextHyp = nextHyp
                Me.Fx = fx
                Me.Gx = gx
            End Sub
        End Class

        Private ReadOnly _bosId As Integer
        Private ReadOnly _eosId As Integer
        Private ReadOnly _rng As Func(Of Double)
        Private ReadOnly _charLenAt As Dictionary(Of Integer, Integer)

        ''' <summary>The sentence being segmented (UTF-8 byte offsets are relative to this).</summary>
        Public ReadOnly Property Sentence As String

        ''' <summary>Byte length of <see cref="Sentence"/>.</summary>
        Public ReadOnly Property Len As Integer

        ''' <summary>All nodes, in insertion order. Indices 0 and 1 are bos and eos.</summary>
        Public ReadOnly Property Nodes As List(Of LatticeNode)

        ''' <summary>Nodes beginning at each byte position.</summary>
        Public ReadOnly Property BeginNodes As List(Of List(Of LatticeNode))

        ''' <summary>Nodes ending at each byte position.</summary>
        Public ReadOnly Property EndNodes As List(Of List(Of LatticeNode))

        Public Sub New(sentence As String, bosId As Integer, eosId As Integer, Optional rng As Func(Of Double) = Nothing)
            Me.Sentence = If(sentence, String.Empty)
            Me.Len = Utf8Helpers.Utf8Length(Me.Sentence)
            Me._bosId = bosId
            Me._eosId = eosId

            If rng Is Nothing Then
                Dim defaultRng As New Random()
                _rng = Function() defaultRng.NextDouble()
            Else
                _rng = rng
            End If

            _charLenAt = New Dictionary(Of Integer, Integer)()
            For Each sc In Utf8Helpers.EnumerateScalars(Me.Sentence)
                _charLenAt(sc.Utf8Start) = sc.Utf8Len
            Next

            Nodes = New List(Of LatticeNode)()
            BeginNodes = New List(Of List(Of LatticeNode))(Me.Len + 1)
            EndNodes = New List(Of List(Of LatticeNode))(Me.Len + 1)
            For i As Integer = 0 To Me.Len
                BeginNodes.Add(New List(Of LatticeNode)())
                EndNodes.Add(New List(Of LatticeNode)())
            Next

            Dim bos As New LatticeNode(bosId, 0, 0, 0, 0.0)
            Dim eos As New LatticeNode(eosId, 1, Me.Len, 0, 0.0)
            BeginNodes(Me.Len).Add(eos)
            EndNodes(0).Add(bos)
            Nodes.Add(bos)
            Nodes.Add(eos)
        End Sub

        ''' <summary>Inserts a node covering byte range [pos, pos+length) with the given score and vocab id.</summary>
        Public Sub Insert(pos As Integer, length As Integer, score As Double, id As Integer)
            Dim nodeId As Integer = Nodes.Count
            Dim node As New LatticeNode(id, nodeId, pos, length, score)
            BeginNodes(pos).Add(node)
            EndNodes(pos + length).Add(node)
            Nodes.Add(node)
        End Sub

        ''' <summary>The bos node (the single node in <c>EndNodes(0)</c>).</summary>
        Public ReadOnly Property BosNode As LatticeNode
            Get
                Return EndNodes(0)(0)
            End Get
        End Property

        ''' <summary>The eos node (the single node in <c>BeginNodes(Len)</c>).</summary>
        Public ReadOnly Property EosNode As LatticeNode
            Get
                Return BeginNodes(Len)(0)
            End Get
        End Property

        ''' <summary>Returns the sentence substring covered by <paramref name="node"/>.</summary>
        Public Function Piece(node As LatticeNode) As String
            Return Utf8Helpers.SliceByUtf8(Sentence, node.Pos, node.Pos + node.Length)
        End Function

        ''' <summary>Returns the substring of the sentence starting at the nth char boundary, or "" if none.</summary>
        Public Function Surface(n As Integer) As String
            Dim count As Integer = 0
            For Each sc In Utf8Helpers.EnumerateScalars(Sentence)
                If count = n Then
                    Return Utf8Helpers.SliceByUtf8(Sentence, sc.Utf8Start, Utf8Helpers.Utf8Length(Sentence))
                End If
                count += 1
            Next
            Return String.Empty
        End Function

        ''' <summary>
        ''' Viterbi decoding: fills <c>Prev</c>/<c>BacktraceScore</c> on every reachable node and
        ''' returns the best path (bos and eos excluded). Returns an empty list when the lattice is
        ''' incomplete (some begin position has no node, or a node has no predecessor).
        ''' </summary>
        Public Function Viterbi() As List(Of LatticeNode)
            Dim totalLen As Integer = Len
            Dim pos As Integer = 0
            While pos <= totalLen
                If BeginNodes(pos).Count = 0 Then Return New List(Of LatticeNode)()
                For Each rnode As LatticeNode In BeginNodes(pos)
                    rnode.Prev = Nothing
                    Dim bestScore As Double = 0.0
                    Dim bestNode As LatticeNode = Nothing
                    For Each lnode As LatticeNode In EndNodes(pos)
                        Dim score As Double = lnode.BacktraceScore + rnode.Score
                        If bestNode Is Nothing OrElse score > bestScore Then
                            bestNode = lnode
                            bestScore = score
                        End If
                    Next
                    If bestNode Is Nothing Then Return New List(Of LatticeNode)()
                    rnode.Prev = bestNode.NodeId
                    rnode.BacktraceScore = bestScore
                Next
                Dim mblen As Integer = CharLenAt(pos)
                If mblen = 0 Then Exit While
                pos += mblen
            End While

            Dim results As New List(Of LatticeNode)()
            Dim root As LatticeNode = BeginNodes(totalLen)(0)
            If Not root.Prev.HasValue Then Return results
            Dim node As LatticeNode = Nodes(root.Prev.Value)
            While node.Prev.HasValue
                results.Add(node)
                node = Nodes(node.Prev.Value)
            End While
            results.Reverse()
            Return results
        End Function

        ''' <summary>Viterbi path as a list of piece strings.</summary>
        Public Function Tokens() As List(Of String)
            Dim result As New List(Of String)()
            For Each node As LatticeNode In Viterbi()
                result.Add(Piece(node))
            Next
            Return result
        End Function

        ''' <summary>
        ''' Enumerates the top-<paramref name="n"/> paths (in decreasing total score) via A* over
        ''' the lattice. Mirrors the Rust <c>nbest</c>: n=0 → empty, n=1 → [viterbi]. When the
        ''' agenda overgrows it is shrunk to <c>min(512, n*10)</c> entries.
        ''' </summary>
        Public Function Nbest(n As Integer) As List(Of List(Of LatticeNode))
            If n = 0 Then Return New List(Of List(Of LatticeNode))()
            If n = 1 Then
                Return New List(Of List(Of LatticeNode))() From {Viterbi()}
            End If

            Dim agenda As New PriorityQueue(Of Hypothesis, Double)()
            Dim hypotheses As New List(Of List(Of LatticeNode))()

            Dim eos As LatticeNode = EosNode
            Dim score As Double = eos.Score
            agenda.Enqueue(New Hypothesis(eos, Nothing, score, score), -score)

            ' Fill backtrace scores.
            Viterbi()

            While agenda.Count > 0
                Dim top As Hypothesis = agenda.Dequeue()
                Dim node As LatticeNode = top.Node
                If node.Id = BosNode.Id Then
                    Dim hypothesis As New List(Of LatticeNode)()
                    Dim nextHyp As Hypothesis = top.NextHyp
                    While nextHyp.NextHyp IsNot Nothing
                        hypothesis.Add(nextHyp.Node)
                        nextHyp = nextHyp.NextHyp
                    End While
                    hypotheses.Add(hypothesis)
                    If hypotheses.Count = n Then Return hypotheses
                Else
                    For Each lnode As LatticeNode In EndNodes(node.Pos)
                        Dim topGx As Double = top.Gx
                        Dim fx As Double = lnode.BacktraceScore + topGx
                        Dim gx As Double = lnode.Score + topGx
                        agenda.Enqueue(New Hypothesis(lnode, top, fx, gx), -fx)
                    Next

                    ' Shrink an overgrown agenda (Rust: keep min(512, n*10) best entries).
                    If agenda.Count > KMaxAgendaSize Then
                        Dim newAgenda As New PriorityQueue(Of Hypothesis, Double)()
                        Dim takeLen As Integer = Math.Min(KMinAgendaSize, n * 10)
                        For i As Integer = 0 To takeLen - 1
                            Dim h As Hypothesis = agenda.Dequeue()
                            newAgenda.Enqueue(h, -h.Fx)
                        Next
                        agenda = newAgenda
                    End If
                End If
            End While

            Return hypotheses
        End Function

        ''' <summary>n-best paths as lists of piece strings.</summary>
        Public Function NbestTokens(n As Integer) As List(Of List(Of String))
            Dim result As New List(Of List(Of String))()
            For Each path As List(Of LatticeNode) In Nbest(n)
                Dim pieces As New List(Of String)()
                For Each node As LatticeNode In path
                    pieces.Add(Piece(node))
                Next
                result.Add(pieces)
            Next
            Return result
        End Function

        ''' <summary>
        ''' Computes forward/backward marginals over the lattice and accumulates
        ''' <c>freq * marginal(node)</c> into <paramref name="expected"/> indexed by node id.
        ''' Returns <c>freq * log_z</c>. Mirrors the Rust <c>populate_marginal</c>.
        ''' </summary>
        Public Function PopulateMarginal(freq As Double, expected As Double()) As Double
            Dim totalLen As Integer = Len
            Dim nNodes As Integer = Nodes.Count
            Dim alpha As Double() = New Double(nNodes - 1) {}
            Dim beta As Double() = New Double(nNodes - 1) {}

            For pos As Integer = 0 To totalLen
                For Each rnode As LatticeNode In BeginNodes(pos)
                    For Each lnode As LatticeNode In EndNodes(pos)
                        Dim lid As Integer = lnode.NodeId
                        Dim rid As Integer = rnode.NodeId
                        alpha(rid) = LogSumExp(alpha(rid), lnode.Score + alpha(lid), lnode.Id = EndNodes(pos)(0).Id)
                    Next
                Next
            Next

            For pos As Integer = totalLen To 0 Step -1
                For Each lnode As LatticeNode In EndNodes(pos)
                    For Each rnode As LatticeNode In BeginNodes(pos)
                        Dim lid As Integer = lnode.NodeId
                        Dim rid As Integer = rnode.NodeId
                        beta(lid) = LogSumExp(beta(lid), rnode.Score + beta(rid), rnode.Id = BeginNodes(pos)(0).Id)
                    Next
                Next
            Next

            Dim eosId As Integer = BeginNodes(totalLen)(0).NodeId
            Dim z As Double = alpha(eosId)
            For pos As Integer = 0 To totalLen - 1
                For Each node As LatticeNode In BeginNodes(pos)
                    Dim nodeId As Integer = node.NodeId
                    Dim id As Integer = node.Id
                    Dim a As Double = alpha(nodeId)
                    Dim b As Double = beta(nodeId)
                    Dim total As Double = a + node.Score + b - z
                    Dim update As Double = freq * Math.Exp(total)
                    expected(id) += update
                Next
            Next
            Return freq * z
        End Function

        ''' <summary>
        ''' Theta-samples a path from the lattice (Rust <c>sample</c>). <c>theta</c> is the
        ''' sampling temperature; the random source is the injected <c>Func(Of Double)</c>.
        ''' </summary>
        Public Function Sample(theta As Double) As List(Of LatticeNode)
            Dim totalLen As Integer = Len
            If totalLen = 0 Then Return New List(Of LatticeNode)()
            Dim alpha As Double() = New Double(Nodes.Count - 1) {}
            For pos As Integer = 0 To totalLen
                For Each rnode As LatticeNode In BeginNodes(pos)
                    For Each lnode As LatticeNode In EndNodes(pos)
                        Dim lid As Integer = lnode.NodeId
                        Dim rid As Integer = rnode.NodeId
                        alpha(rid) = LogSumExp(alpha(rid), theta * (lnode.Score + alpha(lid)), lnode.Id = EndNodes(pos)(0).Id)
                    Next
                Next
            Next

            Dim results As New List(Of LatticeNode)()
            Dim probs As New List(Of Double)()
            Dim z As Double = alpha(EosNode.NodeId)
            Dim node As LatticeNode = EosNode
            While True
                probs.Clear()
                Dim pos As Integer = node.Pos
                For Each lnode As LatticeNode In EndNodes(pos)
                    Dim lid As Integer = lnode.NodeId
                    probs.Add(Math.Exp(alpha(lid) + theta * lnode.Score - z))
                Next
                Dim index As Integer = WeightedPick(probs)
                node = EndNodes(pos)(index)
                If node.Id = BosNode.Id Then Exit While
                z = alpha(node.NodeId)
                results.Add(node)
            End While
            results.Reverse()
            Return results
        End Function

        ''' <summary>
        ''' Samples one of the n-best paths, weighted by <c>exp(theta * path_score)</c>.
        ''' Mirrors the Rust <c>sample_nbest</c> (falls back to the best path when weighting fails).
        ''' </summary>
        Public Function SampleNbest(n As Integer, theta As Double) As List(Of LatticeNode)
            Dim nbestPaths As List(Of List(Of LatticeNode)) = Nbest(n)
            If nbestPaths.Count = 0 Then Return Viterbi()

            Dim probs As New List(Of Double)()
            For Each p As List(Of LatticeNode) In nbestPaths
                Dim pathScore As Double = 0.0
                For Each node As LatticeNode In p
                    pathScore += node.Score
                Next
                probs.Add(Math.Exp(theta * pathScore))
            Next

            Dim total As Double = 0.0
            For Each w In probs
                total += w
            Next
            If Double.IsNaN(total) OrElse total <= 0.0 Then
                Return nbestPaths(0)
            End If
            Dim index As Integer = WeightedPick(probs)
            Return nbestPaths(index)
        End Function

        ''' <summary>
        ''' Stable log-sum-exp: <c>log(exp(x) + exp(y))</c>, or <c>y</c> when <paramref name="initMode"/>
        ''' is true. Mirrors the Rust free function <c>log_sum_exp</c>.
        ''' </summary>
        Public Shared Function LogSumExp(x As Double, y As Double, initMode As Boolean) As Double
            If initMode Then Return y
            Dim vmin As Double
            Dim vmax As Double
            If x > y Then
                vmin = y
                vmax = x
            Else
                vmin = x
                vmax = y
            End If
            If vmax > vmin + KMinusLogEpsilon Then
                Return vmax
            End If
            Return vmax + Math.Log(Math.Exp(vmin - vmax) + 1.0)
        End Function

        ''' <summary>UTF-8 byte length of the scalar starting at byte offset <paramref name="pos"/>, or 0 if none.</summary>
        Private Function CharLenAt(pos As Integer) As Integer
            Dim len As Integer
            If _charLenAt.TryGetValue(pos, len) Then Return len
            Return 0
        End Function

        ''' <summary>Picks an index from <paramref name="probs"/> with probability proportional to its weight.</summary>
        Private Function WeightedPick(probs As IList(Of Double)) As Integer
            Dim total As Double = 0.0
            For Each p In probs
                total += p
            Next
            Dim r As Double = _rng() * total
            Dim cumulative As Double = 0.0
            For i As Integer = 0 To probs.Count - 1
                cumulative += probs(i)
                If cumulative >= r Then Return i
            Next
            Return probs.Count - 1
        End Function

    End Class

End Namespace
