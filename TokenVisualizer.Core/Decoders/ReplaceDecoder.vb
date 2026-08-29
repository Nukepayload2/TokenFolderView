Imports System.Collections.Generic
Imports System.Linq
Imports System.Text
Imports System.Text.Json.Nodes
Imports Tokenizers.Internal

Namespace Decoders

    ''' <summary>
    ''' Port of the Rust <c>Replace</c> decoder (normalizers/replace.rs). Replaces every match of
    ''' a pattern (a literal string or a regex) with the given content, per token.
    ''' </summary>
    Public NotInheritable Class ReplaceDecoder
        Implements IDecoder

        Private ReadOnly _pattern As Pattern
        Private ReadOnly _content As String
        Private ReadOnly _patternKind As String
        Private ReadOnly _patternString As String

        ''' <summary>
        ''' Creates a Replace decoder. <paramref name="patternKind"/> is "String" for a literal
        ''' pattern or "Regex" for a regular expression.
        ''' </summary>
        Public Sub New(patternKind As String, pattern As String, content As String)
            ' NOTE: fully-qualify `Pattern` here because VB is case-insensitive and the
            ' constructor parameter `pattern` would otherwise shadow the Pattern type.
            _pattern = Global.Tokenizers.Internal.Pattern.Create(patternKind, pattern)
            _content = If(content Is Nothing, String.Empty, content)
            _patternKind = patternKind
            _patternString = pattern
        End Sub

        Public Function DecodeChain(tokens As IEnumerable(Of String)) As List(Of String) Implements IDecoder.DecodeChain
            Dim result As New List(Of String)()
            For Each rawToken In If(tokens, Enumerable.Empty(Of String)())
                Dim token As String = If(rawToken, String.Empty)
                Dim sb As New StringBuilder()
                For Each m As MatchInfo In _pattern.FindMatches(token)
                    If m.IsMatch Then
                        sb.Append(_content)
                    Else
                        sb.Append(Utf8Helpers.SliceByUtf8(token, m.Start, m.End))
                    End If
                Next
                result.Add(sb.ToString())
            Next
            Return result
        End Function

        Public Function ToJson() As JsonObject Implements IDecoder.ToJson
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
