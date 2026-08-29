Imports System.Text.Json.Nodes

Namespace Normalizers

    ''' <summary>
    ''' A <c>Normalizer</c> mutates a <see cref="Tokenizers.Internal.NormalizedString"/> in place.
    ''' Mirrors the Rust <c>tokenizers::normalizer::Normalizer</c> trait (<c>normalize</c>).
    ''' </summary>
    Public Interface INormalizer
        ''' <summary>Normalizes the given string in place.</summary>
        Sub Normalize(normalized As Internal.NormalizedString)
        ''' <summary>Serializes this normalizer to its tokenizer.json representation.</summary>
        Function ToJson() As JsonObject
    End Interface

End Namespace
