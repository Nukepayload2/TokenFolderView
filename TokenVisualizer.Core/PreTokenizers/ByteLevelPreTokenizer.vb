Imports System.Text.Json.Nodes
Imports System.Threading
Imports Tokenizers.Internal

Namespace PreTokenizers

    ''' <summary>
    ''' Port of the Rust <c>ByteLevel</c> pre-tokenizer (pre_tokenizers/byte_level.rs). Optionally
    ''' adds a leading space, splits on the GPT-2 regex (unless disabled), then maps every UTF-8
    ''' byte of each split through the GPT-2 byte-to-char table.
    ''' </summary>
    Public NotInheritable Class ByteLevelPreTokenizer
        Implements IPreTokenizer

        Private ReadOnly _addPrefixSpace As Boolean
        Private ReadOnly _trimOffsets As Boolean
        Private ReadOnly _useRegex As Boolean

        ''' <summary>
        ''' Per-thread reusable (Char, Integer) transform stream buffer. The byte transform emits
        ''' one item per UTF-8 byte of the source scalar, so the buffer is re-created per thread
        ''' (zero cross-thread contention, matching the R3 ThreadLocal caching pattern) and
        ''' cleared/reused across the per-piece transforms of a single thread's EncodeCount,
        ''' avoiding one List allocation per piece (hundreds of thousands per encode).
        ''' </summary>
        Private Shared ReadOnly TransformBuffer As ThreadLocal(Of List(Of (Char, Integer))) =
            New ThreadLocal(Of List(Of (Char, Integer)))(Function() New List(Of (Char, Integer))())

        Public Sub New(addPrefixSpace As Boolean, trimOffsets As Boolean, useRegex As Boolean)
            _addPrefixSpace = addPrefixSpace
            _trimOffsets = trimOffsets
            _useRegex = useRegex
        End Sub

        Public Sub New()
            Me.New(True, True, True)
        End Sub

        Public Sub PreTokenize(pretokenized As PreTokenizedString) Implements IPreTokenizer.PreTokenize
            ' Skip the SplitByFunction pass entirely when it is an identity transform (no prefix
            ' space, no regex): rebuilding the splits list would allocate a fresh Split object per
            ' piece for no observable change.
            If _addPrefixSpace OrElse _useRegex Then
                pretokenized.SplitByFunction(
                    Function(i As Integer, normalized As NormalizedString) As IEnumerable(Of NormalizedString)
                        If _addPrefixSpace AndAlso Not normalized.Get.StartsWith(" "c) Then
                            normalized.Prepend(" ")
                        End If
                        If _useRegex Then
                            Dim pattern As Pattern = ManualPatternFactory.TryCreate(Gpt2ByteLevelPattern.Canonical)
                            If pattern Is Nothing Then pattern = New RegexPattern(Gpt2ByteLevelPattern.Canonical)
                            Return normalized.Split(pattern, SplitDelimiterBehavior.Isolated)
                        Else
                            Return New List(Of NormalizedString) From {normalized}
                        End If
                    End Function)
            End If

            pretokenized.Normalize(
                Sub(normalized As NormalizedString)
                    Dim s As String = normalized.Get
                    ' Each source scalar produces exactly its UTF-8 byte count of (Char, Integer)
                    ' items. The per-thread buffer is reused across pieces (Clear keeps the backing
                    ' array, only growing when a piece is larger than the previous max), so no
                    ' per-piece List is allocated.
                    Dim transformations As List(Of (Char, Integer)) = TransformBuffer.Value
                    transformations.Clear()
                    Dim hint As Integer = Utf8Helpers.Utf8Length(s)
                    If transformations.Capacity < hint Then transformations.Capacity = hint
                    For Each sc In Utf8Helpers.EnumerateScalars(s)
                        BytesToUnicodeTable.AppendByteTransform(transformations, sc.CodePoint)
                    Next
                    normalized.Transform(transformations, 0)
                End Sub)
        End Sub

        Public Function ToJson() As JsonObject Implements IPreTokenizer.ToJson
            Dim o As New JsonObject()
            o("type") = "ByteLevel"
            o("add_prefix_space") = _addPrefixSpace
            o("trim_offsets") = _trimOffsets
            o("use_regex") = _useRegex
            Return o
        End Function
    End Class

End Namespace
