Imports System.Text
Imports System.Text.Json.Nodes
Imports Tokenizers.Internal

Namespace PreTokenizers

    ''' <summary>
    ''' Port of the Rust <c>Digits</c> pre-tokenizer (pre_tokenizers/digits.rs). Splits on numbers,
    ''' either as contiguous runs or as each individual digit.
    ''' </summary>
    Public NotInheritable Class DigitsPreTokenizer
        Implements IPreTokenizer

        Private ReadOnly _individualDigits As Boolean

        Public Sub New(individualDigits As Boolean)
            _individualDigits = individualDigits
        End Sub

        Public Sub New()
            Me.New(False)
        End Sub

        Public Sub PreTokenize(pretokenized As PreTokenizedString) Implements IPreTokenizer.PreTokenize
            Dim pattern As New PredicatePattern(AddressOf IsNumber)
            If _individualDigits Then
                pretokenized.SplitBy(pattern, SplitDelimiterBehavior.Isolated)
            Else
                pretokenized.SplitBy(pattern, SplitDelimiterBehavior.Contiguous)
            End If
        End Sub

        ''' <summary>Rust <c>char::is_numeric</c>: Nd/Nl/No categories.</summary>
        Private Shared Function IsNumber(r As Rune) As Boolean
            Return Rune.IsNumber(r)
        End Function

        Public Function ToJson() As JsonObject Implements IPreTokenizer.ToJson
            Dim o As New JsonObject()
            o("type") = "Digits"
            o("individual_digits") = _individualDigits
            Return o
        End Function
    End Class

End Namespace
