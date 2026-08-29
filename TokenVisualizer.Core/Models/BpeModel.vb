Imports System.Collections.Generic
Imports System.Linq
Imports System.Text
Imports System.Text.Json.Nodes
Imports System.Threading
Imports Tokenizers.Internal

Namespace Models

    ''' <summary>
    ''' A Byte Pair Encoding (BPE) model. Faithful port of the Rust
    ''' <c>models/bpe/model.rs</c> (struct <c>BPE</c>) and <c>models/bpe/word.rs</c>
    ''' (<c>Word::merge_all</c>).
    '''
    ''' The word-to-symbols step mirrors <c>BPE::merge_word</c> and the heap-based
    ''' merge step mirrors <c>Word::merge_all</c> (doubly linked symbols, tombstoning,
    ''' stale-entry guard, rank/pos min-priority queue, optional dropout).
    ''' </summary>
    Public NotInheritable Class BpeModel
        Implements IModel

        Private ReadOnly _vocab As IReadOnlyDictionary(Of String, Integer)
        Private ReadOnly _vocabR As IReadOnlyDictionary(Of Integer, String)
        Private ReadOnly _merges As Dictionary(Of (Integer, Integer), (Integer, Integer))
        ''' <summary>
        ''' Thread-local word→merged-symbols cache. Mirrors the Rust <c>thread_local!</c> cache
        ''' semantics: each thread gets its own independent bounded FIFO cache, so concurrent
        ''' <c>EncodeCount</c> callers (e.g. the scanner's <c>Parallel.ForEach</c>) never contend on
        ''' shared mutable state. <c>Nothing</c> when the cache is disabled (capacity &lt;= 0).
        ''' </summary>
        Private ReadOnly _cache As ThreadLocal(Of Cache(Of String, List(Of (Integer, Integer))))
        Private ReadOnly _dropout As Double?
        Private ReadOnly _unkToken As String
        Private ReadOnly _continuingSubwordPrefix As String
        Private ReadOnly _endOfWordSuffix As String
        Private ReadOnly _fuseUnk As Boolean
        Private ReadOnly _byteFallback As Boolean
        Private ReadOnly _ignoreMerges As Boolean
        Private ReadOnly _random As Func(Of Double)
        ''' <summary>
        ''' Pre-built "<c>0xNN</c>" byte-token strings for byte-fallback, indexed by byte value.
        ''' Only built when byte-fallback is enabled; read-only after construction, so it is safe
        ''' to share across concurrent readers.
        ''' </summary>
        Private ReadOnly _byteTokenStrings As String()
        ''' <summary>
        ''' Code point → id map for vocab keys that are exactly one Unicode scalar. Lets
        ''' <c>BuildSymbols</c> resolve a scalar to its token id without allocating a per-scalar
        ''' <see cref="String"/> for the dictionary lookup (only valid when no subword
        ''' prefix/suffix is applied to that scalar). Read-only after construction.
        ''' </summary>
        Private ReadOnly _singleCharVocab As Dictionary(Of Integer, Integer)

        ''' <summary>
        ''' A single linked-list node used by <see cref="MergeAllSymbols"/>. Mirrors the
        ''' Rust <c>Symbol { c, prev, next, len }</c>. <c>Prev</c>/<c>NextIdx</c> are vector
        ''' indices (-1 = none). Tombstoned nodes have <c>Len = 0</c>.
        ''' </summary>
        Private Structure SymbolNode
            Public C As Integer
            Public Prev As Integer
            Public NextIdx As Integer
            Public Len As Integer

            Public Sub New(c As Integer, prev As Integer, nextIdx As Integer, len As Integer)
                Me.C = c
                Me.Prev = prev
                Me.NextIdx = nextIdx
                Me.Len = len
            End Sub

            ''' <summary>Merges this (left) node with <paramref name="other"/> (right), producing a new node.</summary>
            Public Function MergeWith(other As SymbolNode, newC As Integer) As SymbolNode
                Dim result As SymbolNode = Me
                result.C = newC
                result.Len = result.Len + other.Len
                result.NextIdx = other.NextIdx
                Return result
            End Function
        End Structure

        ''' <summary>A pending merge in the priority queue: position, rank and replacement id.</summary>
        Private Structure MergeNode
            Public Pos As Integer
            Public Rank As Integer
            Public NewId As Integer

            Public Sub New(pos As Integer, rank As Integer, newId As Integer)
                Me.Pos = pos
                Me.Rank = rank
                Me.NewId = newId
            End Sub
        End Structure

        ''' <summary>
        ''' Creates a BPE model.
        ''' </summary>
        ''' <param name="vocab">Token to id mapping.</param>
        ''' <param name="merges">Merge lines in the form <c>"left right"</c>; rank is the list index.</param>
        ''' <param name="continuingSubwordPrefix">Optional prefix prepended to non-first subwords (e.g. "##").</param>
        ''' <param name="endOfWordSuffix">Optional suffix appended to the final subword.</param>
        ''' <param name="unkToken">Unknown-token string; must be present in <paramref name="vocab"/> when set.</param>
        ''' <param name="fuseUnk">Whether consecutive unknown chars are fused into a single unk token.</param>
        ''' <param name="byteFallback">Emit <c>"&lt;0xXX&gt;"</c> byte tokens instead of unk for unknown chars.</param>
        ''' <param name="dropout">Merge-dropout probability in [0, 1]; <c>Nothing</c> disables it.</param>
        ''' <param name="ignoreMerges">If the whole word is in the vocab, emit it directly as one token.</param>
        ''' <param name="cacheCapacity">Cache capacity; 0 disables caching.</param>
        ''' <param name="seededRandom">A <see cref="Random"/> or <see cref="Func(Of Double)"/> used only when dropout is set.</param>
        Public Sub New(vocab As IDictionary(Of String, Integer),
                       merges As IReadOnlyList(Of String),
                       Optional continuingSubwordPrefix As String = Nothing,
                       Optional endOfWordSuffix As String = Nothing,
                       Optional unkToken As String = Nothing,
                       Optional fuseUnk As Boolean = False,
                       Optional byteFallback As Boolean = False,
                       Optional dropout As Double? = Nothing,
                       Optional ignoreMerges As Boolean = False,
                       Optional cacheCapacity As Integer = 10000,
                       Optional seededRandom As Object = Nothing)
            If dropout.HasValue AndAlso (dropout.Value < 0.0 OrElse dropout.Value > 1.0) Then
                Throw New ArgumentOutOfRangeException(NameOf(dropout), "dropout must be in the range [0.0, 1.0]")
            End If

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

            _continuingSubwordPrefix = continuingSubwordPrefix
            _endOfWordSuffix = endOfWordSuffix
            _unkToken = unkToken
            _fuseUnk = fuseUnk
            _byteFallback = byteFallback
            _dropout = dropout
            _ignoreMerges = ignoreMerges

            If byteFallback Then
                Dim byteTokens As String() = New String(255) {}
                For b As Integer = 0 To 255
                    byteTokens(b) = $"<0x{b:X2}>"
                Next
                _byteTokenStrings = byteTokens
            Else
                _byteTokenStrings = Nothing
            End If

            _vocab = New Dictionary(Of String, Integer)(vocab)

            Dim vocabR As New Dictionary(Of Integer, String)()
            For Each kvp As KeyValuePair(Of String, Integer) In _vocab
                vocabR(kvp.Value) = kvp.Key
            Next
            _vocabR = vocabR

            ' Single-scalar vocab keys, indexed by code point, so BuildSymbols can resolve a
            ' scalar without allocating a per-scalar String (hot BPE path).
            Dim singleChar As New Dictionary(Of Integer, Integer)()
            For Each kvp As KeyValuePair(Of String, Integer) In _vocab
                Dim k As String = kvp.Key
                If k IsNot Nothing AndAlso Utf8Helpers.ScalarCount(k) = 1 Then
                    singleChar(UnicodePredicates.ScalarCodePoint(k, 0)) = kvp.Value
                End If
            Next
            _singleCharVocab = singleChar

            Dim prefixLen As Integer = If(_continuingSubwordPrefix Is Nothing, 0, _continuingSubwordPrefix.Length)

            ' Build the merge map. The Rust builder returns a MergeTokenOutOfVocabulary error for
            ' any missing token; a constructor cannot surface that error, so (per the task) such
            ' merge lines are skipped instead.
            _merges = New Dictionary(Of (Integer, Integer), (Integer, Integer))()
            For i As Integer = 0 To merges.Count - 1
                Dim line As String = merges(i)
                Dim parts() As String = line.Split(" "c)
                If parts.Length <> 2 Then
                    Throw New ArgumentException($"Invalid merge line at index {i}: '{line}'", NameOf(merges))
                End If
                Dim a As String = parts(0)
                Dim b As String = parts(1)
                Dim aId As Integer
                Dim bId As Integer
                If Not _vocab.TryGetValue(a, aId) Then Continue For
                If Not _vocab.TryGetValue(b, bId) Then Continue For
                If prefixLen > b.Length Then Continue For
                ' Concatenate a + b, stripping continuing_subword_prefix from the start of b
                ' (mirrors model.rs:264-269, which slices off prefix_len bytes of b).
                Dim newToken As String = a & b.Substring(prefixLen)
                Dim newId As Integer
                If Not _vocab.TryGetValue(newToken, newId) Then Continue For
                _merges((aId, bId)) = (i, newId)
            Next

            If cacheCapacity <= 0 Then
                _cache = Nothing
            Else
                ' One bounded FIFO cache per thread, created lazily on first access. Capacity and
                ' eviction are kept per-thread, matching the Rust thread-local cache.
                _cache = New ThreadLocal(Of Cache(Of String, List(Of (Integer, Integer))))(
                    Function() New Cache(Of String, List(Of (Integer, Integer)))(cacheCapacity))
            End If
        End Sub

        ''' <summary>Number of entries in the vocabulary.</summary>
        Public ReadOnly Property VocabSize As Integer Implements IModel.VocabSize
            Get
                Return _vocab.Count
            End Get
        End Property

        ''' <summary>Number of merge rules in the merge map.</summary>
        Public ReadOnly Property MergeCount As Integer
            Get
                Return _merges.Count
            End Get
        End Property

        ''' <summary>Maps a token to its vocabulary id, or <c>Nothing</c> if absent.</summary>
        Public Function TokenToId(token As String) As Integer? Implements IModel.TokenToId
            Dim id As Integer
            If _vocab.TryGetValue(token, id) Then Return id
            Return Nothing
        End Function

        ''' <summary>Maps an id back to its token string, or <c>Nothing</c> if absent.</summary>
        Public Function IdToToken(id As Integer) As String Implements IModel.IdToToken
            Dim token As String = Nothing
            If _vocabR.TryGetValue(id, token) Then Return token
            Return Nothing
        End Function

        ''' <summary>Returns a copy of the token to id vocabulary.</summary>
        Public Function GetVocab() As Dictionary(Of String, Integer) Implements IModel.GetVocab
            Dim d As New Dictionary(Of String, Integer)()
            For Each kv As KeyValuePair(Of String, Integer) In _vocab
                d(kv.Key) = kv.Value
            Next
            Return d
        End Function

        ''' <summary>
        ''' Serializes this model to its tokenizer.json representation. Mirrors the Rust
        ''' <c>BPE</c> serialization (models/bpe/serialization.rs). Vocab is emitted in id order,
        ''' merges in rank order as space-joined strings.
        ''' </summary>
        Public Function ToJson() As JsonObject Implements IModel.ToJson
            Dim o As New JsonObject()
            o("type") = "BPE"
            o("dropout") = If(_dropout.HasValue, JsonValue.Create(_dropout.Value), Nothing)
            o("unk_token") = If(_unkToken Is Nothing, Nothing, JsonValue.Create(_unkToken))
            o("continuing_subword_prefix") = If(_continuingSubwordPrefix Is Nothing, Nothing, JsonValue.Create(_continuingSubwordPrefix))
            o("end_of_word_suffix") = If(_endOfWordSuffix Is Nothing, Nothing, JsonValue.Create(_endOfWordSuffix))
            o("fuse_unk") = _fuseUnk
            o("byte_fallback") = _byteFallback
            o("ignore_merges") = _ignoreMerges

            Dim vocab As New JsonObject()
            If _vocabR.Count > 0 Then
                For i As Integer = 0 To _vocabR.Keys.Max()
                    Dim token As String = Nothing
                    If _vocabR.TryGetValue(i, token) Then vocab(token) = i
                Next
            End If
            o("vocab") = vocab

            Dim mergeArray As New JsonArray()
            For Each mergeEntry As KeyValuePair(Of (Integer, Integer), (Integer, Integer)) In _merges.OrderBy(Function(pair) pair.Value.Item1)
                mergeArray.Add(_vocabR(mergeEntry.Key.Item1) & " " & _vocabR(mergeEntry.Key.Item2))
            Next
            o("merges") = mergeArray
            Return o
        End Function

        ''' <summary>
        ''' Builds the per-scalar symbols for <paramref name="word"/> (with
        ''' continuing_subword_prefix / end_of_word_suffix / unk / fuse / byteFallback handling)
        ''' and then merges them. Mirrors Rust <c>BPE::merge_word</c>. Returns the final
        ''' (id, byteLen) symbols.
        ''' </summary>
        Public Function MergeWord(word As String,
                                  Optional scratch As List(Of (Integer, Integer)) = Nothing) As List(Of (Integer, Integer))
            Dim symbols As List(Of (Integer, Integer)) = BuildSymbols(word, scratch)
            Return MergeAllSymbols(symbols)
        End Function

        ''' <summary>
        ''' Runs the BPE merge algorithm over the given (id, byteLen) symbols and returns the
        ''' surviving ids in order. Mirrors Rust <c>Word::merge_all</c>.
        ''' </summary>
        Public Function MergeAll(symbols As List(Of (Integer, Integer))) As List(Of Integer)
            Dim merged As List(Of (Integer, Integer)) = MergeAllSymbols(symbols)
            Dim ids As New List(Of Integer)(merged.Count)
            For Each s As (Integer, Integer) In merged
                ids.Add(s.Item1)
            Next
            Return ids
        End Function

        ''' <summary>
        ''' Tokenizes a word. Caches the merged symbols keyed by the word (only when no dropout,
        ''' mirroring Rust <c>tokenize_with_cache</c>); the cache is best-effort.
        ''' </summary>
        Public Function Tokenize(word As String) As List(Of Token) Implements IModel.Tokenize
            If word Is Nothing OrElse word.Length = 0 Then
                Return New List(Of Token)()
            End If

            ' ignore_merges: if the whole word is in the vocab, emit it directly.
            ' (Rust model.rs:559-567 — this is the *only* effect of ignore_merges; the word is
            ' still merged otherwise.)
            If _ignoreMerges Then
                Dim wholeId As Integer
                If _vocab.TryGetValue(word, wholeId) Then
                    Dim byteLen As Integer = Utf8Helpers.Utf8Length(word)
                    Return New List(Of Token)() From {New Token(wholeId, word, (0, byteLen))}
                End If
            End If

            Dim symbols As List(Of (Integer, Integer)) = Nothing

            Dim useCache As Boolean = (_cache IsNot Nothing) AndAlso (Not _dropout.HasValue OrElse _dropout.Value = 0.0)
            If useCache Then
                Dim cache As Cache(Of String, List(Of (Integer, Integer))) = _cache.Value
                Dim cached As List(Of (Integer, Integer)) = cache.GetValue(word)
                If cached IsNot Nothing Then
                    symbols = cached
                End If
            End If

            If symbols Is Nothing Then
                symbols = MergeWord(word)
                If useCache Then
                    _cache.Value.Insert(word, symbols)
                End If
            End If

            ' word_to_tokens: cumulative byte offsets (Rust word.rs:260-268).
            Dim tokens As New List(Of Token)(symbols.Count)
            Dim pos As Integer = 0
            For Each sym As (Integer, Integer) In symbols
                Dim newPos As Integer = pos + sym.Item2
                Dim value As String = _vocabR(sym.Item1)
                tokens.Add(New Token(sym.Item1, value, (pos, newPos)))
                pos = newPos
            Next
            Return tokens
        End Function

        ''' <summary>
        ''' Mirrors Rust <c>BPE::merge_word</c> (model.rs:465-550) up to (but not including)
        ''' <c>merge_all</c>: iterates the word's Unicode scalars and emits one symbol per
        ''' vocab token (or byte/unk symbols), tracking unk accumulation. Byte lengths are the
        ''' RAW scalar byte lengths captured before any prefix/suffix.
        ''' </summary>
        Private Function BuildSymbols(word As String,
                                      Optional scratch As List(Of (Integer, Integer)) = Nothing) As List(Of (Integer, Integer))
            Dim symbols As List(Of (Integer, Integer)) = If(scratch Is Nothing, New List(Of (Integer, Integer))(), scratch)
            symbols.Clear()

            Dim unk As (Integer, Integer)? = Nothing

            ' Iterate the word's scalars by .NET index (each word is small, so computing the
            ' scalar count up front is cheap). No List(Of ScalarInfo) is materialized.
            Dim net As Integer = 0
            Dim scalarIdx As Integer = 0
            Dim totalScalars As Integer = Utf8Helpers.ScalarCount(word)
            While net < word.Length
                Dim cp As Integer = UnicodePredicates.ScalarCodePoint(word, net)
                Dim isFirst As Boolean = (scalarIdx = 0)
                Dim isLast As Boolean = (scalarIdx = totalScalars - 1)

                Dim byteLen As Integer = Utf8Helpers.Utf8LengthOfCodePoint(cp)

                ' Fast path: when no subword prefix/suffix is applied to this scalar, resolve it
                ' through the code-point map so no per-scalar String is allocated.
                Dim id As Integer
                Dim matched As Boolean = False
                If (_continuingSubwordPrefix Is Nothing OrElse isFirst) AndAlso
                   (_endOfWordSuffix Is Nothing OrElse isLast) AndAlso
                   _singleCharVocab.TryGetValue(cp, id) Then
                    matched = True
                End If

                Dim rawChar As String = Nothing
                If Not matched Then
                    rawChar = Utf8Helpers.ScalarToString(cp)
                    Dim s As String = rawChar
                    If Not isFirst AndAlso _continuingSubwordPrefix IsNot Nothing Then
                        s = _continuingSubwordPrefix & s
                    End If
                    If isLast AndAlso _endOfWordSuffix IsNot Nothing Then
                        s = s & _endOfWordSuffix
                    End If
                    matched = _vocab.TryGetValue(s, id)
                End If

                If matched Then
                    If unk.HasValue Then
                        symbols.Add(unk.Value)
                        unk = Nothing
                    End If
                    symbols.Add((id, byteLen))
                Else
                    If _byteFallback Then
                        ' Per the task, byte-fallback is applied to the RAW char's UTF-8 bytes
                        ' (the Rust applies it to s; with byteFallback the two coincide because
                        ' byteFallback models do not use subword prefixes/suffixes).
                        Dim bytes As Byte() = Global.System.Text.Encoding.UTF8.GetBytes(rawChar)
                        Dim allPresent As Boolean = True
                        For Each b As Byte In bytes
                            If Not _vocab.ContainsKey(_byteTokenStrings(b)) Then
                                allPresent = False
                                Exit For
                            End If
                        Next
                        If allPresent Then
                            For Each b As Byte In bytes
                                symbols.Add((_vocab(_byteTokenStrings(b)), 1))
                            Next
                            net += Utf8Helpers.NetLengthOfCodePoint(cp)
                            scalarIdx += 1
                            Continue While
                        End If
                    End If

                    If _unkToken IsNot Nothing Then
                        Dim unkId As Integer
                        If Not _vocab.TryGetValue(_unkToken, unkId) Then
                            Throw New InvalidOperationException($"Unk token '{_unkToken}' is not in the vocabulary")
                        End If

                        If unk.HasValue Then
                            If _fuseUnk Then
                                ' Fuse unk: accumulate byte length.
                                unk = (unk.Value.Item1, unk.Value.Item2 + byteLen)
                            Else
                                ' Do not fuse: flush the previous unk, start a new one.
                                symbols.Add(unk.Value)
                                unk = (unkId, byteLen)
                            End If
                        Else
                            unk = (unkId, byteLen)
                        End If
                    End If
                    ' NOTE: when there is no unk token and no byteFallback match, the char is
                    ' silently omitted (Rust behavior).
                End If
                net += Utf8Helpers.NetLengthOfCodePoint(cp)
                scalarIdx += 1
            End While

            If unk.HasValue Then
                symbols.Add(unk.Value)
            End If

            Return symbols
        End Function

        ''' <summary>
        ''' Faithful port of Rust <c>Word::merge_all</c> (word.rs:162-250): a doubly linked list
        ''' of symbols, a min-priority queue keyed on (rank, pos), tombstoned removed symbols,
        ''' a stale-entry guard, and optional dropout. Returns the surviving (id, byteLen) pairs.
        ''' </summary>
        Private Function MergeAllSymbols(symbols As List(Of (Integer, Integer))) As List(Of (Integer, Integer))
            Dim count As Integer = symbols.Count
            Dim nodes As New List(Of SymbolNode)(count)
            For i As Integer = 0 To count - 1
                Dim prev As Integer = If(i = 0, -1, i - 1)
                Dim nxt As Integer = If(i = count - 1, -1, i + 1)
                nodes.Add(New SymbolNode(symbols(i).Item1, prev, nxt, symbols(i).Item2))
            Next

            Dim queue As New PriorityQueue(Of MergeNode, (Integer, Integer))()
            Dim skip As New List(Of MergeNode)()

            ' Seed all adjacent pairs present in the merge map (by vector index, like Rust).
            For i As Integer = 0 To count - 2
                Dim pair As (Integer, Integer) = (nodes(i).C, nodes(i + 1).C)
                Dim mv As (Integer, Integer)
                If _merges.TryGetValue(pair, mv) Then
                    Dim rank As Integer = mv.Item1
                    queue.Enqueue(New MergeNode(i, rank, mv.Item2), (rank, i))
                End If
            Next

            While queue.Count > 0
                Dim top As MergeNode = queue.Dequeue()

                If _dropout.HasValue AndAlso _random() < _dropout.Value Then
                    ' Dropout: skip this merge, defer re-queueing until the next non-dropped pop.
                    skip.Add(top)
                    Continue While
                End If

                ' Re-insert the skipped elements (Rust word.rs:184-185).
                For Each m As MergeNode In skip
                    queue.Enqueue(m, (m.Rank, m.Pos))
                Next
                skip.Clear()

                Dim left As SymbolNode = nodes(top.Pos)
                ' Skip if the left symbol is tombstoned.
                If left.Len = 0 Then Continue While
                ' Do nothing if we are the last symbol.
                If left.NextIdx = -1 Then Continue While

                Dim nextPos As Integer = left.NextIdx
                Dim right As SymbolNode = nodes(nextPos)

                ' Stale-entry guard: recheck the CURRENT (left, right) pair and require the
                ' replacement id to match what this queue entry expects.
                Dim targetNewPair As (Integer, Integer) = (left.C, right.C)
                Dim tVal As (Integer, Integer)
                If (Not _merges.TryGetValue(targetNewPair, tVal)) OrElse tVal.Item2 <> top.NewId Then
                    Continue While
                End If

                ' Fold right into left and tombstone right.
                nodes(top.Pos) = left.MergeWith(right, top.NewId)
                Dim tomb As SymbolNode = nodes(nextPos)
                tomb.Len = 0
                nodes(nextPos) = tomb

                ' Update prev on the new next.
                If right.NextIdx > -1 AndAlso right.NextIdx < nodes.Count Then
                    Dim nn As SymbolNode = nodes(right.NextIdx)
                    nn.Prev = top.Pos
                    nodes(right.NextIdx) = nn
                End If

                ' Insert the new pair formed with the previous symbol.
                Dim current As SymbolNode = nodes(top.Pos)
                If current.Prev >= 0 Then
                    Dim prevSym As SymbolNode = nodes(current.Prev)
                    Dim newPair As (Integer, Integer) = (prevSym.C, current.C)
                    Dim pmv As (Integer, Integer)
                    If _merges.TryGetValue(newPair, pmv) Then
                        queue.Enqueue(New MergeNode(current.Prev, pmv.Item1, pmv.Item2), (pmv.Item1, current.Prev))
                    End If
                End If

                ' Insert the new pair formed with the next symbol.
                Dim nxtIdx As Integer = current.NextIdx
                If nxtIdx >= 0 AndAlso nxtIdx < nodes.Count Then
                    Dim nextSym As SymbolNode = nodes(nxtIdx)
                    Dim np2 As (Integer, Integer) = (current.C, nextSym.C)
                    Dim nmv As (Integer, Integer)
                    If _merges.TryGetValue(np2, nmv) Then
                        queue.Enqueue(New MergeNode(top.Pos, nmv.Item1, nmv.Item2), (nmv.Item1, top.Pos))
                    End If
                End If
            End While

            ' Drop tombstoned symbols.
            Dim result As New List(Of (Integer, Integer))()
            For Each s As SymbolNode In nodes
                If s.Len <> 0 Then
                    result.Add((s.C, s.Len))
                End If
            Next
            Return result
        End Function

    End Class

End Namespace
