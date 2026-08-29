Imports System.Text.Json.Nodes
Imports Tokenizers.Internal

Namespace PreTokenizers

    ''' <summary>
    ''' Port of the Rust <c>Whitespace</c> pre-tokenizer (pre_tokenizers/whitespace.rs): splits on
    ''' the inverse of <c>\w+|[^\w\s]+</c>, i.e. on whitespace (removed).
    ''' </summary>
    Public NotInheritable Class WhitespacePreTokenizer
        Implements IPreTokenizer

        Public Sub PreTokenize(pretokenized As PreTokenizedString) Implements IPreTokenizer.PreTokenize
            Dim pattern As Pattern = ManualPatternFactory.TryCreate(WordPunctPattern.Canonical)
            If pattern Is Nothing Then pattern = New RegexPattern(WordPunctPattern.Canonical)
            pretokenized.SplitBy(New InvertPattern(pattern), SplitDelimiterBehavior.Removed)
        End Sub

        Public Function ToJson() As JsonObject Implements IPreTokenizer.ToJson
            Dim o As New JsonObject()
            o("type") = "Whitespace"
            Return o
        End Function
    End Class

End Namespace
