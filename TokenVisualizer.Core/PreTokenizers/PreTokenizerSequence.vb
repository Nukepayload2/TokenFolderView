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
            Dim idx As Integer = ScanLeadingManualIsolatedRun(patterns)

            If patterns.Count >= 2 Then
                ' Optional trailing pure-map ByteLevel (use_regex=False, add_prefix_space=False):
                ' fold its byte→char mapping into the fused pass so the pieces come out already
                ' mapped and the independent ByteLevel traversal of every piece is skipped. Any
                ' other trailing pre-tokenizer (or a ByteLevel that splits/prefixes) runs normally
                ' after the fused pass.
                Dim fusedByteMap As Boolean = False
                If idx < _pretokenizers.Count Then
                    Dim bl As ByteLevelPreTokenizer = TryCast(_pretokenizers(idx), ByteLevelPreTokenizer)
                    If bl IsNot Nothing AndAlso bl.IsPureMap Then
                        fusedByteMap = True
                        idx += 1
                    End If
                End If

                If fusedByteMap Then
                    pretokenized.FuseIsolatedSplitsWithByteMap(patterns)
                Else
                    pretokenized.FuseIsolatedSplits(patterns)
                End If
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
        ''' Scans the maximal leading run of Isolated manual-pattern Split pre-tokenizers, appending
        ''' their patterns to <paramref name="patterns"/> (cleared first) and returning the index just
        ''' past the run. Shared by <see cref="PreTokenize"/>, <see cref="PreTokenizeProfiled"/> and
        ''' <see cref="TryGetFusedCountConfig"/> so the fused-run detection never drifts.
        ''' </summary>
        Private Function ScanLeadingManualIsolatedRun(patterns As List(Of Pattern)) As Integer
            patterns.Clear()
            Dim idx As Integer = 0
            While idx < _pretokenizers.Count
                Dim sp As SplitPreTokenizer = TryCast(_pretokenizers(idx), SplitPreTokenizer)
                If sp Is Nothing Then Exit While
                Dim pat As Pattern = Nothing
                If Not sp.TryGetIsolatedManualPattern(pat) Then Exit While
                patterns.Add(pat)
                idx += 1
            End While
            Return idx
        End Function

        ''' <summary>
        ''' M2 detection for the count-only fast path: whether the whole sequence is exactly a
        ''' leading run of ≥2 manual Isolated splits followed by a trailing pure-map ByteLevel
        ''' (use_regex=False, add_prefix_space=False) with no remaining pre-tokenizers. When it
        ''' returns True, <c>Tokenizer.EncodeCount</c> can drive the model straight from the fused
        ''' ranges (<see cref="PreTokenizedString.FusedRangesBySplit"/> /
        ''' <see cref="PreTokenizedString.CountFusedRanges"/>) without materializing the per-piece
        ''' <see cref="Split"/> / <see cref="NormalizedString"/> objects that the fuse pass would
        ''' otherwise escape to <c>Me.Splits</c>. <paramref name="patterns"/> receives the fused
        ''' patterns.
        ''' </summary>
        Friend Function TryGetFusedCountConfig(patterns As List(Of Pattern)) As Boolean
            Dim idx As Integer = ScanLeadingManualIsolatedRun(patterns)
            If patterns.Count < 2 Then Return False
            If idx >= _pretokenizers.Count Then Return False
            Dim bl As ByteLevelPreTokenizer = TryCast(_pretokenizers(idx), ByteLevelPreTokenizer)
            If bl Is Nothing OrElse Not bl.IsPureMap Then Return False
            idx += 1
            Return idx = _pretokenizers.Count
        End Function

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
            Dim idx As Integer = ScanLeadingManualIsolatedRun(patterns)

            If patterns.Count >= 2 Then
                ' Same trailing pure-map ByteLevel fold as PreTokenize; keep in sync.
                Dim fusedByteMap As Boolean = False
                If idx < _pretokenizers.Count Then
                    Dim bl As ByteLevelPreTokenizer = TryCast(_pretokenizers(idx), ByteLevelPreTokenizer)
                    If bl IsNot Nothing AndAlso bl.IsPureMap Then
                        fusedByteMap = True
                        idx += 1
                    End If
                End If

                Dim sw As Stopwatch = Stopwatch.StartNew()
                Dim alloc As Long = GC.GetAllocatedBytesForCurrentThread()
                If fusedByteMap Then
                    pretokenized.FuseIsolatedSplitsWithByteMap(patterns)
                Else
                    pretokenized.FuseIsolatedSplits(patterns)
                End If
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
