Imports System.Linq
Imports System.Text.Json.Nodes
Imports Tokenizers.Internal

Namespace PreTokenizers

    ''' <summary>
    ''' Port of the Rust <c>Sequence</c> pre-tokenizer (pre_tokenizers/sequence.rs): runs a list of
    ''' pre-tokenizers in order on the same <see cref="PreTokenizedString"/>.
    ''' </summary>
    Public NotInheritable Class PreTokenizerSequence
        Implements IPreTokenizer

        Private ReadOnly _pretokenizers As List(Of IPreTokenizer)

        Public Sub New(pretokenizers As IEnumerable(Of IPreTokenizer))
            _pretokenizers = pretokenizers.ToList()
        End Sub

        Public Sub PreTokenize(pretokenized As PreTokenizedString) Implements IPreTokenizer.PreTokenize
            For Each pretokenizer In _pretokenizers
                pretokenizer.PreTokenize(pretokenized)
            Next
        End Sub

        Public Function ToJson() As JsonObject Implements IPreTokenizer.ToJson
            Dim o As New JsonObject()
            o("type") = "Sequence"
            Dim arr As New JsonArray()
            For Each pretokenizer In _pretokenizers
                arr.Add(pretokenizer.ToJson())
            Next
            o("pretokenizers") = arr
            Return o
        End Function
    End Class

End Namespace
