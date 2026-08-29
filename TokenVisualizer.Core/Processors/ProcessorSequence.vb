Imports System.Linq
Imports System.Text.Json.Nodes
Imports Tokenizers.Internal

Namespace Processors

    ''' <summary>
    ''' A post-processor that applies a chain of post-processors in order. Mirrors the Rust
    ''' <c>Sequence</c> (processors/sequence.rs).
    ''' </summary>
    Public NotInheritable Class ProcessorSequence
        Implements IPostProcessor

        Private ReadOnly _processors As List(Of IPostProcessor)

        Public Sub New(processors As IEnumerable(Of IPostProcessor))
            _processors = processors.ToList()
        End Sub

        Public Function GetAddedTokens(isPair As Boolean) As Integer Implements IPostProcessor.GetAddedTokens
            Return _processors.Sum(Function(p) p.GetAddedTokens(isPair))
        End Function

        Public Function Process(enc As Encoding, pairEnc As Encoding, addSpecialTokens As Boolean) As Encoding
            Return PostProcessorHelpers.DefaultProcess(Me, enc, pairEnc, addSpecialTokens)
        End Function

        Public Function ProcessEncodings(encodings As List(Of Encoding), addSpecialTokens As Boolean) As List(Of Encoding) Implements IPostProcessor.ProcessEncodings
            For Each p As IPostProcessor In _processors
                encodings = p.ProcessEncodings(encodings, addSpecialTokens)
            Next
            Return encodings
        End Function

        Public Function ToJson() As JsonObject Implements IPostProcessor.ToJson
            Dim o As New JsonObject()
            o("type") = "Sequence"
            Dim arr As New JsonArray()
            For Each p As IPostProcessor In _processors
                arr.Add(p.ToJson())
            Next
            o("processors") = arr
            Return o
        End Function
    End Class

End Namespace
