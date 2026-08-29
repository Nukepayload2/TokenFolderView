Imports System.Collections.Generic
Imports System.Runtime.CompilerServices
Imports System.Text.Json.Nodes

Namespace Decoders

    ''' <summary>
    ''' A <c>Decoder</c> changes the raw tokens into its more readable form.
    ''' Mirrors the Rust <c>Decoder</c> trait (tokenizer/mod.rs).
    ''' </summary>
    Public Interface IDecoder
        ''' <summary>
        ''' Decodes a list of tokens into a (possibly different) list of tokens. Each
        ''' implementation may merge, split, filter or re-map tokens. Mirrors the Rust
        ''' <c>decode_chain</c>.
        ''' </summary>
        Function DecodeChain(tokens As IEnumerable(Of String)) As List(Of String)
        ''' <summary>Serializes this decoder to its tokenizer.json representation.</summary>
        Function ToJson() As JsonObject
    End Interface

    ''' <summary>
    ''' Default <c>Decode</c> behavior: runs <see cref="IDecoder.DecodeChain"/> and joins the
    ''' resulting tokens into a single string. Mirrors the Rust <c>Decoder::decode</c>.
    ''' </summary>
    Public Module DecoderExtensions
        <Extension>
        Public Function Decode(decoder As IDecoder, tokens As IEnumerable(Of String)) As String
            Return String.Join("", decoder.DecodeChain(tokens))
        End Function
    End Module

End Namespace
