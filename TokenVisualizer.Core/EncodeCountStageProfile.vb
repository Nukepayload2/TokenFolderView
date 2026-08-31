''' <summary>
''' Per-stage timing/allocation breakdown of the count-only fast path. Diagnostic only — not
''' used by the scan or encode pipeline itself. (No explicit Namespace block: like Tokenizer.vb,
''' this type lives directly in the RootNamespace "Tokenizers".)
''' </summary>
Public NotInheritable Class EncodeCountStageProfile

    ''' <summary>Length of the input text in UTF-16 code units.</summary>
    Public Property InputCharCount As Integer

    ''' <summary>Number of pieces handed to the model (after pre-tokenization).</summary>
    Public Property PieceCount As Integer

    ''' <summary>Wall ticks of ExtractAndNormalize (+ the no-track alignment skipping).</summary>
    Public Property ExtractTicks As Long

    ''' <summary>Bytes allocated by ExtractAndNormalize on the current thread.</summary>
    Public Property ExtractAllocated As Long

    ''' <summary>Wall ticks of the pre-tokenizer (whole stage).</summary>
    Public Property PretokenizeTicks As Long

    ''' <summary>Bytes allocated by the pre-tokenizer on the current thread.</summary>
    Public Property PretokenizeAllocated As Long

    ''' <summary>Wall ticks of the fused manual-Isolated-split fast path (0 when not a Sequence).</summary>
    Public Property FusedSplitTicks As Long

    ''' <summary>Bytes allocated by the fused manual-Isolated-split fast path.</summary>
    Public Property FusedSplitAllocated As Long

    ''' <summary>Remaining pre-tokenizer sub-stages after the fused run, in order (e.g. ByteLevel).</summary>
    Public Property RemainingStages As List(Of (name As String, ticks As Long, allocated As Long)) =
        New List(Of (name As String, ticks As Long, allocated As Long))()

    ''' <summary>Wall ticks of the per-split model tokenization (TokenizeCount / CountTokens).</summary>
    Public Property ModelTicks As Long

    ''' <summary>Bytes allocated by the model tokenization on the current thread.</summary>
    Public Property ModelAllocated As Long

    ''' <summary>The token count the pipeline would report (must match <see cref="Tokenizer.EncodeCount"/>).</summary>
    Public Property TokenCount As Integer

End Class

''' <summary>Per-phase breakdown of a <c>PreTokenizerSequence</c> run (diagnostic).</summary>
Public NotInheritable Class PreTokenizeStageProfile

    ''' <summary>Wall ticks of the fused leading manual-Isolated-split run (0 when not fused).</summary>
    Public Property FusedSplitTicks As Long

    ''' <summary>Bytes allocated by the fused run.</summary>
    Public Property FusedSplitAllocated As Long

    ''' <summary>Remaining sub-stages after the fused run, in order (name, ticks, allocated).</summary>
    Public Property Remaining As List(Of (name As String, ticks As Long, allocated As Long)) =
        New List(Of (name As String, ticks As Long, allocated As Long))()

End Class
