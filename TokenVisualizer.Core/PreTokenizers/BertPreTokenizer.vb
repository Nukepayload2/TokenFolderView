Imports System.Text
Imports System.Text.Json.Nodes
Imports Tokenizers.Internal

Namespace PreTokenizers

    ''' <summary>
    ''' Port of the Rust <c>BertPreTokenizer</c> (pre_tokenizers/bert.rs): splits on whitespace
    ''' (removed), then on punctuation (isolated).
    ''' </summary>
    Public NotInheritable Class BertPreTokenizer
        Implements IPreTokenizer

        Public Sub PreTokenize(pretokenized As PreTokenizedString) Implements IPreTokenizer.PreTokenize
            pretokenized.SplitBy(New PredicatePattern(AddressOf IsWhiteSpaceScalar), SplitDelimiterBehavior.Removed)
            pretokenized.SplitBy(New PredicatePattern(AddressOf IsBertPunc), SplitDelimiterBehavior.Isolated)
        End Sub

        Private Shared Function IsWhiteSpaceScalar(r As Rune) As Boolean
            Return Rune.IsWhiteSpace(r)
        End Function

        ''' <summary>Rust <c>is_bert_punc</c>: ASCII punctuation or Unicode P* category.</summary>
        Private Shared Function IsBertPunc(r As Rune) As Boolean
            Dim cp As Integer = r.Value
            Return (cp >= &H21 AndAlso cp <= &H2F) OrElse
                   (cp >= &H3A AndAlso cp <= &H40) OrElse
                   (cp >= &H5B AndAlso cp <= &H60) OrElse
                   (cp >= &H7B AndAlso cp <= &H7E) OrElse
                   Rune.IsPunctuation(r)
        End Function

        Public Function ToJson() As JsonObject Implements IPreTokenizer.ToJson
            Dim o As New JsonObject()
            o("type") = "BertPreTokenizer"
            Return o
        End Function
    End Class

End Namespace
