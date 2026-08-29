Imports System.Collections.Generic
Imports System.Linq
Imports System.Text.Json.Nodes

Namespace Decoders

    ''' <summary>
    ''' Port of the Rust <c>WordPiece</c> decoder (decoders/wordpiece.rs). Removes the
    ''' continuation prefix from tokens (prepending a space when a token is not a continuation)
    ''' and optionally cleans up tokenization artifacts.
    ''' </summary>
    Public NotInheritable Class WordPieceDecoder
        Implements IDecoder

        Private ReadOnly _prefix As String
        Private ReadOnly _cleanup As Boolean

        Public Sub New(Optional prefix As String = "##", Optional cleanup As Boolean = True)
            _prefix = If(prefix Is Nothing, "##", prefix)
            _cleanup = cleanup
        End Sub

        ''' <summary>
        ''' Cleans up some tokenization artifacts: spaces before punctuation and some abbreviated
        ''' English forms. Mirrors the Rust <c>wordpiece::cleanup</c>.
        ''' </summary>
        Public Shared Function Cleanup(s As String) As String
            If s Is Nothing Then Return String.Empty
            Return s.Replace(" .", ".").
                      Replace(" ?", "?").
                      Replace(" !", "!").
                      Replace(" ,", ",").
                      Replace(" ' ", "'").
                      Replace(" n't", "n't").
                      Replace(" 'm", "'m").
                      Replace(" do not", " don't").
                      Replace(" 's", "'s").
                      Replace(" 've", "'ve").
                      Replace(" 're", "'re")
        End Function

        Public Function DecodeChain(tokens As IEnumerable(Of String)) As List(Of String) Implements IDecoder.DecodeChain
            Dim result As New List(Of String)()
            Dim i As Integer = 0
            For Each rawToken In If(tokens, Enumerable.Empty(Of String)())
                Dim token As String = If(rawToken, String.Empty)
                If i <> 0 Then
                    If token.StartsWith(_prefix, StringComparison.Ordinal) Then
                        token = token.Substring(_prefix.Length)
                    Else
                        token = " " & token
                    End If
                End If
                If _cleanup Then
                    token = Cleanup(token)
                End If
                result.Add(token)
                i += 1
            Next
            Return result
        End Function

        Public Function ToJson() As JsonObject Implements IDecoder.ToJson
            Dim o As New JsonObject()
            o("type") = "WordPiece"
            o("prefix") = _prefix
            o("cleanup") = _cleanup
            Return o
        End Function
    End Class

End Namespace
