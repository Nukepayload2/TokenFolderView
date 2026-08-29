Imports System.Text.Json.Nodes
Imports Tokenizers.Internal

Namespace Normalizers

    ''' <summary>
    ''' Port of the Rust <c>Replace</c> normalizer (normalizers/replace.rs).
    ''' Replaces every occurrence of a pattern (a literal string or a regex) with the given
    ''' content. The Decoder half is implemented elsewhere (P6).
    ''' </summary>
    Public NotInheritable Class ReplaceNormalizer
        Implements INormalizer

        Private ReadOnly _pattern As Pattern
        Private ReadOnly _content As String
        Private ReadOnly _patternKind As String
        Private ReadOnly _patternString As String

        ''' <summary>
        ''' Creates a Replace normalizer. <paramref name="patternKind"/> is "String" for a
        ''' literal pattern or "Regex" for a regular expression (canonical patterns are routed
        ''' to their hand-written manual scanners).
        ''' </summary>
        Public Sub New(patternKind As String, pattern As String, content As String)
            ' NOTE: fully-qualify `Pattern` here because VB is case-insensitive and the
            ' constructor parameter `pattern` would otherwise shadow the Pattern type.
            _pattern = Global.Tokenizers.Internal.Pattern.Create(patternKind, pattern)
            _content = If(content Is Nothing, String.Empty, content)
            _patternKind = patternKind
            _patternString = pattern
        End Sub

        Public Sub Normalize(normalized As Internal.NormalizedString) Implements INormalizer.Normalize
            normalized.Replace(_pattern, _content)
        End Sub

        Public Function ToJson() As JsonObject Implements INormalizer.ToJson
            Dim o As New JsonObject()
            o("type") = "Replace"
            Dim patternObj As New JsonObject()
            patternObj(_patternKind) = _patternString
            o("pattern") = patternObj
            o("content") = _content
            Return o
        End Function
    End Class

End Namespace
