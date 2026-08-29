Imports System.Collections.Generic
Imports System.Linq
Imports System.Text
Imports System.Text.Json.Nodes

Namespace Decoders

    ''' <summary>
    ''' Port of the Rust <c>Strip</c> decoder (decoders/strip.rs). Strips up to <c>start</c>
    ''' leading characters equal to <c>content</c> and up to <c>stop</c> trailing characters
    ''' equal to <c>content</c>, per token.
    ''' </summary>
    Public NotInheritable Class StripDecoder
        Implements IDecoder

        Private ReadOnly _content As Char
        Private ReadOnly _start As Integer
        Private ReadOnly _stop As Integer

        Public Sub New(content As Char, start As Integer, [stop] As Integer)
            _content = content
            _start = start
            _stop = [stop]
        End Sub

        Public Function DecodeChain(tokens As IEnumerable(Of String)) As List(Of String) Implements IDecoder.DecodeChain
            Dim result As New List(Of String)()
            For Each rawToken In If(tokens, Enumerable.Empty(Of String)())
                Dim token As String = If(rawToken, String.Empty)
                Dim chars As List(Of Char) = token.ToList()

                Dim startCut As Integer = 0
                Dim startLimit As Integer = Math.Min(_start, chars.Count)
                For i As Integer = 0 To startLimit - 1
                    If chars(i) = _content Then
                        startCut = i + 1
                    Else
                        Exit For
                    End If
                Next

                Dim stopCut As Integer = chars.Count
                Dim stopLimit As Integer = Math.Min(_stop, chars.Count)
                For i As Integer = 0 To stopLimit - 1
                    Dim index As Integer = chars.Count - i - 1
                    If chars(index) = _content Then
                        stopCut = index
                    Else
                        Exit For
                    End If
                Next

                Dim sb As New StringBuilder()
                For i As Integer = startCut To stopCut - 1
                    sb.Append(chars(i))
                Next
                result.Add(sb.ToString())
            Next
            Return result
        End Function

        Public Function ToJson() As JsonObject Implements IDecoder.ToJson
            Dim o As New JsonObject()
            o("type") = "Strip"
            o("content") = _content.ToString()
            o("start") = _start
            o("stop") = _stop
            Return o
        End Function
    End Class

End Namespace
