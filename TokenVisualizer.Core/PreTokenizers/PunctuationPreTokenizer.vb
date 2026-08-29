Imports System.Text
Imports System.Text.Json.Nodes
Imports Tokenizers.Internal
Imports Tokenizers.Serialization

Namespace PreTokenizers

    ''' <summary>
    ''' Port of the Rust <c>Punctuation</c> pre-tokenizer (pre_tokenizers/punctuation.rs). Splits
    ''' on punctuation (ASCII punctuation or Unicode P*) with the configured behavior.
    ''' </summary>
    Public NotInheritable Class PunctuationPreTokenizer
        Implements IPreTokenizer

        Private ReadOnly _behavior As SplitDelimiterBehavior

        Public Sub New(behavior As SplitDelimiterBehavior)
            _behavior = behavior
        End Sub

        Public Sub New()
            Me.New(SplitDelimiterBehavior.Isolated)
        End Sub

        Public Sub PreTokenize(pretokenized As PreTokenizedString) Implements IPreTokenizer.PreTokenize
            pretokenized.SplitBy(New PredicatePattern(AddressOf IsPunc), _behavior)
        End Sub

        ''' <summary>Rust <c>is_punc</c>: ASCII punctuation or Unicode P* category.</summary>
        Private Shared Function IsPunc(r As Rune) As Boolean
            Dim cp As Integer = r.Value
            Return (cp >= &H21 AndAlso cp <= &H2F) OrElse
                   (cp >= &H3A AndAlso cp <= &H40) OrElse
                   (cp >= &H5B AndAlso cp <= &H60) OrElse
                   (cp >= &H7B AndAlso cp <= &H7E) OrElse
                   Rune.IsPunctuation(r)
        End Function

        Public Function ToJson() As JsonObject Implements IPreTokenizer.ToJson
            Dim o As New JsonObject()
            o("type") = "Punctuation"
            o("behavior") = SerializationHelpers.SplitDelimiterBehaviorToString(_behavior)
            Return o
        End Function
    End Class

End Namespace
