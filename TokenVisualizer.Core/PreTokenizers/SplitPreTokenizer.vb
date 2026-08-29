Imports System.Text.Json.Nodes
Imports Tokenizers.Internal
Imports Tokenizers.Serialization

Namespace PreTokenizers

    ''' <summary>
    ''' Port of the Rust <c>Split</c> pre-tokenizer (pre_tokenizers/split.rs). Splits on a pattern
    ''' (string or regex), optionally inverting it.
    ''' </summary>
    Public NotInheritable Class SplitPreTokenizer
        Implements IPreTokenizer

        Private ReadOnly _pattern As Pattern
        Private ReadOnly _patternKind As String
        Private ReadOnly _patternString As String
        Private ReadOnly _behavior As SplitDelimiterBehavior
        Private ReadOnly _invert As Boolean

        Public Sub New(patternKind As String, patternString As String, behavior As SplitDelimiterBehavior, invert As Boolean)
            _pattern = Pattern.Create(patternKind, patternString)
            _patternKind = patternKind
            _patternString = patternString
            _behavior = behavior
            _invert = invert
        End Sub

        Public Sub PreTokenize(pretokenized As PreTokenizedString) Implements IPreTokenizer.PreTokenize
            If _invert Then
                pretokenized.SplitBy(New InvertPattern(_pattern), _behavior)
            Else
                pretokenized.SplitBy(_pattern, _behavior)
            End If
        End Sub

        Public Function ToJson() As JsonObject Implements IPreTokenizer.ToJson
            Dim o As New JsonObject()
            o("type") = "Split"
            Dim patternObj As New JsonObject()
            patternObj(_patternKind) = _patternString
            o("pattern") = patternObj
            o("behavior") = SerializationHelpers.SplitDelimiterBehaviorToString(_behavior)
            o("invert") = _invert
            Return o
        End Function
    End Class

End Namespace
