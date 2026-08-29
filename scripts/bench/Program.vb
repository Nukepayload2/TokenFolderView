' DEV-ONLY benchmark harness for TokenVisualizer.Core.
' Measures EncodeCount throughput on the deepseek tokenizer and prints a
' machine-parseable "DOTNET|..." line that scripts/bench.ps1 consumes.
' Mirrors scripts/bench_ref.py exactly so the inputs are byte-identical.
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.IO
Imports System.Linq
Imports System.Text
Imports Tokenizers

Module Program

    ' Same deterministic paragraph the Python reference decodes from this base64
    ' (ASCII + CJK + digits + punctuation + emoji, 451 UTF-8 bytes per copy).
    Private Const ParaB64 As String = "RGVlcFNlZWsgaXMgYW4gYWR2YW5jZWQgbGFyZ2UgbGFuZ3VhZ2UgbW9kZWwgcGxhdGZvcm0uClRoZSBxdWljayBicm93biBmb3gganVtcHMgb3ZlciB0aGUgbGF6eSBkb2cgMTIzNDU2Nzg5MCEKSGVsbG8gd29ybGQsIHRoaXMgaXMgYSB0b2tlbml6YXRpb24gYmVuY2htYXJrLgpDaGluZXNlIHdvcmQgc2VnbWVudGF0aW9uIHRlc3Q6IEFJLCBtYWNoaW5lIGxlYXJuaW5nLCBOTFAuClN5bWJvbHM6IEAjJCVeJiooKV8rLT1bXXt9OzpcfH5gCkZ1bGx3aWR0aCBDSksgcHVuY3R1YXRpb246IO+8ge+8n+OAgu+8jO+8m++8muOAge+8iO+8ieOAiuOAi+OAkOOAkQpDSksgd29yZHM6IOS6uuW3peaZuuiDvSDmnLrlmajlrabkuaAg6Ieq54S26K+t6KiA5aSE55CGIOWkp+ivreiogOaooeWeiwpNaXhlZDogYWJjMTIz5Lit5paH5a2X56ym8J+YgGVtb2pp8J+QiWRyYWdvbiBhbmQg5pWw5a2XMTIzNDUuCg=="
    Private ReadOnly Para As String = Encoding.UTF8.GetString(Convert.FromBase64String(ParaB64))

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

        If String.IsNullOrEmpty(tokenizerPath) Then tokenizerPath = FindDeepseekTokenizer()
        If String.IsNullOrEmpty(tokenizerPath) OrElse Not File.Exists(tokenizerPath) Then
            Console.Error.WriteLine("DOTNET|error|tokenizer file not found")
            Return 2
        End If

        ' ---- Build the benchmark text (byte-identical to scripts/bench_ref.py) ----
        Dim text As String
        If Not String.IsNullOrEmpty(path) Then
            text = BuildTextFromPath(path)
        Else
            If repeat <= 0 Then
                Dim bytesPerCopy As Integer = Encoding.UTF8.GetByteCount(Para)
                repeat = Math.Max(1, CInt(2000000 \ bytesPerCopy))
            End If
            Dim sb As New StringBuilder(Para.Length * repeat)
            For i As Integer = 0 To repeat - 1
                sb.Append(Para)
            Next
            text = sb.ToString()
        End If

        Dim inputBytes As Long = Encoding.UTF8.GetByteCount(text)
        Dim inputMb As Double = inputBytes / (1024.0 * 1024.0)

        ' ---- Load the tokenizer ----
        Dim tokenizer As Tokenizer
        Try
            tokenizer = Tokenizer.Load(tokenizerPath)
        Catch ex As Exception
            Console.Error.WriteLine("DOTNET|error|failed to load tokenizer: " & ex.Message)
            Return 3
        End Try

        ' ---- Warmup (JIT + regex tables + added-token cache) ----
        Dim warmTokens As Integer = tokenizer.EncodeCount(text)

        ' ---- Measure best of N ----
        Dim bestTicks As Long = Long.MaxValue
        Dim tokenCount As Integer = 0
        For i As Integer = 0 To iterations - 1
            Dim sw As New Stopwatch()
            sw.Start()
            tokenCount = tokenizer.EncodeCount(text)
            sw.Stop()
            If sw.ElapsedTicks < bestTicks Then bestTicks = sw.ElapsedTicks
        Next

        Dim elapsedSec As Double = bestTicks / Stopwatch.Frequency
        Dim elapsedMs As Double = elapsedSec * 1000.0
        Dim mbps As Double = inputMb / elapsedSec
        Dim tps As Double = tokenCount / elapsedSec

        Console.WriteLine(
            $"DOTNET|input_mb={inputMb:F6}|tokens={tokenCount}|elapsed_ms={elapsedMs:F1}|mb_per_s={mbps:F1}|tokens_per_s={tps:F0}")
        Console.WriteLine(
            $"dotnet: {inputMb:F2} MB in {elapsedMs:F1} ms -> {mbps:F1} MB/s, {tps:F0} tokens/s ({tokenCount} tokens)  [warmup={warmTokens}]")
        Return 0
    End Function

    ' ------------------------------------------------------------------
    ' Text building (must match scripts/bench_ref.py's collect order so the
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
                total += Encoding.UTF8.GetByteCount(content)
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
