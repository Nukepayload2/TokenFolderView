Imports System.Collections.Generic
Imports System.Linq
Imports System.Text.Json.Nodes
Imports Tokenizers.Internal

Namespace Models

    ''' <summary>
    ''' A WordPiece model. Faithful port of the Rust <c>models/wordpiece/mod.rs</c>
    ''' <c>WordPiece::tokenize</c> (lines 224-283): greedy longest-prefix segmentation with a
    ''' continuing-subword prefix and char-aware shrinking that never splits a codepoint.
    ''' All offsets are UTF-8 byte offsets into the input word.
    ''' </summary>
    Public NotInheritable Class WordPieceModel
        Implements IModel

        Private ReadOnly _vocab As IReadOnlyDictionary(Of String, Integer)
        Private ReadOnly _vocabR As IReadOnlyDictionary(Of Integer, String)
        Private ReadOnly _unkToken As String
        Private ReadOnly _continuingSubwordPrefix As String
        Private ReadOnly _maxInputCharsPerWord As Integer

        ''' <summary>
        ''' Creates a WordPiece model.
        ''' </summary>
        ''' <param name="vocab">Token to id mapping.</param>
        ''' <param name="unkToken">Unknown-token string; must be present in <paramref name="vocab"/> to tokenize unknowns.</param>
        ''' <param name="continuingSubwordPrefix">Prefix prepended to non-first subwords (e.g. "##").</param>
        ''' <param name="maxInputCharsPerWord">Max Unicode scalars per word before the whole word becomes unk.</param>
        Public Sub New(vocab As IDictionary(Of String, Integer),
                       Optional unkToken As String = "[UNK]",
                       Optional continuingSubwordPrefix As String = "##",
                       Optional maxInputCharsPerWord As Integer = 100)
            _vocab = New Dictionary(Of String, Integer)(vocab)
            Dim vocabR As New Dictionary(Of Integer, String)()
            For Each kvp As KeyValuePair(Of String, Integer) In _vocab
                vocabR(kvp.Value) = kvp.Key
            Next
            _vocabR = vocabR
            _unkToken = unkToken
            _continuingSubwordPrefix = continuingSubwordPrefix
            _maxInputCharsPerWord = maxInputCharsPerWord
        End Sub

        ''' <summary>Number of entries in the vocabulary.</summary>
        Public ReadOnly Property VocabSize As Integer Implements IModel.VocabSize
            Get
                Return _vocab.Count
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
        ''' <c>WordPiece</c> serialization (models/wordpiece/serialization.rs).
        ''' </summary>
        Public Function ToJson() As JsonObject Implements IModel.ToJson
            Dim o As New JsonObject()
            o("type") = "WordPiece"
            o("unk_token") = JsonValue.Create(_unkToken)
            o("continuing_subword_prefix") = JsonValue.Create(_continuingSubwordPrefix)
            o("max_input_chars_per_word") = _maxInputCharsPerWord

            Dim vocab As New JsonObject()
            If _vocabR.Count > 0 Then
                For i As Integer = 0 To _vocabR.Keys.Max()
                    Dim token As String = Nothing
                    If _vocabR.TryGetValue(i, token) Then vocab(token) = i
                Next
            End If
            o("vocab") = vocab
            Return o
        End Function

        ''' <summary>
        ''' Segments a word into subword tokens. Mirrors Rust <c>WordPiece::tokenize</c>: when the
        ''' word's scalar count exceeds <c>max_input_chars_per_word</c>, or any substring cannot be
        ''' segmented, the whole word is replaced by a single unk token.
        ''' </summary>
        Public Function Tokenize(word As String) As List(Of Token) Implements IModel.Tokenize
            Dim charCount As Integer = Utf8Helpers.ScalarCount(word)
            Dim byteLen As Integer = Utf8Helpers.Utf8Length(word)

            If charCount > _maxInputCharsPerWord Then
                Return New List(Of Token)() From {New Token(GetUnkId(), _unkToken, (0, byteLen))}
            End If

            ' Map each UTF-8 byte-boundary that ends a scalar to that scalar's byte length, so the
            ' inner loop can shrink `end` by exactly one whole char.
            Dim lenEndingAt As New Dictionary(Of Integer, Integer)()
            For Each sc In Utf8Helpers.EnumerateScalars(word)
                lenEndingAt(sc.Utf8Start + sc.Utf8Len) = sc.Utf8Len
            Next

            Dim isBad As Boolean = False
            Dim start As Integer = 0
            Dim subTokens As New List(Of Token)()

            While start < byteLen
                Dim finish As Integer = byteLen
                Dim curStr As Token? = Nothing

                While start < finish
                    Dim substr As String = Utf8Helpers.SliceByUtf8(word, start, finish)
                    If start > 0 Then
                        substr = _continuingSubwordPrefix & substr
                    End If

                    Dim id As Integer
                    If _vocab.TryGetValue(substr, id) Then
                        ' The value includes the continuing prefix; the offsets cover only the raw
                        ' substring (mirrors wordpiece/mod.rs:251-257).
                        curStr = New Token(id, substr, (start, finish))
                        Exit While
                    End If

                    ' Shrink by one whole UTF-8 char at the end of the current substring.
                    ' (Rust: end -= substr.chars().last().map_or(1, |c| c.len_utf8()); the last char
                    ' of the prefixed string is the last char of the raw substring.)
                    Dim shrink As Integer
                    If Not lenEndingAt.TryGetValue(finish, shrink) Then shrink = 1
                    finish -= shrink
                End While

                If Not curStr.HasValue Then
                    isBad = True
                    Exit While
                End If

                subTokens.Add(curStr.Value)
                start = finish
            End While

            If isBad Then
                Return New List(Of Token)() From {New Token(GetUnkId(), _unkToken, (0, byteLen))}
            End If
            Return subTokens
        End Function

        ''' <summary>
        ''' Count-only fallback: the WordPiece model is not the count fast path, so reuse
        ''' <see cref="Tokenize"/> and take its length. Equal to <c>Tokenize(word).Count</c> by
        ''' construction.
        ''' </summary>
        Public Function CountTokens(word As String) As Integer Implements IModel.CountTokens
            Return Tokenize(word).Count
        End Function

        Private Function GetUnkId() As Integer
            Dim id As Integer
            If _vocab.TryGetValue(_unkToken, id) Then Return id
            Throw New InvalidOperationException("WordPiece error: Missing [UNK] token from the vocabulary")
        End Function

    End Class

End Namespace
