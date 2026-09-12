' DEV-ONLY benchmark harness for TokenVisualizer.Core.
' Measures EncodeCount throughput on the deepseek tokenizer and prints a
' machine-parseable "DOTNET|..." line that tests/performance/bench.ps1 consumes.
' Mirrors tests/performance/bench_ref.py exactly so the inputs are byte-identical.
' With --scan <dir> it instead runs the FolderScanner end-to-end pass (see RunScanMode).
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.IO
Imports System.Linq
Imports System.Text
Imports Tokenizers
Imports Tokenizers.Internal
Imports Tokenizers.Models
Imports Tokenizers.Scanning

Module Program

    ' Same deterministic paragraph the Python reference decodes from this base64
    ' (ASCII + CJK + digits + punctuation + emoji, 451 UTF-8 bytes per copy).
    Private Const ParaB64 As String = "RGVlcFNlZWsgaXMgYW4gYWR2YW5jZWQgbGFyZ2UgbGFuZ3VhZ2UgbW9kZWwgcGxhdGZvcm0uClRoZSBxdWljayBicm93biBmb3gganVtcHMgb3ZlciB0aGUgbGF6eSBkb2cgMTIzNDU2Nzg5MCEKSGVsbG8gd29ybGQsIHRoaXMgaXMgYSB0b2tlbml6YXRpb24gYmVuY2htYXJrLgpDaGluZXNlIHdvcmQgc2VnbWVudGF0aW9uIHRlc3Q6IEFJLCBtYWNoaW5lIGxlYXJuaW5nLCBOTFAuClN5bWJvbHM6IEAjJCVeJiooKV8rLT1bXXt9OzpcfH5gCkZ1bGx3aWR0aCBDSksgcHVuY3R1YXRpb246IO+8ge+8n+OAgu+8jO+8m++8muOAge+8iO+8ieOAiuOAi+OAkOOAkQpDSksgd29yZHM6IOS6uuW3peaZuuiDvSDmnLrlmajlrabkuaAg6Ieq54S26K+t6KiA5aSE55CGIOWkp+ivreiogOaooeWeiwpNaXhlZDogYWJjMTIz5Lit5paH5a2X56ym8J+YgGVtb2pp8J+QiWRyYWdvbiBhbmQg5pWw5a2XMTIzNDUuCg=="
    Private ReadOnly Para As String = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(ParaB64))

    Private Const TextLimitBytes As Long = 4L * 1024L * 1024L
    Private Const FileLimitChars As Integer = 256 * 1024

    Private ReadOnly TextExtensions As HashSet(Of String) =
        New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
            ".vb", ".py", ".cs", ".fs", ".fsx", ".txt", ".md",
            ".json", ".xml", ".html", ".css", ".js", ".ts"
        }

    Private ReadOnly SkipDirs As HashSet(Of String) =
        New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
            "bin", "obj", ".vs", ".git", "node_modules", "target"
        }

    Function Main(args As String()) As Integer
        Dim tokenizerPath As String = GetArg(args, "--tokenizer", "")
        Dim path As String = GetArg(args, "--path", "")
        Dim repeat As Integer = GetIntArg(args, "--repeat", 0)
        Dim iterations As Integer = GetIntArg(args, "--iterations", 3)

        ' --scan <dir> selects the FolderScanner instrument instead of the EncodeCount
        ' measurement. A bare --scan (no directory) is a usage error rather than a silent
        ' fallback into the EncodeCount mode.
        Dim scanPath As String = GetArg(args, "--scan", "")
        If scanPath.Length = 0 AndAlso HasArg(args, "--scan") Then
            Console.Error.WriteLine("DOTNET|error|--scan requires a directory argument")
            Return 2
        End If

        ' Optional BPE word-cache overrides for the capacity x max-word sweep (task #38).
        ' --cache-capacity N: 0/1000/10000/32000 (Nothing = model default 10000).
        ' --cache-max-word  N: 0 = unlimited; >0 = cache-eligibility word length limit.
        Dim cacheCapacity As Integer? = Nothing
        Dim cacheMaxWord As Integer? = Nothing
        Dim cc As String = GetArg(args, "--cache-capacity", "")
        Dim ccVal As Integer
        If cc.Length > 0 AndAlso Integer.TryParse(cc, ccVal) Then cacheCapacity = ccVal
        Dim mw As String = GetArg(args, "--cache-max-word", "")
        Dim mwVal As Integer
        If mw.Length > 0 AndAlso Integer.TryParse(mw, mwVal) AndAlso mwVal > 0 Then cacheMaxWord = mwVal

        If String.IsNullOrEmpty(tokenizerPath) Then tokenizerPath = FindDeepseekTokenizer()
        If String.IsNullOrEmpty(tokenizerPath) OrElse Not File.Exists(tokenizerPath) Then
            Console.Error.WriteLine("DOTNET|error|tokenizer file not found")
            Return 2
        End If

        If scanPath.Length > 0 Then Return RunScanMode(args, tokenizerPath, scanPath)

        ' ---- Build the benchmark text (byte-identical to tests/performance/bench_ref.py) ----
        Dim text As String
        If Not String.IsNullOrEmpty(path) Then
            text = BuildTextFromPath(path)
        Else
            If repeat <= 0 Then
                Dim bytesPerCopy As Integer = System.Text.Encoding.UTF8.GetByteCount(Para)
                repeat = Math.Max(1, CInt(2000000 \ bytesPerCopy))
            End If
            Dim sb As New StringBuilder(Para.Length * repeat)
            For i As Integer = 0 To repeat - 1
                sb.Append(Para)
            Next
            text = sb.ToString()
        End If

        Dim inputBytes As Long = System.Text.Encoding.UTF8.GetByteCount(text)
        Dim inputMb As Double = inputBytes / (1024.0 * 1024.0)

        ' ---- Load the tokenizer ----
        Dim tokenizer As Tokenizer
        Try
            tokenizer = Tokenizer.Load(tokenizerPath, cacheCapacity, cacheMaxWord)
        Catch ex As Exception
            Console.Error.WriteLine("DOTNET|error|failed to load tokenizer: " & ex.Message)
            Return 3
        End Try

        ' Cache statistics are only gathered when a cache override is in play; enabling them on a
        ' default run would add a per-word branch to the hot path for no benchmark benefit.
        Dim bpe As BpeModel = TryCast(tokenizer.Model, BpeModel)
        Dim statsOn As Boolean = bpe IsNot Nothing AndAlso (cacheCapacity.HasValue OrElse cacheMaxWord.HasValue)
        If statsOn Then bpe.EnableCacheStats()

        ' ---- Warmup (JIT + regex tables + added-token cache) ----
        Dim warmTokens As Integer = tokenizer.EncodeCount(text)

        ' ---- Measure best of N ----
        If statsOn Then bpe.ResetCacheStats()
        Dim bestTicks As Long = Long.MaxValue
        Dim tokenCount As Integer = 0
        For i As Integer = 0 To iterations - 1
            Dim sw As New Stopwatch()
            sw.Start()
            tokenCount = tokenizer.EncodeCount(text)
            sw.Stop()
            If sw.ElapsedTicks < bestTicks Then bestTicks = sw.ElapsedTicks
        Next
        Dim stats As New CacheStats()
        If statsOn Then stats = bpe.GetCacheStats()

        Dim elapsedSec As Double = bestTicks / Stopwatch.Frequency
        Dim elapsedMs As Double = elapsedSec * 1000.0
        Dim mbps As Double = inputMb / elapsedSec
        Dim tps As Double = tokenCount / elapsedSec

        Console.WriteLine(
            $"DOTNET|input_mb={inputMb:F6}|tokens={tokenCount}|elapsed_ms={elapsedMs:F1}|mb_per_s={mbps:F1}|tokens_per_s={tps:F0}|cache_hits={stats.Hits}|cache_misses={stats.Misses}|cache_skips={stats.Skips}|cache_evictions={stats.Evictions}")
        Console.WriteLine(
            $"dotnet: {inputMb:F2} MB in {elapsedMs:F1} ms -> {mbps:F1} MB/s, {tps:F0} tokens/s ({tokenCount} tokens)  [warmup={warmTokens}]  cache: hits={stats.Hits} misses={stats.Misses} skips={stats.Skips} evictions={stats.Evictions}")
        Return 0
    End Function

    ' ------------------------------------------------------------------
    ' Folder-scan mode (--scan <dir>): dev-only instrument over FolderScanner.
    '
    ' It MUST stay a pure delegator: it never walks the directory itself, never
    ' filters, and never adjusts a count. Every number below is read back from
    ' ScanProgress or from the returned root node, so a pre-change and a
    ' post-change binary measure exactly the same pipeline under exactly the same
    ' fixed options (default blacklist / size limit / binary detection), which is
    ' what makes a before/after comparison meaningful.
    '
    ' Per iteration it prints one DOTNET|scan|... line; after the last iteration it
    ' prints one DOTNET|scan_median|... line with the medians and the observed range.
    ' ------------------------------------------------------------------
    Private Function RunScanMode(args As String(), tokenizerPath As String, scanPath As String) As Integer
        Dim iterations As Integer = GetIntArg(args, "--iterations", 5)
        If iterations < 1 Then iterations = 5

        If Not Directory.Exists(scanPath) Then
            Console.Error.WriteLine("DOTNET|error|scan directory not found: " & scanPath)
            Return 2
        End If

        Dim tokenizer As Tokenizer
        Try
            tokenizer = Tokenizer.Load(tokenizerPath)
        Catch ex As Exception
            Console.Error.WriteLine("DOTNET|error|failed to load tokenizer: " & ex.Message)
            Return 3
        End Try

        Dim elapsedMs As New List(Of Double)(iterations)
        Dim tokensPerSec As New List(Of Double)(iterations)
        Dim mbPerSec As New List(Of Double)(iterations)

        ' Every iteration must observe the same workload; a mismatch means the tree changed
        ' under the instrument (or a count is unstable) and the runs must not be compared.
        Dim firstTokens As Long = -1
        Dim firstFiles As Long = -1
        Dim firstSkipped As Long = -1
        Dim firstErrors As Long = -1
        Dim firstBytes As Long = -1
        Dim consistent As Boolean = True

        For i As Integer = 1 To iterations
            ' Fresh default options and a fresh scanner per iteration: the comparison relies on
            ' the blacklist / size limit / binary detection staying exactly at their defaults.
            Dim options As New ScanOptions()
            Dim scanner As New FolderScanner(tokenizer, options)
            Dim root As ScanTreeNode = scanner.ScanAsync(scanPath).GetAwaiter().GetResult()

            Dim tokens As Long = root.TokenCount
            Dim files As Long = root.FileCount
            Dim bytes As Long = root.FileSize
            Dim skipped As Long = scanner.ScanProgress.ReadFilesSkipped()
            Dim errors As Long = scanner.ScanProgress.ReadFilesWithErrors()
            Dim progressTokens As Long = scanner.ScanProgress.ReadTotalTokens()
            Dim ms As Double = scanner.ScanProgress.Elapsed.TotalMilliseconds
            Dim sec As Double = ms / 1000.0
            Dim tps As Double = If(sec > 0.0, tokens / sec, 0.0)
            Dim mbps As Double = If(sec > 0.0, bytes / (1024.0 * 1024.0) / sec, 0.0)

            elapsedMs.Add(ms)
            tokensPerSec.Add(tps)
            mbPerSec.Add(mbps)

            If i = 1 Then
                firstTokens = tokens
                firstFiles = files
                firstSkipped = skipped
                firstErrors = errors
                firstBytes = bytes
            ElseIf tokens <> firstTokens OrElse files <> firstFiles OrElse skipped <> firstSkipped OrElse
                   errors <> firstErrors OrElse bytes <> firstBytes Then
                consistent = False
            End If
            ' The tree total and the live counter must agree; a divergence means the scan
            ' dropped or double-counted a file.
            If progressTokens <> tokens Then consistent = False

            Console.WriteLine(
                $"DOTNET|scan|iter={i}|tokens={tokens}|progress_tokens={progressTokens}|files={files}" &
                $"|skipped={skipped}|errors={errors}|bytes={bytes}|elapsed_ms={ms:F1}" &
                $"|tokens_per_s={tps:F0}|mb_per_s={mbps:F2}")
        Next

        Dim medMs As Double = Median(elapsedMs)
        Dim medTps As Double = Median(tokensPerSec)
        Dim medMbps As Double = Median(mbPerSec)

        Console.WriteLine(
            $"DOTNET|scan_median|dir={scanPath}|iterations={iterations}|tokens={firstTokens}" &
            $"|files={firstFiles}|skipped={firstSkipped}|errors={firstErrors}|bytes={firstBytes}" &
            $"|elapsed_ms={medMs:F1}|tokens_per_s={medTps:F0}|mb_per_s={medMbps:F2}" &
            $"|elapsed_ms_min={elapsedMs.Min():F1}|elapsed_ms_max={elapsedMs.Max():F1}" &
            $"|tokens_per_s_min={tokensPerSec.Min():F0}|tokens_per_s_max={tokensPerSec.Max():F0}" &
            $"|consistent={If(consistent, "true", "false")}")
        Console.WriteLine(
            $"scan: {firstFiles} files, {firstTokens} tokens, {firstBytes} bytes in {medMs:F1} ms " &
            $"(median of {iterations}) -> {medTps:F0} tokens/s, {medMbps:F2} MB/s; " &
            $"skipped={firstSkipped} errors={firstErrors} consistent={If(consistent, "true", "false")}")
        Return 0
    End Function

    ''' <summary>Median of a non-empty sequence (the mean of the two middle values for an even count).</summary>
    Private Function Median(values As List(Of Double)) As Double
        Dim sorted As List(Of Double) = values.OrderBy(Function(v As Double) v).ToList()
        Dim mid As Integer = sorted.Count \ 2
        If sorted.Count Mod 2 = 1 Then Return sorted(mid)
        Return (sorted(mid - 1) + sorted(mid)) / 2.0
    End Function

    ' ------------------------------------------------------------------
    ' Text building (must match tests/performance/bench_ref.py's collect order so the
    ' parity run compares identical bytes).
    ' ------------------------------------------------------------------
    Private Function BuildTextFromPath(path As String) As String
        Dim parts As New List(Of String)()
        Dim total As Long = 0
        If Directory.Exists(path) Then
            CollectFiles(path, parts, total)
        ElseIf File.Exists(path) Then
            AddFile(path, parts, total)
        End If
        Return String.Join(vbLf, parts)
    End Function

    Private Sub AddFile(fp As String, parts As List(Of String), ByRef total As Long)
        If total >= TextLimitBytes Then Return
        Try
            Dim encStrict As New UTF8Encoding(False, True) ' throw on invalid UTF-8 (mirrors Python)
            Dim content As String = File.ReadAllText(fp, encStrict)
            If content.Length > FileLimitChars Then content = content.Substring(0, FileLimitChars)
            If content.Length > 0 Then
                parts.Add(content)
                total += System.Text.Encoding.UTF8.GetByteCount(content)
            End If
        Catch
            ' Skip files that aren't valid UTF-8.
        End Try
    End Sub

    Private Sub CollectFiles(dir As String, parts As List(Of String), ByRef total As Long)
        Dim entries As String()
        Try
            entries = Directory.GetFileSystemEntries(dir)
        Catch
            Return
        End Try
        Array.Sort(entries, StringComparer.Ordinal)

        Dim files As New List(Of String)()
        Dim dirs As New List(Of String)()
        For Each e As String In entries
            If Directory.Exists(e) Then
                Dim name As String = Path.GetFileName(e)
                If Not SkipDirs.Contains(name) Then dirs.Add(e)
            Else
                files.Add(e)
            End If
        Next

        For Each f As String In files
            If total >= TextLimitBytes Then Exit For
            If TextExtensions.Contains(Path.GetExtension(f)) Then AddFile(f, parts, total)
        Next
        For Each d As String In dirs
            If total >= TextLimitBytes Then Exit Sub
            CollectFiles(d, parts, total)
        Next
    End Sub

    ' ------------------------------------------------------------------
    ' Argument helpers + tokenizer discovery
    ' ------------------------------------------------------------------
    Private Function GetArg(args As String(), key As String, defaultValue As String) As String
        For i As Integer = 0 To args.Length - 2
            If String.Equals(args(i), key, StringComparison.OrdinalIgnoreCase) Then
                Return args(i + 1)
            End If
        Next
        Return defaultValue
    End Function

    ''' <summary>True when <paramref name="key"/> appears anywhere in the argument list.</summary>
    Private Function HasArg(args As String(), key As String) As Boolean
        For Each arg As String In args
            If String.Equals(arg, key, StringComparison.OrdinalIgnoreCase) Then Return True
        Next
        Return False
    End Function

    Private Function GetIntArg(args As String(), key As String, defaultValue As Integer) As Integer
        Dim s As String = GetArg(args, key, "")
        Dim v As Integer
        If Integer.TryParse(s, v) Then Return v
        Return defaultValue
    End Function

    Private Function FindDeepseekTokenizer() As String
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        For i As Integer = 0 To 10
            Dim candidate As String = Path.Combine(dir.FullName, "deepseek-v4-flash", "tokenizer.json")
            If File.Exists(candidate) Then Return candidate
            If dir.Parent Is Nothing Then Exit For
            dir = dir.Parent
        Next
        Return Nothing
    End Function

End Module
