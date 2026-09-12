Imports System.Collections.Concurrent
Imports System.Reflection
Imports System.Threading
Imports System.Threading.Tasks
Imports Tokenizers
Imports Tokenizers.Internal
Imports Tokenizers.Models
Imports Tokenizers.PreTokenizers

Namespace TokenVisualizer.Core.Tests

    ''' <summary>
    ''' Thread-safety gate for the shared tokenizer caches. The scanner runs
    ''' <c>Parallel.ForEach</c> over files and calls <see cref="Tokenizer.EncodeCount"/> from many
    ''' threads, so the BPE/Unigram word/sentence caches must be safe for concurrent readers.
    ''' These tests hammer <c>EncodeCount</c> from <see cref="Environment.ProcessorCount"/> threads
    ''' (with a start-gate to maximize the contention window) and assert every thread's result
    ''' matches the single-threaded baseline, byte-for-byte, with no exceptions.
    ''' </summary>
    <TestClass>
    Public NotInheritable Class ConcurrencyTests

        Private Const DeepSeekPath As String =
            "C:\Users\james\Projects\TokenVisualizer\deepseek-v4-flash\tokenizer.json"

        ''' <summary>A batch of varied texts that exercises many distinct cache keys.</summary>
        Private Shared Function BuildTexts() As List(Of String)
            Dim paragraph As String =
                "The quick brown fox jumps over the lazy dog. 12345 67890 !!! ??? " &
                "你好世界 こんにちは hello world tokenization is amazing. " &
                "DeepSeek is an advanced large language model platform. " &
                "Symbols: @#$%^&*()_+-=[]{}:;\|~` émoji 🚀 and 中文. " &
                "Repeat repeat repeat repeat repeat repeat repeat. "
            Dim texts As New List(Of String)()
            texts.Add(paragraph)
            texts.Add(paragraph & paragraph)
            texts.Add("  leading and trailing  ")
            texts.Add("")
            texts.Add(" ")
            texts.Add("12345678901234567890")
            texts.Add("a😁b𠀀c")
            For i As Integer = 0 To 19
                texts.Add(paragraph.Substring(0, Math.Min(paragraph.Length, 5 + i * 7)))
            Next
            Return texts
        End Function

        ''' <summary>
        ''' Runs <see cref="Tokenizer.EncodeCount"/> on <paramref name="texts"/> from
        ''' <see cref="Environment.ProcessorCount"/> concurrent tasks, repeatedly, and asserts every
        ''' result equals the serial baseline and that no exception is thrown. A manual-reset start
        ''' gate releases all workers simultaneously to maximize cache contention.
        ''' </summary>
        Private Shared Sub AssertConcurrentMatchesSerial(tokenizer As Tokenizer, texts As List(Of String))
            ' Serial baseline.
            Dim baseline As Integer() = New Integer(texts.Count - 1) {}
            For i As Integer = 0 To texts.Count - 1
                baseline(i) = tokenizer.EncodeCount(texts(i))
            Next

            Dim threads As Integer = Math.Max(2, Environment.ProcessorCount)
            Dim outerRounds As Integer = 3
            Dim errors As New ConcurrentQueue(Of String)()
            Dim mismatches As New ConcurrentQueue(Of String)()

            For round As Integer = 0 To outerRounds - 1
                Dim roundId As Integer = round ' capture loop var for the worker lambdas
                Dim startGate As New ManualResetEventSlim(False)
                Dim workers(threads - 1) As Task
                For t As Integer = 0 To threads - 1
                    Dim threadId As Integer = t
                    workers(threadId) = Task.Run(
                        Sub()
                            Try
                                startGate.Wait()
                                For r As Integer = 0 To 4
                                    For i As Integer = 0 To texts.Count - 1
                                        Dim actual As Integer = tokenizer.EncodeCount(texts(i))
                                        If actual <> baseline(i) Then
                                            mismatches.Enqueue(
                                                $"round={roundId} thread={threadId} rep={r} text#{i}: got {actual}, expected {baseline(i)}")
                                        End If
                                    Next
                                Next
                            Catch ex As Exception
                                errors.Enqueue($"round={roundId} thread={threadId}: {ex.GetType().Name}: {ex.Message}")
                            End Try
                        End Sub)
                Next
                startGate.Set()
                Task.WaitAll(workers)
            Next

            Assert.IsTrue(
                errors.IsEmpty,
                "Concurrent EncodeCount threw an exception: " & String.Join(" | ", errors.Take(5)))

            Assert.IsTrue(
                mismatches.IsEmpty,
                "Concurrent EncodeCount result differed from serial baseline: " &
                String.Join(" | ", mismatches.Take(5)))
        End Sub

        ''' <summary>A synthetic GPT-2-style BPE tokenizer used to exercise the BPE cache.</summary>
        Private Shared Function BuildBpeTokenizer() As Tokenizer
            Dim vocab As New Dictionary(Of String, Integer)()
            Dim id As Integer = 0
            ' Single characters (letters, digits, space surrogate, punctuation).
            For Each c As Char In " abcdefghijklmnopqrstuvwxyz0123456789.,!?Ġ".ToCharArray()
                vocab(c.ToString()) = id
                id += 1
            Next
            ' Common bigrams that merges can produce.
            For Each p As String In {"th", "he", "qu", "ic", "ck", "br", "ow", "fo", "ox", "ju", "mp", "ov", "la", "zy", "lo", "is"}
                If Not vocab.ContainsKey(p) Then
                    vocab(p) = id
                    id += 1
                End If
            Next
            ' Whole words.
            For Each w As String In {"the", "quick", "brown", "fox", "jumps", "over", "lazy", "dog", "hello", "world", "tokenization"}
                If Not vocab.ContainsKey(w) Then
                    vocab(w) = id
                    id += 1
                End If
            Next
            ' Merge lines (rank = list index), all resolvable against the vocab above.
            Dim merges As New List(Of String)() From {
                "t h", "h e", "q u", "i c", "c k", "b r", "o w", "f o", "o x",
                "j u", "m p", "o v", "l a", "z y", "d o", "o g", "l o", "i s"
            }
            Dim bpe As New BpeModel(vocab, merges, cacheCapacity:=10000)
            Dim tok As New Tokenizer(bpe)
            Return tok
        End Function

        ''' <summary>
        ''' The same synthetic BPE model as <see cref="BuildBpeTokenizer"/>, wired with a fused count
        ''' config: ≥2 manual Isolated splits followed by a trailing pure-map ByteLevel — the
        ''' DeepSeek shape (see EncodeFastRegressionTests.DeepSeekFusedPath_FastPathTaken_*, which
        ''' builds the same pre-tokenizer). With this configuration
        ''' <see cref="Tokenizer.EncodeCount"/> returns from the fused-range branch
        ''' (<c>Tokenizer.vb:378-390</c>) instead of falling through to the String-keyed
        ''' <c>Model.CountTokens</c>, and the per-thread count visitor counts every fused range via
        ''' <see cref="BpeModel.CountTokensMemory"/> (<c>PreTokenizedString.vb:768</c>) — the
        ''' memory-keyed alternate lookup, which is the path the real application (deepseek) scans
        ''' with.
        ''' </summary>
        Private Shared Function BuildFusedBpeTokenizer() As Tokenizer
            Dim tokenizer As Tokenizer = BuildBpeTokenizer()
            tokenizer.WithPreTokenizer(
                New PreTokenizerSequence(New IPreTokenizer() {
                    New SplitPreTokenizer("Regex", DeepSeekNumbersPattern.Canonical, SplitDelimiterBehavior.Isolated, False),
                    New SplitPreTokenizer("Regex", DeepSeekCjkPattern.Canonical, SplitDelimiterBehavior.Isolated, False),
                    New SplitPreTokenizer("Regex", DeepSeekGpt2Pattern.Canonical, SplitDelimiterBehavior.Isolated, False),
                    New ByteLevelPreTokenizer(False, True, False)
                }))
            Return tokenizer
        End Function

        ''' <summary>A synthetic Unigram tokenizer used to exercise the Unigram cache.</summary>
        Private Shared Function BuildUnigramTokenizer() As Tokenizer
            Dim vocab As New List(Of (String, Double))() From {
                ("<unk>", 0.0),
                ("ab", 0.0),
                ("cd", -0.1),
                ("abc", -0.2),
                ("a", -0.3),
                ("b", -0.4),
                ("c", -0.5),
                ("ABC", -0.5),
                ("abcdabcd", 20.0),
                ("q", 20.5),
                ("r", 20.5),
                ("qr", -0.5),
                ("hello", -0.2),
                ("world", -0.3),
                ("the", -0.1),
                ("quick", -0.4),
                ("brown", -0.4),
                ("fox", -0.5)
            }
            Dim uni As New UnigramModel(vocab, 0, False)
            Return New Tokenizer(uni)
        End Function

        <TestMethod>
        Public Sub BpeModel_ConcurrentEncodeCount_MatchesSerial()
            Dim tokenizer As Tokenizer = BuildBpeTokenizer()
            Dim texts As List(Of String) = BuildTexts()
            AssertConcurrentMatchesSerial(tokenizer, texts)
        End Sub

        ''' <summary>
        ''' Asserts that <paramref name="tokenizer"/>'s pre-tokenizer qualifies as a fused count
        ''' config, i.e. that <see cref="Tokenizer.EncodeCount"/> will take the fused-range branch
        ''' (<c>Tokenizer.vb:378</c>) — the branch whose per-word entry point is
        ''' <see cref="BpeModel.CountTokensMemory"/>. Evaluates the exact predicate the tokenizer
        ''' itself evaluates (<c>PreTokenizerSequence.TryGetFusedCountConfig</c> on its
        ''' <c>PreTokenizerSequence</c>), so a configuration that silently degraded to the
        ''' String-keyed path fails here loudly instead of making the storm vacuous. Read-only.
        ''' </summary>
        Private Shared Sub AssertFusedCountConfig(tokenizer As Tokenizer)
            Dim seq As PreTokenizerSequence = TryCast(tokenizer.PreTokenizer, PreTokenizerSequence)
            Assert.IsNotNull(seq, "a fused count config's pre-tokenizer must be a PreTokenizerSequence")
            Dim tryGetFused As MethodInfo = GetType(PreTokenizerSequence).GetMethod(
                "TryGetFusedCountConfig", BindingFlags.Instance Or BindingFlags.NonPublic)
            Assert.IsNotNull(tryGetFused, "PreTokenizerSequence.TryGetFusedCountConfig must exist (Friend M2 method)")
            Dim patterns As New List(Of Pattern)()
            Dim fused As Boolean = CBool(tryGetFused.Invoke(seq, New Object() {patterns}))
            Assert.IsTrue(
                fused,
                "the pre-tokenizer must qualify as a fused count config (>=2 manual Isolated splits + a trailing pure-map ByteLevel)")
            Assert.IsGreaterThanOrEqualTo(
                2, patterns.Count, "a fused count config must fuse at least two split patterns")
        End Sub

        ''' <summary>
        ''' Number of entries currently held in the model's shared L2 word cache (the sum over its
        ''' lock-striped shards). The L2 is written ONLY by <see cref="BpeModel.CountTokensMemory"/>'s
        ''' L1-miss write-through (<c>BpeModel.vb:744/760/770</c>); the String-keyed
        ''' <see cref="BpeModel.CountTokens"/> never touches it. A non-zero count after the cache was
        ''' cleared is therefore runtime proof that the memory-keyed path actually executed (the
        ''' readers' own L1 caches are per-thread and not visible from here). Read-only.
        ''' </summary>
        Private Shared Function SharedL2EntryCount(bpe As BpeModel) As Integer
            Dim field As FieldInfo = GetType(BpeModel).GetField(
                "_sharedCaches", BindingFlags.Instance Or BindingFlags.NonPublic)
            Assert.IsNotNull(field, "BpeModel._sharedCaches must exist (the shared L2 word cache)")
            Dim shards As Array = DirectCast(field.GetValue(bpe), Array)
            If shards Is Nothing Then Return 0
            Dim total As Integer = 0
            Dim countProp As PropertyInfo = Nothing
            For i As Integer = 0 To shards.Length - 1
                Dim shard As Object = shards.GetValue(i)
                If countProp Is Nothing Then
                    countProp = shard.GetType().GetProperty("Count", BindingFlags.Instance Or BindingFlags.Public)
                    Assert.IsNotNull(countProp, "a shared L2 shard must expose a public Count")
                End If
                total += CInt(countProp.GetValue(shard))
            Next
            Return total
        End Function

        ''' <summary>
        ''' Regression gate for the L1 word-cache compaction race: while
        ''' <see cref="BpeModel.CompactWordCache"/> runs (it disposes the whole
        ''' <see cref="ThreadLocal(Of T)"/> and installs a replacement, clearing the other live
        ''' threads' slots), concurrent readers of the OLD instance used to get
        ''' <see cref="ObjectDisposedException"/> out of <c>.Value</c> — deterministically (measured:
        ''' 1000/1000 per interleaving; 100% of the 4 M reads that followed the dispose flag), which
        ''' the colored view surfaced as "无法读取文件：Cannot access a disposed object.".
        '''
        ''' One reader drives the colored-view path (<see cref="Tokenizer.EncodeWithSpans"/>, i.e.
        ''' <c>BpeModel.Tokenize</c>) and the rest drive the count path
        ''' (<see cref="Tokenizer.EncodeCount"/>); all readers must survive the compaction storm with
        ''' no exception and with counts / spans identical to the single-threaded reference.
        ''' Time-bounded (~1 s) so it stays cheap. No I/O.
        ''' </summary>
        ''' <param name="tokenizer">The tokenizer under test. Its configuration decides which per-word
        ''' count entry point the count readers land on — see <paramref name="memoryKeyedCountPath"/>.
        ''' </param>
        ''' <param name="memoryKeyedCountPath">When <c>True</c>, <paramref name="tokenizer"/> must be
        ''' a fused count config, so the count readers enter the model through
        ''' <see cref="BpeModel.CountTokensMemory"/> (the path the real application scans with). The
        ''' run is then additionally gated on that path having really been executed (shared-L2
        ''' write-through count &gt; 0, with the String-keyed path shown not to write that L2), so the
        ''' test cannot pass while covering nothing.</param>
        Private Shared Sub AssertConcurrentEncodeSurvivesCompactStorm(tokenizer As Tokenizer,
                                                                     memoryKeyedCountPath As Boolean)
            Const RaceMilliseconds As Integer = 800

            Dim bpe As BpeModel = DirectCast(tokenizer.Model, BpeModel)
            Dim texts As List(Of String) = BuildTexts()
            Dim spanText As String = texts(1) ' two paragraphs: many distinct cache keys per call

            ' Single-threaded reference; every reader must reproduce these exactly.
            Dim expectedCounts As Integer() = New Integer(texts.Count - 1) {}
            For i As Integer = 0 To texts.Count - 1
                expectedCounts(i) = tokenizer.EncodeCount(texts(i))
            Next
            Dim expectedSpans As List(Of (Integer, Integer, Integer)) = tokenizer.EncodeWithSpans(spanText)

            ' Run gate for the memory-keyed count path (fused config only). The single-threaded
            ' reference above already warmed both L1s and the shared L2, so clear EVERYTHING (drop the
            ' per-thread L1s and all L2 shards) before the storm: from here on, only the storm can
            ' repopulate anything. Then show the gate is discriminating — the String-keyed
            ' Model.CountTokens does not write the shared L2 — which leaves the L2's post-storm
            ' content as a count of CountTokensMemory write-throughs alone.
            If memoryKeyedCountPath Then
                AssertFusedCountConfig(tokenizer)
                bpe.CompactWordCache(dropShared:=True)
                Assert.AreEqual(0, SharedL2EntryCount(bpe), "dropShared must empty the shared L2")
                ' Twice: the first call is an L1 miss + insert, the second an L1 hit — neither may
                ' reach the shared L2.
                For i As Integer = 0 To 1
                    tokenizer.Model.CountTokens(spanText)
                    Assert.AreEqual(
                        0, SharedL2EntryCount(bpe),
                        "the String-keyed Model.CountTokens must not write the shared L2 " &
                        "(otherwise the run gate below would not be specific to CountTokensMemory)")
                Next
            End If

            Dim threads As Integer = Math.Max(2, Environment.ProcessorCount)
            Dim errors As New ConcurrentQueue(Of String)()
            Dim mismatches As New ConcurrentQueue(Of String)()
            Dim errorCount As Integer = 0
            Dim compactions As Integer = 0
            Dim countPasses As Integer = 0
            Dim spanPasses As Integer = 0

            Using startGate As New ManualResetEventSlim(False)
                ' Completes when the reader window is over; the readers and the compactor poll it.
                Dim raceStop As Task = Task.Delay(RaceMilliseconds)

                ' The compactor runs on a DEDICATED thread rather than a pool task: the readers
                ' saturate the thread pool (one task per processor), so a pool-scheduled compactor can
                ' be injected after the whole window has elapsed — it then swaps nothing and the
                ' "the compactor never swapped the cache" gate below fails for a scheduling reason
                ' instead of a code defect (observed once in a 5-run streak, flaking the run without
                ' any reader error). A real thread is never subject to pool injection delay.
                Dim compactor As New Thread(
                    New ThreadStart(
                        Sub()
                            Try
                                startGate.Wait()
                                ' Hammer the swap for the whole reader window: every CompactWordCache
                                ' disposes the instance the readers may be holding.
                                Do While Not raceStop.IsCompleted
                                    bpe.CompactWordCache()
                                    Interlocked.Increment(compactions)
                                Loop
                            Catch ex As Exception
                                Interlocked.Increment(errorCount)
                                errors.Enqueue($"compactor: {ex.GetType().Name}: {ex.Message}")
                            End Try
                        End Sub))
                compactor.IsBackground = True
                compactor.Name = "word cache compactor"
                compactor.Start()

                Dim workers(threads - 1) As Thread
                For t As Integer = 0 To threads - 1
                    Dim threadId As Integer = t
                    workers(threadId) = New Thread(
                        New ThreadStart(
                        Sub()
                            Try
                                startGate.Wait()
                                If threadId = 0 Then
                                    ' The colored view: Tokenize → the L1 hit lookup / miss insert.
                                    Do While Not raceStop.IsCompleted
                                        Dim actual As List(Of (Integer, Integer, Integer)) = tokenizer.EncodeWithSpans(spanText)
                                        If actual.Count <> expectedSpans.Count Then
                                            mismatches.Enqueue($"spans: {actual.Count} spans, expected {expectedSpans.Count}")
                                        Else
                                            For k As Integer = 0 To actual.Count - 1
                                                If actual(k).Item1 <> expectedSpans(k).Item1 OrElse
                                                   actual(k).Item2 <> expectedSpans(k).Item2 OrElse
                                                   actual(k).Item3 <> expectedSpans(k).Item3 Then
                                                    mismatches.Enqueue($"span[{k}]: {actual(k)}, expected {expectedSpans(k)}")
                                                    Exit For
                                                End If
                                            Next
                                        End If
                                        Interlocked.Increment(spanPasses)
                                    Loop
                                Else
                                    ' The count path: Tokenizer.EncodeCount. Which per-word entry
                                    ' point it lands on is a function of the tokenizer
                                    ' configuration, and the two tests below pin both sides:
                                    '   * no PreTokenizer (BuildBpeTokenizer) ⇒ isFusedCountConfig
                                    '     = False ⇒ EncodeCount falls through to Tokenizer.vb:394,
                                    '     Model.CountTokens (the String-keyed lookup);
                                    '   * fused count config (BuildFusedBpeTokenizer) ⇒ EncodeCount
                                    '     returns from the fused branch (Tokenizer.vb:378-390) and
                                    '     the per-thread count visitor counts every fused range via
                                    '     Model.CountTokensMemory (PreTokenizedString.vb:768) — the
                                    '     memory-keyed alternate lookup, which is the path the real
                                    '     application (deepseek) scans with.
                                    ' Both paths go through the L1 word cache whose instance the
                                    ' compactor swaps.
                                    Do While Not raceStop.IsCompleted
                                        For i As Integer = 0 To texts.Count - 1
                                            Dim actual As Integer = tokenizer.EncodeCount(texts(i))
                                            If actual <> expectedCounts(i) Then
                                                mismatches.Enqueue($"text#{i}: {actual}, expected {expectedCounts(i)}")
                                            End If
                                        Next
                                        Interlocked.Increment(countPasses)
                                    Loop
                                End If
                            Catch ex As Exception
                                Interlocked.Increment(errorCount)
                                errors.Enqueue($"thread={threadId}: {ex.GetType().Name}: {ex.Message}")
                            End Try
                        End Sub))
                    ' Dedicated threads (not pool tasks) for the READERS too: the readers alone would
                    ' saturate the pool (one per processor), and a pool-scheduled reader can be
                    ' injected after the window has closed — it then completes no pass at all, failing
                    ' the non-vacuity gates below ("no span reader completed a pass", observed once in
                    ' a full-suite streak) for a scheduling reason instead of a code defect.
                    workers(threadId).IsBackground = True
                    workers(threadId).Name = $"word cache storm reader {threadId}"
                    workers(threadId).Start()
                Next

                startGate.Set()
                For t As Integer = 0 To threads - 1
                    workers(t).Join()
                Next
                compactor.Join()
            End Using

            Assert.IsTrue(
                errors.IsEmpty,
                $"{errorCount} of {threads + 1} tasks threw while the word cache was being compacted: " &
                String.Join(" | ", errors.Take(3)))

            Assert.IsTrue(
                mismatches.IsEmpty,
                "A reader's result differed from the serial baseline: " & String.Join(" | ", mismatches.Take(5)))

            ' A vacuous run (no swap at all, or readers that never got to run) would pass the two
            ' assertions above without exercising the race.
            Assert.IsGreaterThan(0, compactions, "the compactor never swapped the cache")
            Assert.IsGreaterThan(0, countPasses, "no count reader completed a pass")
            Assert.IsGreaterThan(0, spanPasses, "no span reader completed a pass")

            If memoryKeyedCountPath Then
                ' The storm emptied the L2 and the String-keyed path provably cannot refill it, so a
                ' non-empty L2 can only come from BpeModel.CountTokensMemory's write-through: the
                ' count readers really ran the memory-keyed path under the compaction storm.
                Assert.IsGreaterThan(
                    0, SharedL2EntryCount(bpe),
                    "no CountTokensMemory write-through reached the shared L2 — the storm never exercised the memory-keyed count path")
            End If
        End Sub

        ''' <summary>
        ''' Regression gate for the L1 word-cache compaction race on the STRING-keyed count path:
        ''' <see cref="BpeModel.CountTokens"/> resolves the L1 via <c>_cache.Value</c> (per word),
        ''' which the compactor's <c>ThreadLocal.Dispose</c> used to make throw
        ''' <see cref="ObjectDisposedException"/>. One span reader plus one count reader per processor
        ''' compete with a compactor for ~1 s; all results must match the serial reference.
        ''' Deliberately kept alongside its fused twin below so both count entry points stay covered.
        ''' </summary>
        <TestMethod>
        Public Sub BpeModel_ConcurrentEncodeWithCompactWordCache_MatchesSerial()
            AssertConcurrentEncodeSurvivesCompactStorm(BuildBpeTokenizer(), memoryKeyedCountPath:=False)
        End Sub

        ''' <summary>
        ''' Same storm on the MEMORY-keyed count path: a fused count config (DeepSeek's shape) makes
        ''' <see cref="Tokenizer.EncodeCount"/> count every fused range through
        ''' <see cref="BpeModel.CountTokensMemory"/>, the alternate-lookup path the real application
        ''' scans with (and the one the previous test does NOT reach). Beyond "no exception, results
        ''' identical to serial", this run is gated on that path having actually executed.
        ''' </summary>
        <TestMethod>
        Public Sub BpeModel_ConcurrentFusedEncodeWithCompactWordCache_MatchesSerial()
            AssertConcurrentEncodeSurvivesCompactStorm(BuildFusedBpeTokenizer(), memoryKeyedCountPath:=True)
        End Sub

        <TestMethod>
        Public Sub UnigramModel_ConcurrentEncodeCount_MatchesSerial()
            Dim tokenizer As Tokenizer = BuildUnigramTokenizer()
            Dim texts As List(Of String) = BuildTexts()
            AssertConcurrentMatchesSerial(tokenizer, texts)
        End Sub

        <TestMethod>
        Public Sub DeepSeekBpe_ConcurrentEncodeCount_MatchesSerial()
            If Not IO.File.Exists(DeepSeekPath) Then
                Assert.Inconclusive("deepseek-v4-flash/tokenizer.json not present")
                Return
            End If
            Dim tokenizer As Tokenizer = Tokenizer.FromFile(DeepSeekPath)
            Dim texts As List(Of String) = BuildTexts()
            AssertConcurrentMatchesSerial(tokenizer, texts)
        End Sub
    End Class

End Namespace
