Imports System.Linq
Imports System.Text.Json.Nodes
Imports Tokenizers.Internal

Namespace PreTokenizers

    ''' <summary>
    ''' Port of the Rust <c>FixedLength</c> pre-tokenizer (pre_tokenizers/fixed_length.rs). Chunks
    ''' the text into groups of the given number of Unicode scalars.
    ''' </summary>
    Public NotInheritable Class FixedLengthPreTokenizer
        Implements IPreTokenizer

        Private ReadOnly _length As Integer

        Public Sub New(length As Integer)
            _length = length
        End Sub

        Public Sub New()
            Me.New(5)
        End Sub

        Public Sub PreTokenize(pretokenized As PreTokenizedString) Implements IPreTokenizer.PreTokenize
            pretokenized.SplitByFunction(
                Function(i As Integer, normalized As NormalizedString) As IEnumerable(Of NormalizedString)
                    Dim result As New List(Of NormalizedString)()
                    Dim text As String = normalized.Get
                    If text.Length = 0 OrElse _length <= 0 Then
                        Return result
                    End If
                    Dim scalars As List(Of ScalarInfo) = Utf8Helpers.EnumerateScalars(text).ToList()
                    For chunkStart As Integer = 0 To scalars.Count - 1 Step _length
                        Dim chunkEnd As Integer = Math.Min(chunkStart + _length, scalars.Count)
                        Dim start As Integer = scalars(chunkStart).Utf8Start
                        Dim lastIdx As Integer = chunkEnd - 1
                        Dim [end] As Integer = scalars(lastIdx).Utf8Start + scalars(lastIdx).Utf8Len
                        result.Add(normalized.Slice(New OffsetRange(False, start, [end])))
                    Next
                    Return result
                End Function)
        End Sub

        Public Function ToJson() As JsonObject Implements IPreTokenizer.ToJson
            Dim o As New JsonObject()
            o("type") = "FixedLength"
            o("length") = _length
            Return o
        End Function
    End Class

End Namespace
