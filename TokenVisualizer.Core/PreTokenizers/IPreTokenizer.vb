Imports System.Text.Json.Nodes
Imports Tokenizers.Internal

Namespace PreTokenizers

    ''' <summary>
    ''' A <c>PreTokenizer</c> splits a <see cref="PreTokenizedString"/> into sub-parts. Mirrors the
    ''' Rust <c>tokenizers::PreTokenizer</c> trait (<c>pre_tokenize</c>).
    ''' </summary>
    Public Interface IPreTokenizer
        ''' <summary>Pre-tokenizes the given <see cref="PreTokenizedString"/> in place.</summary>
        Sub PreTokenize(pretokenized As PreTokenizedString)
        ''' <summary>Serializes this pre-tokenizer to its tokenizer.json representation.</summary>
        Function ToJson() As JsonObject
    End Interface

End Namespace
