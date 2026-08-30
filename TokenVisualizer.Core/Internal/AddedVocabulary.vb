Imports System.Linq
Imports Tokenizers.Models
Imports Tokenizers.Normalizers

Namespace Internal

    ''' <summary>
    ''' A token added by the user on top of the existing Model vocabulary. Faithful port of the
    ''' Rust <c>AddedToken</c>. <see cref="Equals"/> compares all flags (matching the Rust derived
    ''' <c>PartialEq</c>), while <see cref="GetHashCode"/> is by content only (matching the Rust
    ''' <c>Hash</c> impl).
    ''' </summary>
    Public Class AddedToken
        ''' <summary>The content of the added token (original, as provided by the user).</summary>
        Public Content As String
        ''' <summary>Whether this token must be a single word or can break words.</summary>
        Public SingleWord As Boolean
        ''' <summary>Whether this token should strip whitespaces on its left.</summary>
        Public LStrip As Boolean
        ''' <summary>Whether this token should strip whitespaces on its right.</summary>
        Public RStrip As Boolean
        ''' <summary>Whether this token should be normalized.</summary>
        Public Normalized As Boolean
        ''' <summary>Whether this token is special.</summary>
        Public Special As Boolean

        ''' <summary>
        ''' Builds a token from the given content, specifying if it is intended to be a special
        ''' token. Special tokens are not normalized by default. Mirrors <c>AddedToken::from</c>.
        ''' </summary>
        Public Sub New(content As String, special As Boolean)
            Me.Content = content
            Me.Normalized = Not special
            Me.Special = special
            Me.SingleWord = False
            Me.LStrip = False
            Me.RStrip = False
        End Sub

        Public Shared Function From(content As String, special As Boolean) As AddedToken
            Return New AddedToken(content, special)
        End Function

        ''' <summary>Specifies whether this token should only match on whole single words.</summary>
        Public Function WithSingleWord(singleWord As Boolean) As AddedToken
            Me.SingleWord = singleWord
            Return Me
        End Function

        ''' <summary>Specifies whether this token should include all the whitespaces on its left.</summary>
        Public Function WithLStrip(lstrip As Boolean) As AddedToken
            Me.LStrip = lstrip
            Return Me
        End Function

        ''' <summary>Specifies whether this token should include all the whitespaces on its right.</summary>
        Public Function WithRStrip(rstrip As Boolean) As AddedToken
            Me.RStrip = rstrip
            Return Me
        End Function

        ''' <summary>
        ''' Specifies whether this token should be normalized and match against its normalized
        ''' version in the input text.
        ''' </summary>
        Public Function WithNormalized(normalized As Boolean) As AddedToken
            Me.Normalized = normalized
            Return Me
        End Function

        ''' <summary>Specifies whether this token is special (skipped when decoding).</summary>
        Public Function WithSpecial(special As Boolean) As AddedToken
            Me.Special = special
            Return Me
        End Function

        Public Overrides Function Equals(obj As Object) As Boolean
            Dim other As AddedToken = TryCast(obj, AddedToken)
            If other Is Nothing Then Return False
            Return Me.Content = other.Content AndAlso
                   Me.SingleWord = other.SingleWord AndAlso
                   Me.LStrip = other.LStrip AndAlso
                   Me.RStrip = other.RStrip AndAlso
                   Me.Normalized = other.Normalized AndAlso
                   Me.Special = other.Special
        End Function

        Public Overrides Function GetHashCode() As Integer
            Return If(Content Is Nothing, 0, Content.GetHashCode())
        End Function

        Public Overrides Function ToString() As String
            Return If(Content, "")
        End Function
    End Class

    ''' <summary>
    ''' A vocabulary built on top of the Model, providing a way to add tokens to an already
    ''' trained model. Faithful port of the Rust <c>AddedVocabulary</c>.
    ''' </summary>
    Public Class AddedVocabulary

        Private _addedTokensMap As New Dictionary(Of String, Integer)()
        Private _addedTokensMapR As New Dictionary(Of Integer, AddedToken)()
        Private _specialTokensSet As New HashSet(Of String)()
        Private _normalizedCache As New Dictionary(Of Integer, String)()
        Private _splitTrie As CharTrie
        Private _splitTrieIds As New Dictionary(Of String, Integer)()
        Private _splitNormalizedTrie As CharTrie
        Private _splitNormalizedTrieIds As New Dictionary(Of String, Integer)()
        Private _encodeSpecialTokens As Boolean

        ''' <summary>
        ''' The underlying model vocabulary (token content to id). Used to assign ids to added
        ''' tokens that already exist in the model. Set by the tests / tokenizer before adding.
        ''' </summary>
        Public Property ModelVocab As Dictionary(Of String, Integer)

        ''' <summary>
        ''' The normalizer used to cache the normalized form of <c>normalized = true</c> added
        ''' tokens when they are added. Mirrors the normalizer argument of the Rust
        ''' <c>add_tokens</c>.
        ''' </summary>
        Public Property Normalizer As INormalizer

        ''' <summary>Size of the additional vocabulary.</summary>
        Public ReadOnly Property Count As Integer
            Get
                Return _addedTokensMap.Count
            End Get
        End Property

        ''' <summary>The additional vocabulary (token content to id).</summary>
        Public ReadOnly Property AddedTokensMap As Dictionary(Of String, Integer)
            Get
                Return _addedTokensMap
            End Get
        End Property

        ''' <summary>The additional vocabulary with the <see cref="AddedToken"/>s (id to token).</summary>
        Public ReadOnly Property AddedTokensDecoder As Dictionary(Of Integer, AddedToken)
            Get
                Return _addedTokensMapR
            End Get
        End Property

        ''' <summary>The non-normalized split trie (used by tests).</summary>
        Public ReadOnly Property SplitTrie As CharTrie
            Get
                Return _splitTrie
            End Get
        End Property

        ''' <summary>The id lookup for the non-normalized split trie (used by tests).</summary>
        Public ReadOnly Property SplitTrieIds As Dictionary(Of String, Integer)
            Get
                Return _splitTrieIds
            End Get
        End Property

        ''' <summary>
        ''' Get the id matching one of our tokens if it exists, checking the added vocabulary
        ''' first and then the model vocabulary. Mirrors the Rust <c>token_to_id</c>.
        ''' </summary>
        Public Function TokenToId(content As String) As Integer?
            Dim id As Integer
            If _addedTokensMap.TryGetValue(content, id) Then Return id
            If Me.ModelVocab IsNot Nothing AndAlso Me.ModelVocab.TryGetValue(content, id) Then Return id
            Return Nothing
        End Function

        ''' <summary>
        ''' Returns the string form of an added token used during decoding: the cached normalized
        ''' form when available, otherwise the original content.
        ''' </summary>
        Public Function SimpleIdToToken(id As Integer) As String
            Dim token As AddedToken = Nothing
            If _addedTokensMapR.TryGetValue(id, token) Then
                Dim cached As String = Nothing
                If _normalizedCache.TryGetValue(id, cached) Then Return cached
                Return token.Content
            End If
            Return Nothing
        End Function

        ''' <summary>Checks whether the given token content is a special token.</summary>
        Public Function IsSpecialToken(content As String) As Boolean
            Return _specialTokensSet.Contains(content)
        End Function

        ''' <summary>Checks whether the token with the given id is a special token.</summary>
        Public Function IsSpecialToken(id As Integer) As Boolean
            Dim token As AddedToken = Nothing
            If _addedTokensMapR.TryGetValue(id, token) Then Return token.Special
            Return False
        End Function

        Public Sub SetEncodeSpecialTokens(value As Boolean)
            _encodeSpecialTokens = value
        End Sub

        Public Function GetEncodeSpecialTokens() As Boolean
            Return _encodeSpecialTokens
        End Function

        ''' <summary>Adds some special tokens to the vocabulary. Mirrors the Rust <c>add_special_tokens</c>.</summary>
        Public Function AddSpecialTokens(modelVocabSize As Integer, tokens As IEnumerable(Of AddedToken)) As Integer
            Return Me.AddTokens(modelVocabSize, tokens)
        End Function

        ''' <summary>
        ''' Adds some tokens to the vocabulary, assigning ids exactly like the Rust
        ''' <c>add_tokens</c> (including reuse of model ids and duplicate skipping). Returns the
        ''' number of added tokens.
        ''' </summary>
        Public Function AddTokens(modelVocabSize As Integer, tokens As IEnumerable(Of AddedToken)) As Integer
            Dim ignored As Integer = 0
            Dim total As Integer = 0

            Dim maxId As Integer? = Nothing
            For Each key In _addedTokensMapR.Keys
                If Not maxId.HasValue OrElse key > maxId.Value Then maxId = key
            Next
            Dim nextId As Integer
            If Not maxId.HasValue Then
                nextId = modelVocabSize
            ElseIf maxId.Value >= modelVocabSize OrElse modelVocabSize = 0 Then
                nextId = maxId.Value + 1
            Else
                nextId = modelVocabSize
            End If

            For Each token In tokens
                total += 1
                If token.Content Is Nothing OrElse token.Content.Length = 0 Then
                    ignored += 1
                    Continue For
                End If

                ' Fast path: skip if this content is already in the map with identical properties.
                Dim existingId As Integer
                If _addedTokensMap.TryGetValue(token.Content, existingId) Then
                    Dim existingToken As AddedToken = Nothing
                    If _addedTokensMapR.TryGetValue(existingId, existingToken) AndAlso existingToken.Equals(token) Then
                        ignored += 1
                        Continue For
                    End If
                End If

                Dim newId As Integer
                Dim existingIdOpt As Integer? = Me.TokenToId(token.Content)
                If existingIdOpt.HasValue Then
                    newId = existingIdOpt.Value
                Else
                    newId = nextId
                    nextId += 1
                End If

                If token.Normalized Then
                    If Me.Normalizer IsNot Nothing Then
                        Dim s As NormalizedString = NormalizedString.FromString(token.Content)
                        Me.Normalizer.Normalize(s)
                        Dim normed As String = s.Get
                        If normed <> token.Content Then
                            _normalizedCache(newId) = normed
                        End If
                    End If
                End If

                _addedTokensMap(token.Content) = newId

                Dim isNewSpecial As Boolean = token.Special AndAlso
                    Not token.Content Is Nothing AndAlso token.Content.Length > 0 AndAlso
                    Not _specialTokensSet.Contains(token.Content)
                If isNewSpecial Then
                    _specialTokensSet.Add(token.Content)
                End If

                _addedTokensMapR(newId) = token
            Next

            Me.RefreshAddedTokens()

            ' Return the number of added tokens.
            Return total - ignored
        End Function

        ''' <summary>
        ''' Re-applies normalization to every added token that has <c>normalized = true</c>, then
        ''' rebuilds the matching tries. Mirrors the Rust <c>refresh_normalized_tokens</c>.
        ''' </summary>
        Public Sub RefreshNormalizedTokens(normalizer As INormalizer)
            _normalizedCache.Clear()
            For Each kv In _addedTokensMapR
                Dim id As Integer = kv.Key
                Dim token As AddedToken = kv.Value
                If token.Normalized Then
                    If normalizer IsNot Nothing Then
                        Dim s As NormalizedString = NormalizedString.FromString(token.Content)
                        normalizer.Normalize(s)
                        Dim normed As String = s.Get
                        If normed <> token.Content Then
                            _normalizedCache(id) = normed
                        End If
                    End If
                End If
            Next
            Me.RefreshAddedTokens()
        End Sub

        ''' <summary>
        ''' Extracts the additional vocabulary from the given sentence, normalizing it along the
        ''' way. Mirrors the Rust <c>extract_and_normalize</c> two-pass algorithm.
        ''' </summary>
        Public Function ExtractAndNormalize(rawText As String, normalizer As INormalizer) As PreTokenizedString
            Dim pretokenized As PreTokenizedString = PreTokenizedString.FromString(rawText)

            ' 1. Extract all the non-normalized tokens from the non-normalized string.
            Me.SplitUsingTrie(pretokenized, Me._splitTrie, Me._splitTrieIds)

            ' 2. Then extract the normalized tokens from the normalized pieces of the string.
            Dim newSplits As New List(Of Split)()
            For Each split In pretokenized.Splits
                If split.Tokens IsNot Nothing Then
                    newSplits.Add(split)
                    Continue For
                End If

                If normalizer IsNot Nothing Then
                    normalizer.Normalize(split.Normalized)
                End If

                Dim matches As List(Of (Integer?, (Integer, Integer))) =
                    Me.FindMatches(split.Normalized.Get, Me._splitNormalizedTrie, Me._splitNormalizedTrieIds)
                AppendMatches(split, matches, newSplits)
            Next
            pretokenized.Splits = newSplits

            Return pretokenized
        End Function

        ''' <summary>
        ''' Splits every untokenized split of <paramref name="pretokenized"/> on the given trie,
        ''' attaching a single token to each matched piece. Empty pieces are dropped. Mirrors the
        ''' Rust <c>split_with_indices</c> over <c>PreTokenizedString::split</c>.
        ''' </summary>
        Private Sub SplitUsingTrie(pretokenized As PreTokenizedString, trie As CharTrie, trieIds As Dictionary(Of String, Integer))
            Dim newSplits As New List(Of Split)()
            For Each split In pretokenized.Splits
                If split.Tokens IsNot Nothing Then
                    newSplits.Add(split)
                    Continue For
                End If
                Dim matches As List(Of (Integer?, (Integer, Integer))) = Me.FindMatches(split.Normalized.Get, trie, trieIds)
                AppendMatches(split, matches, newSplits)
            Next
            pretokenized.Splits = newSplits
        End Sub

        ''' <summary>Appends the slices corresponding to <paramref name="matches"/> to <paramref name="newSplits"/>.</summary>
        Private Shared Sub AppendMatches(split As Split, matches As List(Of (Integer?, (Integer, Integer))), newSplits As List(Of Split))
            Dim wholeLen As Integer = Utf8Helpers.Utf8Length(split.Normalized.Get)
            For Each m In matches
                ' Fast path: a non-token match covering the whole normalized string (the common
                ' "no added token found" case) is reused as-is instead of being copied through a
                ' full-range Slice, which would rebuild the 2M-entry alignment list and the
                ' scalar-boundary index arrays for the whole text.
                If m.Item2.Item1 = 0 AndAlso m.Item2.Item2 = wholeLen AndAlso Not m.Item1.HasValue Then
                    newSplits.Add(Split.FromNormalizedString(split.Normalized))
                    Continue For
                End If

                Dim slice As NormalizedString = split.Normalized.Slice(New OffsetRange(False, m.Item2.Item1, m.Item2.Item2))
                If slice.IsEmpty() Then Continue For
                If m.Item1.HasValue Then
                    Dim value As String = slice.Get
                    Dim len As Integer = Utf8Helpers.Utf8Length(value)
                    Dim tokens As New List(Of Token) From {New Token(m.Item1.Value, value, (0, len))}
                    newSplits.Add(New Split(slice, tokens))
                Else
                    newSplits.Add(Split.FromNormalizedString(slice))
                End If
            Next
        End Sub

        ''' <summary>
        ''' Reconstructs the split tries (non-normalized and normalized) when tokens are added or
        ''' the normalizer changes. Mirrors the Rust <c>refresh_added_tokens</c>.
        ''' </summary>
        Private Sub RefreshAddedTokens()
            Dim normalizedPairs As New List(Of (String, Integer))()
            Dim nonNormalizedPairs As New List(Of (String, Integer))()
            For Each kv In _addedTokensMapR
                Dim id As Integer = kv.Key
                Dim token As AddedToken = kv.Value
                If token.Normalized Then
                    Dim pattern As String = token.Content
                    Dim cached As String = Nothing
                    If _normalizedCache.TryGetValue(id, cached) Then pattern = cached
                    normalizedPairs.Add((pattern, id))
                Else
                    nonNormalizedPairs.Add((token.Content, id))
                End If
            Next

            _splitTrie = Nothing
            _splitTrieIds = New Dictionary(Of String, Integer)()
            If nonNormalizedPairs.Count > 0 Then
                Dim trie As New CharTrie()
                For Each pair In nonNormalizedPairs
                    trie.Push(pair.Item1)
                    _splitTrieIds(pair.Item1) = pair.Item2
                Next
                _splitTrie = trie
            End If

            _splitNormalizedTrie = Nothing
            _splitNormalizedTrieIds = New Dictionary(Of String, Integer)()
            If normalizedPairs.Count > 0 Then
                Dim trie As New CharTrie()
                For Each pair In normalizedPairs
                    trie.Push(pair.Item1)
                    _splitNormalizedTrieIds(pair.Item1) = pair.Item2
                Next
                _splitNormalizedTrie = trie
            End If
        End Sub

        ''' <summary>
        ''' Finds any AddedToken in the given sentence using the provided trie. Returns a list of
        ''' "splits", each a pair of byte Offsets and an optional id (present when it is an AddedToken).
        ''' The list of splits covers the entire input string. Mirrors the Rust <c>find_matches</c>
        ''' (LeftmostLongest via <see cref="CharTrie"/>), including the <c>single_word</c>,
        ''' <c>lstrip</c>, <c>rstrip</c> and <c>encode_special_tokens</c> behaviors.
        ''' </summary>
        Public Function FindMatches(sentence As String, splitRe As CharTrie, trieIds As Dictionary(Of String, Integer)) As List(Of (Integer?, (Integer, Integer)))
            Dim result As New List(Of (Integer?, (Integer, Integer)))()
            If sentence Is Nothing OrElse sentence.Length = 0 Then
                result.Add((Nothing, (0, 0)))
                Return result
            End If
            If splitRe Is Nothing Then
                result.Add((Nothing, (0, Utf8Helpers.Utf8Length(sentence))))
                Return result
            End If

            Dim byteLen As Integer = Utf8Helpers.Utf8Length(sentence)
            Dim startOffset As Integer = 0
            Dim netLen As Integer = sentence.Length
            Dim netIdx As Integer = 0
            ' Running UTF-8 byte offset of netIdx, kept incrementally. Recomputing it from the
            ' string start per trie match (the old Utf8Helpers.NetIndexToUtf8) made the matcher
            ' O(n * matches): real code contains the tokenizer.json's own added-token literals, so
            ' ~1000+ matches each paid an O(n) rescan of the whole text. The counter mirrors
            ' NetIndexToUtf8 exactly (ScalarCodePoint + Utf8LengthOfCodePoint per scalar), so the
            ' emitted byte offsets are byte-identical.
            Dim byteOff As Integer = 0
            While netIdx < netLen
                Dim netMatchEnd As Integer = splitRe.FindLongestPrefix(sentence, netIdx)
                If netMatchEnd <= netIdx Then
                    Dim cp As Integer = UnicodePredicates.ScalarCodePoint(sentence, netIdx)
                    byteOff += Utf8Helpers.Utf8LengthOfCodePoint(cp)
                    netIdx += ScalarNetLen(sentence, netIdx)
                    Continue While
                End If

                Dim matched As String = sentence.Substring(netIdx, netMatchEnd - netIdx)
                Dim id As Integer = trieIds(matched)
                Dim addedToken As AddedToken = _addedTokensMapR(id)

                Dim startByte As Integer = byteOff
                ' True byte offset of netMatchEnd. LStrip/RStrip may extend the emitted
                ' (startByte, stopByte) range, but the running counter must track netIdx exactly,
                ' so byteOff is always advanced to matchEndByte (never the adjusted stopByte).
                Dim matchEndByte As Integer = byteOff + Utf8Helpers.Utf8Length(matched)
                Dim stopByte As Integer = matchEndByte

                If Me._encodeSpecialTokens AndAlso _specialTokensSet.Contains(addedToken.Content) Then
                    byteOff = matchEndByte
                    netIdx = netMatchEnd
                    Continue While
                End If

                If addedToken.SingleWord Then
                    Dim startSpace As Boolean = startByte = 0 OrElse Not EndsWithWord(sentence, netIdx)
                    Dim stopSpace As Boolean = stopByte = byteLen OrElse Not StartsWithWord(sentence, netMatchEnd)
                    If Not stopSpace OrElse Not startSpace Then
                        ' Discard: not a single word.
                        byteOff = matchEndByte
                        netIdx = netMatchEnd
                        Continue While
                    End If
                End If

                If addedToken.LStrip Then
                    ' This will be strictly inferior to start and in correct sentence offset.
                    Dim newStartByte As Integer = SpaceLeftmostAtEnd(sentence, netIdx)
                    ' The previous match could have already matched those spaces; ignore them.
                    startByte = Math.Max(newStartByte, startOffset)
                End If

                If addedToken.RStrip Then
                    stopByte += SpaceRightmostAtStart(sentence, netMatchEnd)
                End If

                If startOffset < startByte Then
                    result.Add((Nothing, (startOffset, startByte)))
                End If
                result.Add((id, (startByte, stopByte)))
                startOffset = stopByte
                byteOff = matchEndByte
                netIdx = netMatchEnd
            End While

            Dim totalByteLen As Integer = Utf8Helpers.Utf8Length(sentence)
            If startOffset <> totalByteLen Then
                result.Add((Nothing, (startOffset, totalByteLen)))
            End If

            Return result
        End Function

        ''' <summary>Whether the scalar immediately before the given .NET index is a <c>\w</c> char.</summary>
        Private Shared Function EndsWithWord(sentence As String, netIdx As Integer) As Boolean
            If netIdx <= 0 Then Return False
            Dim idx As Integer = netIdx - 1
            If idx > 0 AndAlso Char.IsLowSurrogate(sentence(idx)) AndAlso Char.IsHighSurrogate(sentence(idx - 1)) Then
                idx -= 1
            End If
            Return UnicodePredicates.IsWord(sentence, idx)
        End Function

        ''' <summary>Whether the scalar at the given .NET index is a <c>\w</c> char.</summary>
        Private Shared Function StartsWithWord(sentence As String, netIdx As Integer) As Boolean
            If netIdx < 0 OrElse netIdx >= sentence.Length Then Return False
            Return UnicodePredicates.IsWord(sentence, netIdx)
        End Function

        ''' <summary>
        ''' Byte offset of the start of the trailing whitespace run ending at the given .NET index.
        ''' Mirrors the Rust <c>space_leftmost_at_end</c> (<c>\s*$</c>).
        ''' </summary>
        Private Shared Function SpaceLeftmostAtEnd(sentence As String, netIdx As Integer) As Integer
            Dim i As Integer = netIdx
            While i > 0
                Dim prev As Integer = i - 1
                If prev > 0 AndAlso Char.IsLowSurrogate(sentence(prev)) AndAlso Char.IsHighSurrogate(sentence(prev - 1)) Then
                    If UnicodePredicates.IsWhiteSpace(sentence, prev - 1) Then
                        i = prev - 1
                    Else
                        Exit While
                    End If
                Else
                    If UnicodePredicates.IsWhiteSpace(sentence, prev) Then
                        i = prev
                    Else
                        Exit While
                    End If
                End If
            End While
            Return Utf8Helpers.NetIndexToUtf8(sentence, i)
        End Function

        ''' <summary>
        ''' Byte length of the whitespace run starting at the given .NET index. Mirrors the Rust
        ''' <c>space_rightmost_at_start</c> (<c>^\s*</c>).
        ''' </summary>
        Private Shared Function SpaceRightmostAtStart(sentence As String, netIdx As Integer) As Integer
            Dim i As Integer = netIdx
            While i < sentence.Length
                If UnicodePredicates.IsWhiteSpace(sentence, i) Then
                    i += ScalarNetLen(sentence, i)
                Else
                    Exit While
                End If
            End While
            Return Utf8Helpers.NetIndexToUtf8(sentence, i) - Utf8Helpers.NetIndexToUtf8(sentence, netIdx)
        End Function

        ''' <summary>.NET (UTF-16 code-unit) length of the scalar at the given index (1 or 2).</summary>
        Private Shared Function ScalarNetLen(sentence As String, netIdx As Integer) As Integer
            If netIdx < sentence.Length AndAlso Char.IsHighSurrogate(sentence(netIdx)) AndAlso
               netIdx + 1 < sentence.Length AndAlso Char.IsLowSurrogate(sentence(netIdx + 1)) Then
                Return 2
            End If
            Return 1
        End Function

    End Class

End Namespace
