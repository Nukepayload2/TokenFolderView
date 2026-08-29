Imports System.Text
Imports System.Text.Json.Nodes
Imports Tokenizers.Internal

Namespace PreTokenizers

    ''' <summary>
    ''' Port of the Rust <c>WhitespaceSplit</c> pre-tokenizer (pre_tokenizers/whitespace.rs): splits
    ''' on whitespace (removed).
    ''' </summary>
    Public NotInheritable Class WhitespaceSplitPreTokenizer
        Implements IPreTokenizer

        Public Sub PreTokenize(pretokenized As PreTokenizedString) Implements IPreTokenizer.PreTokenize
            pretokenized.SplitBy(New PredicatePattern(AddressOf IsWhiteSpaceScalar), SplitDelimiterBehavior.Removed)
        End Sub

        Private Shared Function IsWhiteSpaceScalar(r As Rune) As Boolean
            Return Rune.IsWhiteSpace(r)
        End Function

        Public Function ToJson() As JsonObject Implements IPreTokenizer.ToJson
            Dim o As New JsonObject()
            o("type") = "WhitespaceSplit"
            Return o
        End Function
    End Class

End Namespace
