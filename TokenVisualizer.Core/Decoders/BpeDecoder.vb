Imports System.Collections.Generic
Imports System.Linq
Imports System.Text.Json.Nodes

Namespace Decoders

    ''' <summary>
    ''' Port of the Rust <c>BPEDecoder</c> (decoders/bpe.rs). Joins tokens by replacing the
    ''' end-of-word suffix with a space, except on the last token.
    ''' </summary>
    Public NotInheritable Class BpeDecoder
        Implements IDecoder

        Private ReadOnly _suffix As String

        Public Sub New(Optional suffix As String = "</w>")
            _suffix = If(suffix Is Nothing, "</w>", suffix)
        End Sub

        Public Function DecodeChain(tokens As IEnumerable(Of String)) As List(Of String) Implements IDecoder.DecodeChain
            Dim list As List(Of String) = If(tokens Is Nothing, New List(Of String)(), tokens.ToList())
            Dim n As Integer = list.Count - 1
            Dim result As New List(Of String)(list.Count)
            For i As Integer = 0 To list.Count - 1
                Dim replacement As String = If(i = n, "", " ")
                result.Add(If(list(i), String.Empty).Replace(_suffix, replacement))
            Next
            Return result
        End Function

        Public Function ToJson() As JsonObject Implements IDecoder.ToJson
            Dim o As New JsonObject()
            o("type") = "BPEDecoder"
            o("suffix") = _suffix
            Return o
        End Function
    End Class

End Namespace
