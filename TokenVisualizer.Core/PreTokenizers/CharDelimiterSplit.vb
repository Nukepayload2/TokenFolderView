Imports System.Text
Imports System.Text.Json.Nodes
Imports Tokenizers.Internal

Namespace PreTokenizers

    ''' <summary>
    ''' Port of the Rust <c>CharDelimiterSplit</c> (pre_tokenizers/delimiter.rs): splits on every
    ''' occurrence of the given delimiter character (removed).
    ''' </summary>
    Public NotInheritable Class CharDelimiterSplit
        Implements IPreTokenizer

        Private ReadOnly _delimiter As Char

        Public Sub New(delimiter As Char)
            _delimiter = delimiter
        End Sub

        Public Sub PreTokenize(pretokenized As PreTokenizedString) Implements IPreTokenizer.PreTokenize
            Dim delimiterCp As Integer = AscW(_delimiter)
            pretokenized.SplitBy(New PredicatePattern(Function(r As Rune) r.Value = delimiterCp), SplitDelimiterBehavior.Removed)
        End Sub

        Public Function ToJson() As JsonObject Implements IPreTokenizer.ToJson
            Dim o As New JsonObject()
            o("type") = "CharDelimiterSplit"
            o("delimiter") = _delimiter.ToString()
            Return o
        End Function
    End Class

End Namespace
