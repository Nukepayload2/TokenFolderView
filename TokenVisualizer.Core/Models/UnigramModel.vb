Imports System.Collections.Generic
Imports System.Linq
Imports System.Text
Imports System.Text.Json.Nodes
Imports System.Threading
Imports Tokenizers.Internal

Namespace Models

    ''' <summary>
    ''' A Unigram (sentence-piece) model. Faithful port of the Rust
    ''' <c>models/unigram/model.rs</c> (struct <c>Unigram</c>) using
    ''' <see cref="Lattice"/> for the unoptimized path and a direct trie DP for the optimized path.
    '''
    ''' <c>Encode</c> returns the best tokenization as a list of piece strings; <c>Tokenize</c>
    ''' converts those to <see cref="Token"/>s with byte offsets (optionally via byte-fallback).
    ''' </summary>
    Public NotInheritable Class UnigramModel
        Implements IModel

        Private Const KUnkPenalty As Double = 10.0
        Private Const MaxCacheLength As Integer = 256

        Private ReadOnly _vocab As List(Of (String, Double))
        Private ReadOnly _tokenToIds As Dictionary(Of String, Integer)
        Private ReadOnly _trie As Trie(Of Byte)
        ''' <summary>
        ''' Thread-local sentence→pieces cache. Mirrors the Rust <c>thread_local!</c> cache: each
        ''' thread has an independent bounded FIFO cache, so concurrent <c>EncodeCount</c> callers
        ''' never contend. <c>trackAllValues</c> is enabled so <see cref="ClearCache"/> /
        ''' <see cref="FuseUnk"/> can clear every thread's cache.
        ''' </summary>
        Private ReadOnly _cache As ThreadLocal(Of Cache(Of String, List(Of String)))
        Private ReadOnly _random As Func(Of Double)

        Private _fuseUnk As Boolean
        Private _isOptimized As Boolean
        Private _byteFallback As Boolean
        Private _minScore As Double
        Private _unkId As Integer?
        Private _bosId As Integer
        Private _eosId As Integer
        Private _alpha As Double?
        Private _nbestSize As Integer?

        ''' <summary>
        ''' Creates a Unigram model.
        ''' </summary>
        ''' <param name="vocab">Ordered (piece, logprob) pairs; the order fixes the ids.</param>
        ''' <param name="unkId">Index of the <c>&lt;unk&gt;</c> piece in <paramref name="vocab"/>, or <c>Nothing</c>.</param>
        ''' <param name="byteFallback">Emit <c>"&lt;0xXX&gt;"</c> byte tokens instead of unk for unknown pieces.</param>
        ''' <param name="seededRandom">A <see cref="Random"/> or <see cref="Func(Of Double)"/> used only for sampling.</param>
        Public Sub New(vocab As IEnumerable(Of (String, Double)),
                       unkId As Integer?,
                       byteFallback As Boolean,
                       Optional seededRandom As Object = Nothing)
            If seededRandom Is Nothing Then
                Dim rng As New Random()
                _random = Function() rng.NextDouble()
            ElseIf TypeOf seededRandom Is Random Then
                Dim rng As Random = DirectCast(seededRandom, Random)
                _random = Function() rng.NextDouble()
            ElseIf TypeOf seededRandom Is Func(Of Double) Then
                _random = DirectCast(seededRandom, Func(Of Double))
            Else
                Throw New ArgumentException("seededRandom must be a Random or a Func(Of Double)", NameOf(seededRandom))
            End If

            _vocab = New List(Of (String, Double))(vocab)
            Dim n As Integer = _vocab.Count

            If unkId.HasValue Then
                If _vocab.Count = 0 Then
                    Throw New InvalidOperationException("The vocabulary is empty but at least <unk> is needed")
                End If
                If unkId.Value >= _vocab.Count Then
                    Throw New InvalidOperationException("The `unk_id` is larger than vocabulary size")
                End If
            End If

            _bosId = n + 1
            _eosId = n + 2
            _unkId = unkId
            _byteFallback = byteFallback
            _fuseUnk = True
            _isOptimized = True
            _alpha = Nothing
            _nbestSize = Nothing
            _cache = New ThreadLocal(Of Cache(Of String, List(Of String)))(
                Function() New Cache(Of String, List(Of String))(),
                trackAllValues:=True)

            _tokenToIds = New Dictionary(Of String, Integer)()
            _trie = New Trie(Of Byte)()
            _minScore = Double.PositiveInfinity
            For i As Integer = 0 To _vocab.Count - 1
                Dim token As String = _vocab(i).Item1
                Dim score As Double = _vocab(i).Item2
                _tokenToIds(token) = i
                _trie.Push(Global.System.Text.Encoding.UTF8.GetBytes(token))
                If score < _minScore Then _minScore = score
            Next
        End Sub

        ''' <summary>The minimum score in the vocabulary.</summary>
        Public ReadOnly Property MinScore As Double
            Get
                Return _minScore
            End Get
        End Property

        ''' <summary>Index of the <c>&lt;unk&gt;</c> piece, or <c>Nothing</c>.</summary>
        Public ReadOnly Property UnkId As Integer?
            Get
                Return _unkId
            End Get
        End Property

        ''' <summary>bos id (vocab count + 1).</summary>
        Public ReadOnly Property BosId As Integer
            Get
                Return _bosId
            End Get
        End Property

        ''' <summary>eos id (vocab count + 2).</summary>
        Public ReadOnly Property EosId As Integer
            Get
                Return _eosId
            End Get
        End Property

        ''' <summary>Whether unknown pieces fall back to byte tokens.</summary>
        Public ReadOnly Property ByteFallback As Boolean
            Get
                Return _byteFallback
            End Get
        End Property

        ''' <summary>Number of entries in the vocabulary.</summary>
        Public ReadOnly Property VocabSize As Integer Implements IModel.VocabSize
            Get
                Return _vocab.Count
            End Get
        End Property

        ''' <summary>
        ''' Whether consecutive unknown pieces are fused into a single piece. Setting this clears
        ''' the internal cache (mirrors the Rust test-only <c>set_fuse_unk</c>).
        ''' </summary>
        Public Property FuseUnk As Boolean
            Get
                Return _fuseUnk
            End Get
            Set(value As Boolean)
                _fuseUnk = value
                ClearAllThreadCaches()
            End Set
        End Property

        ''' <summary>
        ''' Whether <c>Encode</c> uses the fast DP path (<c>encode_optimized</c>) instead of the
        ''' lattice path. Results are identical; this exists for parity with the Rust tests.
        ''' </summary>
        Public Property IsOptimized As Boolean
            Get
                Return _isOptimized
            End Get
            Set(value As Boolean)
                _isOptimized = value
            End Set
        End Property

        ''' <summary>Sampling temperature; <c>Nothing</c> or 0 disables sampling.</summary>
        Public Property Alpha As Double?
            Get
                Return _alpha
            End Get
            Set(value As Double?)
                _alpha = value
            End Set
        End Property

        ''' <summary>n-best size for sampling; <c>Nothing</c> disables n-best sampling.</summary>
        Public Property NbestSize As Integer?
            Get
                Return _nbestSize
            End Get
            Set(value As Integer?)
                _nbestSize = value
            End Set
        End Property

        ''' <summary>Clears the internal encode cache.</summary>
        Public Sub ClearCache()
            ClearAllThreadCaches()
        End Sub

        ''' <summary>
        ''' Clears every thread's cache. Threads that have not yet accessed the cache have no
        ''' instance (a fresh empty cache is created on their first access), so clearing the
        ''' tracked values covers all live caches.
        ''' </summary>
        Private Sub ClearAllThreadCaches()
            For Each c As Cache(Of String, List(Of String)) In _cache.Values
                c.Clear()
            Next
        End Sub

        ''' <summary>Returns a copy of the token→id map.</summary>
        Public Function GetVocab() As Dictionary(Of String, Integer) Implements IModel.GetVocab
            Return New Dictionary(Of String, Integer)(_tokenToIds)
        End Function

        ''' <summary>Maps a token to its vocabulary id, or <c>Nothing</c> if absent.</summary>
        Public Function TokenToId(token As String) As Integer? Implements IModel.TokenToId
            Dim id As Integer
            If _tokenToIds.TryGetValue(token, id) Then Return id
            Return Nothing
        End Function

        ''' <summary>Maps an id back to the vocabulary piece at that index, or <c>Nothing</c> if out of range.</summary>
        Public Function IdToToken(id As Integer) As String Implements IModel.IdToToken
            If id >= 0 AndAlso id < _vocab.Count Then Return _vocab(id).Item1
            Return Nothing
        End Function

        ''' <summary>
        ''' Serializes this model to its tokenizer.json representation. Mirrors the Rust
        ''' <c>Unigram</c> serialization (models/unigram/serialization.rs). The ordered
        ''' (piece, score) vocabulary is preserved exactly.
        ''' </summary>
        Public Function ToJson() As JsonObject Implements IModel.ToJson
            Dim o As New JsonObject()
            o("type") = "Unigram"
            o("unk_id") = If(_unkId.HasValue, JsonValue.Create(_unkId.Value), Nothing)

            Dim vocabArr As New JsonArray()
            For Each pair As (String, Double) In _vocab
                Dim entry As New JsonArray()
                entry.Add(JsonValue.Create(pair.Item1))
                entry.Add(JsonValue.Create(pair.Item2))
                vocabArr.Add(entry)
            Next
            o("vocab") = vocabArr
            o("byte_fallback") = _byteFallback
            Return o
        End Function

        ''' <summary>
        ''' Encodes a sentence into its best (or sampled) tokenization. Empty input → empty result.
        ''' Mirrors Rust <c>Unigram::encode</c> including the <c>&lt;256 byte</c> cache.
        ''' </summary>
        Public Function Encode(sentence As String) As List(Of String)
            If String.IsNullOrEmpty(sentence) Then Return New List(Of String)()
            If (Not _alpha.HasValue) OrElse _alpha.Value = 0.0 Then
                Dim cache As Cache(Of String, List(Of String)) = _cache.Value
                Dim cached As List(Of String) = cache.GetValue(sentence)
                If cached IsNot Nothing Then Return New List(Of String)(cached)
                Dim result As List(Of String)
                If _isOptimized Then
                    result = EncodeOptimized(sentence)
                Else
                    result = EncodeUnoptimized(sentence)
                End If
                If Utf8Helpers.Utf8Length(sentence) < MaxCacheLength Then
                    cache.Insert(sentence, New List(Of String)(result))
                End If
                Return result
            Else
                Return EncodeUnoptimized(sentence)
            End If
        End Function

        ''' <summary>
        ''' Tokenizes a sentence into <see cref="Token"/>s with byte offsets. Mirrors Rust
        ''' <c>Unigram::tokenize</c> (model.rs:443-477) including the byte-fallback offsets quirk:
        ''' every byte token of an unknown piece carries the whole piece's offsets.
        ''' </summary>
        Public Function Tokenize(sentence As String) As List(Of Token) Implements IModel.Tokenize
            Dim strTokens As List(Of String) = Encode(sentence)
            Dim offset As Integer = 0
            Dim tokens As New List(Of Token)(strTokens.Count)
            For Each s As String In strTokens
                Dim len As Integer = Utf8Helpers.Utf8Length(s)
                Dim offsets As (Integer, Integer) = (offset, offset + len)
                Dim id As Integer
                If _tokenToIds.TryGetValue(s, id) Then
                    offset += len
                    tokens.Add(New Token(id, s, offsets))
                Else
                    If _byteFallback Then
                        Dim byteTokens As New List(Of Token)()
                        Dim allOk As Boolean = True
                        Dim bytes As Byte() = Global.System.Text.Encoding.UTF8.GetBytes(s)
                        For Each b As Byte In bytes
                            Dim byteString As String = String.Format("<0x{0:X2}>", b)
                            Dim bid As Integer
                            If _tokenToIds.TryGetValue(byteString, bid) Then
                                byteTokens.Add(New Token(bid, byteString, offsets))
                            Else
                                allOk = False
                                Exit For
                            End If
                        Next
                        If allOk Then
                            For Each t As Token In byteTokens
                                tokens.Add(t)
                            Next
                            offset += len
                            Continue For
                        End If
                    End If
                    If Not _unkId.HasValue Then
                        Throw New InvalidOperationException("Encountered an unknown token but `unk_id` is missing")
                    End If
                    offset += len
                    tokens.Add(New Token(_unkId.Value, s, offsets))
                End If
            Next
            Return tokens
        End Function

        ''' <summary>
        ''' Populates a lattice with the vocabulary trie matches and unk fallbacks. Mirrors Rust
        ''' <c>Unigram::populate_nodes</c> (model.rs:170-209).
        ''' </summary>
        Public Sub PopulateNodes(lattice As Lattice)
            Dim unkScore As Double = _minScore - KUnkPenalty
            Dim len As Integer = lattice.Len
            Dim sentenceBytes As Byte() = Global.System.Text.Encoding.UTF8.GetBytes(lattice.Sentence)
            Dim mblenAt As New Dictionary(Of Integer, Integer)()
            For Each sc In Utf8Helpers.EnumerateScalars(lattice.Sentence)
                mblenAt(sc.Utf8Start) = sc.Utf8Len
            Next

            Dim beginPos As Integer = 0
            While beginPos < len
                Dim mblen As Integer = mblenAt(beginPos)
                Dim hasSingleNode As Boolean = False

                For Each tokBytes As List(Of Byte) In _trie.CommonPrefixSearch(SubArray(sentenceBytes, beginPos))
                    Dim n As Integer = tokBytes.Count
                    Dim tok As String = Global.System.Text.Encoding.UTF8.GetString(tokBytes.ToArray())
                    Dim id As Integer = _tokenToIds(tok)
                    Dim score As Double = _vocab(id).Item2
                    lattice.Insert(beginPos, n, score, id)
                    If (Not hasSingleNode) AndAlso n = mblen Then hasSingleNode = True
                Next

                If Not hasSingleNode Then
                    If _unkId.HasValue Then
                        lattice.Insert(beginPos, mblen, unkScore, _unkId.Value)
                    End If
                End If
                beginPos += mblen
            End While
        End Sub

        ''' <summary>
        ''' Fast forward-DP encoding (Rust <c>encode_optimized</c>, model.rs:255-344): a
        ''' <c>BestPathNode</c> per byte position, trie common-prefix candidates, unk fallback,
        ''' then backtracking with optional unk fusion.
        ''' </summary>
        Private Function EncodeOptimized(sentence As String) As List(Of String)
            Dim size As Integer = Utf8Helpers.Utf8Length(sentence)
            Dim unkScore As Double = _minScore - KUnkPenalty
            Dim bestPathEndsAt(size) As BestPathNode

            Dim sentenceBytes As Byte() = Global.System.Text.Encoding.UTF8.GetBytes(sentence)
            Dim mblenAt As New Dictionary(Of Integer, Integer)()
            For Each sc In Utf8Helpers.EnumerateScalars(sentence)
                mblenAt(sc.Utf8Start) = sc.Utf8Len
            Next

            Dim startsAt As Integer = 0
            While startsAt < size
                Dim bestPathScoreTillHere As Double = bestPathEndsAt(startsAt).BestPathScore
                Dim hasSingleNode As Boolean = False
                Dim mblen As Integer = mblenAt(startsAt)

                For Each tokBytes As List(Of Byte) In _trie.CommonPrefixSearch(SubArray(sentenceBytes, startsAt))
                    Dim keyPos As Integer = startsAt + tokBytes.Count
                    Dim token As String = Global.System.Text.Encoding.UTF8.GetString(tokBytes.ToArray())
                    Dim length As Integer = keyPos - startsAt
                    Dim id As Integer = _tokenToIds(token)
                    Dim score As Double = _vocab(id).Item2
                    Dim candidateScore As Double = score + bestPathScoreTillHere
                    Dim target As BestPathNode = bestPathEndsAt(keyPos)
                    If (Not target.StartsAt.HasValue) OrElse candidateScore > target.BestPathScore Then
                        target.BestPathScore = candidateScore
                        target.StartsAt = startsAt
                        target.Id = id
                        bestPathEndsAt(keyPos) = target
                    End If
                    If (Not hasSingleNode) AndAlso length = mblen Then hasSingleNode = True
                Next

                If Not hasSingleNode Then
                    Dim keyPos As Integer = startsAt + mblen
                    Dim candidateScore As Double = unkScore + bestPathScoreTillHere
                    Dim target As BestPathNode = bestPathEndsAt(keyPos)
                    If (Not target.StartsAt.HasValue) OrElse candidateScore > target.BestPathScore Then
                        target.BestPathScore = candidateScore
                        target.StartsAt = startsAt
                        If Not _unkId.HasValue Then
                            Throw New InvalidOperationException("Encountered an unknown token but `unk_id` is missing")
                        End If
                        target.Id = _unkId.Value
                        bestPathEndsAt(keyPos) = target
                    End If
                End If
                startsAt += mblen
            End While

            Dim endsAt As Integer = size
            Dim results As New List(Of String)()
            Dim fusedParts As New List(Of String)()
            While endsAt > 0
                Dim node As BestPathNode = bestPathEndsAt(endsAt)
                Dim nodeStarts As Integer = node.StartsAt.Value
                If _fuseUnk AndAlso _unkId.HasValue AndAlso node.Id = _unkId.Value Then
                    fusedParts.Add(Utf8Helpers.SliceByUtf8(sentence, nodeStarts, endsAt))
                Else
                    If fusedParts.Count > 0 Then
                        fusedParts.Reverse()
                        results.Add(String.Concat(fusedParts))
                        fusedParts.Clear()
                    End If
                    results.Add(Utf8Helpers.SliceByUtf8(sentence, nodeStarts, endsAt))
                End If
                endsAt = nodeStarts
            End While
            If fusedParts.Count > 0 Then
                fusedParts.Reverse()
                results.Add(String.Concat(fusedParts))
            End If
            results.Reverse()
            Return results
        End Function

        ''' <summary>
        ''' Lattice-based encoding (Rust <c>encode_unoptimized</c>, model.rs:346-380): build a
        ''' lattice, populate nodes, then dispatch on (nbest_size, alpha) to sample_nbest / sample
        ''' / viterbi. Applies fuse_unk over consecutive unk nodes.
        ''' </summary>
        Private Function EncodeUnoptimized(sentence As String) As List(Of String)
            Dim lattice As New Lattice(sentence, _bosId, _eosId, _random)
            PopulateNodes(lattice)

            Dim path As List(Of LatticeNode)
            If _nbestSize.HasValue AndAlso _alpha.HasValue AndAlso _nbestSize.Value > 0 Then
                path = lattice.SampleNbest(_nbestSize.Value, _alpha.Value)
            ElseIf _alpha.HasValue Then
                path = lattice.Sample(_alpha.Value)
            Else
                path = lattice.Viterbi()
            End If

            If _fuseUnk Then
                Dim results As New List(Of String)()
                Dim fused As String = String.Empty
                If Not _unkId.HasValue Then
                    Throw New InvalidOperationException("Encountered an unknown token but `unk_id` is missing")
                End If
                Dim unkIdValue As Integer = _unkId.Value
                For Each node As LatticeNode In path
                    Dim item As String = lattice.Piece(node)
                    If node.Id = unkIdValue Then
                        fused &= item
                    Else
                        If fused.Length > 0 Then
                            results.Add(fused)
                            fused = String.Empty
                        End If
                        results.Add(item)
                    End If
                Next
                If fused.Length > 0 Then results.Add(fused)
                Return results
            Else
                Dim results As New List(Of String)()
                For Each node As LatticeNode In path
                    results.Add(lattice.Piece(node))
                Next
                Return results
            End If
        End Function

        ''' <summary>One DP cell of the optimized encoder (Rust <c>BestPathNode</c>).</summary>
        Private Structure BestPathNode
            Public Id As Integer
            Public BestPathScore As Double
            Public StartsAt As Integer?
        End Structure

        ''' <summary>Returns a copy of <paramref name="bytes"/> from <paramref name="start"/> to the end.</summary>
        Private Shared Function SubArray(bytes As Byte(), start As Integer) As Byte()
            Dim count As Integer = bytes.Length - start
            If count <= 0 Then Return New Byte() {}
            Dim result As Byte() = New Byte(count - 1) {}
            Array.Copy(bytes, start, result, 0, count)
            Return result
        End Function

    End Class

End Namespace
