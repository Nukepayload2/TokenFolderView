Imports System.Linq
Imports System.Text.Json.Nodes
Imports Tokenizers.Internal

Namespace Processors

    ''' <summary>
    ''' A PostProcessor has the responsibility to post process an encoded output of the Tokenizer.
    ''' It adds any special tokens that a language model would require. Mirrors the Rust
    ''' <c>PostProcessor</c> trait (tokenizer/mod.rs).
    ''' </summary>
    Public Interface IPostProcessor

        ''' <summary>Returns the number of tokens that will be added during the processing step.</summary>
        Function GetAddedTokens(isPair As Boolean) As Integer

        ''' <summary>
        ''' Process any amount of encodings and returns a series of encoding (might merge them).
        ''' Mirrors <c>PostProcessor::process_encodings</c>.
        ''' </summary>
        Function ProcessEncodings(encodings As List(Of Encoding), addSpecialTokens As Boolean) As List(Of Encoding)

        ''' <summary>Serializes this post-processor to its tokenizer.json representation.</summary>
        Function ToJson() As JsonObject

    End Interface

    ''' <summary>
    ''' Shared implementation of the Rust <c>PostProcessor::process</c> default method. Builds the
    ''' <c>[encoding]</c> / <c>[encoding, pair]</c> vector, pre-sets each encoding's type ids to all
    ''' <c>i</c> and its sequence id to <c>i</c> (including overflowings), dispatches to
    ''' <see cref="IPostProcessor.ProcessEncodings"/> and merges the resulting encodings.
    ''' </summary>
    Public Module PostProcessorHelpers

        Public Function DefaultProcess(processor As IPostProcessor,
                                       enc As Encoding,
                                       pairEnc As Encoding,
                                       addSpecialTokens As Boolean) As Encoding
            Dim encodings As List(Of Encoding)
            If pairEnc Is Nothing Then
                encodings = New List(Of Encoding) From {enc}
            Else
                encodings = New List(Of Encoding) From {enc, pairEnc}
            End If

            For i As Integer = 0 To encodings.Count - 1
                Dim e As Encoding = encodings(i)
                e.TypeIds = Enumerable.Repeat(i, e.Length).ToList()
                e.SetSequenceId(i)
                For Each o As Encoding In e.Overflowing
                    o.SetSequenceId(i)
                Next
            Next

            Dim processed As List(Of Encoding) = processor.ProcessEncodings(encodings, addSpecialTokens)
            Return Encoding.Merge(processed, False)
        End Function

    End Module

End Namespace
