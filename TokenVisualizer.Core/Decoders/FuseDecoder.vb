Imports System.Collections.Generic
Imports System.Linq
Imports System.Text.Json.Nodes

Namespace Decoders

    ''' <summary>
    ''' Port of the Rust <c>Fuse</c> decoder (decoders/fuse.rs). Fuses all tokens into one big
    ''' string.
    ''' </summary>
    Public NotInheritable Class FuseDecoder
        Implements IDecoder

        Public Sub New()
        End Sub

        Public Function DecodeChain(tokens As IEnumerable(Of String)) As List(Of String) Implements IDecoder.DecodeChain
            Dim newString As String = String.Join("", If(tokens, Enumerable.Empty(Of String)()))
            Return New List(Of String) From {newString}
        End Function

        Public Function ToJson() As JsonObject Implements IDecoder.ToJson
            Dim o As New JsonObject()
            o("type") = "Fuse"
            Return o
        End Function
    End Class

End Namespace
