Imports System.Collections.Concurrent
Imports System.Threading
Imports System.Threading.Tasks
Imports Tokenizers
Imports Tokenizers.Internal
Imports Tokenizers.Models

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
