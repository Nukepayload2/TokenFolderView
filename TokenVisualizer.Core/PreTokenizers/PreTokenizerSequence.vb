Imports System.Diagnostics
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
            ' Fused fast path: a maximal leading run of Isolated Split pre-tokenizers that all use
            ' hand-written manual patterns (e.g. DeepSeek's Numbers + CJK + Gpt2) is collapsed into
            ' a single pass that slices the root NormalizedString once per final piece. Any
            ' non-qualifying pre-tokenizer (a Regex pattern, another behavior, an inverted split,
            ' or any other pre-tokenizer type) breaks the run and falls back to the sequential loop
            ' so semantics are byte-identical to the Rust reference.
            Dim patterns As New List(Of Pattern)()
            Dim idx As Integer = 0
            While idx < _pretokenizers.Count
                Dim sp As SplitPreTokenizer = TryCast(_pretokenizers(idx), SplitPreTokenizer)
                If sp Is Nothing Then Exit While
                Dim pat As Pattern = Nothing
                If Not sp.TryGetIsolatedManualPattern(pat) Then Exit While
                patterns.Add(pat)
                idx += 1
            End While

            If patterns.Count >= 2 Then
                pretokenized.FuseIsolatedSplits(patterns)
                For i As Integer = idx To _pretokenizers.Count - 1
                    _pretokenizers(i).PreTokenize(pretokenized)
                Next
            Else
                For Each pretokenizer In _pretokenizers
                    pretokenizer.PreTokenize(pretokenized)
                Next
            End If
        End Sub

        ''' <summary>
        ''' Diagnostic (dev-only): runs the same pipeline as <see cref="PreTokenize"/> — including the
        ''' fused manual-Isolated-split fast path — and reports per-phase wall ticks + allocated bytes:
        ''' the fused leading run first, then each remaining pre-tokenizer individually. Mirrors the
        ''' fuse decision in <see cref="PreTokenize"/>; keep in sync if it changes.
        ''' </summary>
        Public Function PreTokenizeProfiled(pretokenized As PreTokenizedString) As PreTokenizeStageProfile
            Dim profile As New PreTokenizeStageProfile()

            ' Same leading-run detection as PreTokenize.
            Dim patterns As New List(Of Pattern)()
            Dim idx As Integer = 0
            While idx < _pretokenizers.Count
                Dim sp As SplitPreTokenizer = TryCast(_pretokenizers(idx), SplitPreTokenizer)
                If sp Is Nothing Then Exit While
                Dim pat As Pattern = Nothing
                If Not sp.TryGetIsolatedManualPattern(pat) Then Exit While
                patterns.Add(pat)
                idx += 1
            End While

            If patterns.Count >= 2 Then
                Dim sw As Stopwatch = Stopwatch.StartNew()
                Dim alloc As Long = GC.GetAllocatedBytesForCurrentThread()
                pretokenized.FuseIsolatedSplits(patterns)
                profile.FusedSplitTicks = sw.ElapsedTicks
                profile.FusedSplitAllocated = GC.GetAllocatedBytesForCurrentThread() - alloc
                For i As Integer = idx To _pretokenizers.Count - 1
                    sw.Restart()
                    alloc = GC.GetAllocatedBytesForCurrentThread()
                    _pretokenizers(i).PreTokenize(pretokenized)
                    profile.Remaining.Add((SubStageName(_pretokenizers(i)), sw.ElapsedTicks, GC.GetAllocatedBytesForCurrentThread() - alloc))
                Next
            Else
                For Each pretokenizer In _pretokenizers
                    Dim sw As Stopwatch = Stopwatch.StartNew()
                    Dim alloc As Long = GC.GetAllocatedBytesForCurrentThread()
                    pretokenizer.PreTokenize(pretokenized)
                    profile.Remaining.Add((SubStageName(pretokenizer), sw.ElapsedTicks, GC.GetAllocatedBytesForCurrentThread() - alloc))
                Next
            End If
            Return profile
        End Function

        Private Shared Function SubStageName(p As IPreTokenizer) As String
            If TypeOf p Is SplitPreTokenizer Then Return "Split"
            If TypeOf p Is ByteLevelPreTokenizer Then Return "ByteLevel"
            Return p.GetType().Name
        End Function

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
