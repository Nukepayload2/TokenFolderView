Imports System.Collections.Generic
Imports System.Linq
Imports System.Text.Json.Nodes

Namespace Decoders

    ''' <summary>
    ''' Port of the Rust <c>Sequence</c> decoder (decoders/sequence.rs). Applies a list of
    ''' decoders in order, feeding each decoder's output into the next.
    ''' </summary>
    Public NotInheritable Class DecoderSequence
        Implements IDecoder

        Private ReadOnly _decoders As IReadOnlyList(Of IDecoder)

        Public Sub New(decoders As IEnumerable(Of IDecoder))
            _decoders = If(decoders Is Nothing, New List(Of IDecoder)(), decoders.ToList())
        End Sub

        Public Function DecodeChain(tokens As IEnumerable(Of String)) As List(Of String) Implements IDecoder.DecodeChain
            Dim current As List(Of String) = If(tokens Is Nothing, New List(Of String)(), tokens.ToList())
            For Each decoder In _decoders
                current = decoder.DecodeChain(current)
            Next
            Return current
        End Function

        Public Function ToJson() As JsonObject Implements IDecoder.ToJson
            Dim o As New JsonObject()
            o("type") = "Sequence"
            Dim arr As New JsonArray()
            For Each decoder As IDecoder In _decoders
                arr.Add(decoder.ToJson())
            Next
            o("decoders") = arr
            Return o
        End Function
    End Class

End Namespace
