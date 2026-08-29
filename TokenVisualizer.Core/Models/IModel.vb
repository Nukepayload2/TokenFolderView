Imports System.Text.Json.Nodes

Namespace Models

    ''' <summary>
    ''' The common contract satisfied by every model (<see cref="BpeModel"/>,
    ''' <see cref="WordPieceModel"/>, <see cref="WordLevelModel"/>, <see cref="UnigramModel"/>).
    ''' Mirrors the Rust <c>Model</c> trait for the parts the <c>Tokenizer</c> needs.
    ''' </summary>
    Public Interface IModel
        ''' <summary>Tokenizes the given word into tokens with offsets relative to the word.</summary>
        Function Tokenize(word As String) As List(Of Token)
        ''' <summary>Number of entries in the vocabulary.</summary>
        ReadOnly Property VocabSize As Integer
        ''' <summary>Maps a token to its vocabulary id, or <c>Nothing</c> if absent.</summary>
        Function TokenToId(token As String) As Integer?
        ''' <summary>Maps an id back to its token string, or <c>Nothing</c> if absent.</summary>
        Function IdToToken(id As Integer) As String
        ''' <summary>Returns a copy of the token to id vocabulary.</summary>
        Function GetVocab() As Dictionary(Of String, Integer)
        ''' <summary>Serializes this model to its tokenizer.json representation.</summary>
        Function ToJson() As JsonObject
    End Interface

End Namespace
