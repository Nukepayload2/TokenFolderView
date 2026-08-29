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
                    ' Collect the UTF-8 byte offset of each scalar start plus the final end
                    ' (a plain Int32 list; no ScalarInfo/string materialization).
                    Dim boundaryBytes As New List(Of Integer)()
                    For Each sc In Utf8Helpers.EnumerateScalars(text)
                        boundaryBytes.Add(sc.Utf8Start)
                    Next
                    boundaryBytes.Add(Utf8Helpers.Utf8Length(text))
                    For chunkStart As Integer = 0 To boundaryBytes.Count - 2 Step _length
                        Dim chunkEnd As Integer = Math.Min(chunkStart + _length, boundaryBytes.Count - 1)
                        Dim start As Integer = boundaryBytes(chunkStart)
                        Dim [end] As Integer = boundaryBytes(chunkEnd)
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
