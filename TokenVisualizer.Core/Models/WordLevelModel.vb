Imports System.Collections.Generic
Imports System.Linq
Imports System.Text.Json.Nodes
Imports Tokenizers.Internal

Namespace Models

    ''' <summary>
    ''' A whole-word vocabulary model. Faithful port of the Rust
    ''' <c>models/wordlevel/mod.rs</c> <c>WordLevel::tokenize</c> (lines 162-178): if the whole
    ''' word is in the vocabulary emit it directly, else fall back to the unk token.
    ''' </summary>
    Public NotInheritable Class WordLevelModel
        Implements IModel

        Private ReadOnly _vocab As IReadOnlyDictionary(Of String, Integer)
        Private ReadOnly _vocabR As IReadOnlyDictionary(Of Integer, String)
        Private ReadOnly _unkToken As String

        ''' <summary>
        ''' Creates a WordLevel model.
        ''' </summary>
        ''' <param name="vocab">Token to id mapping.</param>
        ''' <param name="unkToken">Unknown-token string (default "<unk>").</param>
        Public Sub New(vocab As IDictionary(Of String, Integer),
                       Optional unkToken As String = "<unk>")
            _vocab = New Dictionary(Of String, Integer)(vocab)
            Dim vocabR As New Dictionary(Of Integer, String)()
            For Each kvp As KeyValuePair(Of String, Integer) In _vocab
                vocabR(kvp.Value) = kvp.Key
            Next
            _vocabR = vocabR
            _unkToken = unkToken
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
        ''' <c>WordLevel</c> serialization (models/wordlevel/serialization.rs).
        ''' </summary>
        Public Function ToJson() As JsonObject Implements IModel.ToJson
            Dim o As New JsonObject()
            o("type") = "WordLevel"

            Dim vocab As New JsonObject()
            If _vocabR.Count > 0 Then
                For i As Integer = 0 To _vocabR.Keys.Max()
                    Dim token As String = Nothing
                    If _vocabR.TryGetValue(i, token) Then vocab(token) = i
                Next
            End If
            o("vocab") = vocab
            o("unk_token") = JsonValue.Create(_unkToken)
            Return o
        End Function

        ''' <summary>
        ''' Tokenizes a word. Emits one token for the whole word (with its byte offsets) or the
        ''' unk token with the whole word's offsets; throws when neither the word nor the unk token
        ''' is in the vocabulary.
        ''' </summary>
        Public Function Tokenize(word As String) As List(Of Token) Implements IModel.Tokenize
            Dim byteLen As Integer = Utf8Helpers.Utf8Length(word)
            Dim id As Integer
            If _vocab.TryGetValue(word, id) Then
                Return New List(Of Token)() From {New Token(id, word, (0, byteLen))}
            End If
            If _vocab.TryGetValue(_unkToken, id) Then
                Return New List(Of Token)() From {New Token(id, _unkToken, (0, byteLen))}
            End If
            Throw New InvalidOperationException("WordLevel error: Missing [UNK] token from the vocabulary")
        End Function

        ''' <summary>
        ''' Count-only fallback: the WordLevel model is not the count fast path, so reuse
        ''' <see cref="Tokenize"/> and take its length. Equal to <c>Tokenize(word).Count</c> by
        ''' construction.
        ''' </summary>
        Public Function CountTokens(word As String) As Integer Implements IModel.CountTokens
            Return Tokenize(word).Count
        End Function

    End Class

End Namespace
