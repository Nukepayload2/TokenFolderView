Imports System.Collections.Generic
Imports System.Linq
Imports System.Text.Json.Nodes

Namespace Decoders

    ''' <summary>
    ''' Port of the Rust <c>CTC</c> decoder (decoders/ctc.rs). Sanitizes a list of input tokens:
    ''' removes consecutive duplicates, strips the pad token, and optionally cleans up
    ''' tokenization artifacts (spaces before punctuation and abbreviated English forms).
    ''' </summary>
    Public NotInheritable Class CtcDecoder
        Implements IDecoder

        Private ReadOnly _padToken As String
        Private ReadOnly _wordDelimiterToken As String
        Private ReadOnly _cleanup As Boolean

        Public Sub New(Optional padToken As String = "<pad>",
                       Optional wordDelimiterToken As String = "|",
                       Optional cleanup As Boolean = True)
            _padToken = If(padToken Is Nothing, "<pad>", padToken)
            _wordDelimiterToken = If(wordDelimiterToken Is Nothing, "|", wordDelimiterToken)
            _cleanup = cleanup
        End Sub

        Public Function DecodeChain(tokens As IEnumerable(Of String)) As List(Of String) Implements IDecoder.DecodeChain
            Dim result As New List(Of String)()
            Dim prev As String = Nothing
            Dim first As Boolean = True

            For Each rawToken In If(tokens, Enumerable.Empty(Of String)())
                Dim token As String = If(rawToken, String.Empty)
                If Not first AndAlso token = prev Then Continue For
                first = False
                prev = token

                Dim replaced As String = token.Replace(_padToken, "")
                If _cleanup Then
                    replaced = WordPieceDecoder.Cleanup(replaced).Replace(_wordDelimiterToken, " ")
                End If
                If replaced.Length > 0 Then
                    result.Add(replaced)
                End If
            Next
            Return result
        End Function

        Public Function ToJson() As JsonObject Implements IDecoder.ToJson
            Dim o As New JsonObject()
            o("type") = "CTC"
            o("pad_token") = _padToken
            o("word_delimiter_token") = _wordDelimiterToken
            o("cleanup") = _cleanup
            Return o
        End Function
    End Class

End Namespace
