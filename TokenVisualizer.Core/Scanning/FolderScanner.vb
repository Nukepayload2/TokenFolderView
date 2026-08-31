Imports System
Imports System.Buffers
Imports System.Collections.Concurrent
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks

Namespace Scanning

    ''' <summary>
    ''' Walks a directory tree, filters blacklisted / binary / oversized files, and counts tokens
    ''' for every remaining file using <see cref="Tokenizer.EncodeCount"/>. The scan is split into
    ''' a sequential enumeration phase and a parallel tokenization phase; the pure
    ''' <see cref="BuildTree"/> turns the flat results into a <see cref="ScanTreeNode"/> hierarchy.
    ''' </summary>
    Public NotInheritable Class FolderScanner

        Private Const BinaryHeadBytes As Integer = 4096

        Private ReadOnly _tokenizer As Tokenizer
        Private ReadOnly _options As ScanOptions

        Public Sub New(tokenizer As Tokenizer, options As ScanOptions)
            If tokenizer Is Nothing Then Throw New ArgumentNullException(NameOf(tokenizer))
            If options Is Nothing Then Throw New ArgumentNullException(NameOf(options))
            _tokenizer = tokenizer
            _options = options
            ScanProgress = New ScanProgress()
        End Sub

        ''' <summary>The live counters the UI polls while the scan runs.</summary>
        Public ReadOnly Property ScanProgress As ScanProgress

        ''' <summary>
        ''' Scans <paramref name="rootPath"/> recursively and returns the aggregated root node.
        ''' The parallel tokenization pass runs on the thread pool and honours
        ''' <paramref name="ct"/> for cooperative cancellation.
        ''' </summary>
        Public Async Function ScanAsync(rootPath As String, Optional ct As CancellationToken = Nothing) As Task(Of ScanTreeNode)
            If String.IsNullOrWhiteSpace(rootPath) Then Throw New ArgumentException("Path must not be empty.", NameOf(rootPath))

            ' Time the whole load (enumeration + tokenization + tree build) so the status bar
            ' can report tokens/s and MB/s when the scan completes.
            ScanProgress.Start()

            ' 1. Enumerate every file recursively (blacklisted folders are pruned by name).
            Dim files As New List(Of (relativePath As String, fullPath As String, length As Long))()
            EnumerateFiles(rootPath, String.Empty, files, ct)

            ' 2. Parallel tokenization pass. Cap workers at one per physical core: logical
            '    processor count includes SMT siblings that add little for CPU-bound counting,
            '    and leaving half the machine free keeps the UI responsive while the scan runs.
            Dim results As New ConcurrentBag(Of (relativePath As String, length As Long, tokenCount As Integer))()
            Dim parallelOptions As New ParallelOptions With {
                .MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount \ 2),
                .CancellationToken = ct
            }
            Await Task.Run(
                Sub()
                    Parallel.ForEach(files, parallelOptions,
                        Sub(file)
                            ProcessFile(file.relativePath, file.fullPath, file.length, results)
                        End Sub)
                End Sub, ct)

            ' 3. Pure tree construction.
            Dim rootName As String = Path.GetFileName(rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            If String.IsNullOrEmpty(rootName) Then rootName = rootPath
            Dim rootNode As ScanTreeNode = BuildTree(rootName, rootPath, results)
            ScanProgress.Stop()
            Return rootNode
        End Function

        ''' <summary>
        ''' Pure, I/O-free construction of the directory tree from flat file records. Directories
        ''' aggregate the token counts, file counts and sizes of all descendants; children are
        ''' sorted by name for a deterministic ordering.
        ''' </summary>
        Public Shared Function BuildTree(rootName As String,
                                         rootPath As String,
                                         files As IEnumerable(Of (relativePath As String, length As Long, tokenCount As Integer))) As ScanTreeNode
            Dim root As New ScanTreeNode(rootName, rootPath, True)
            Dim nodes As New Dictionary(Of String, ScanTreeNode)() From {
                {String.Empty, root}
            }

            For Each file As (relativePath As String, length As Long, tokenCount As Integer) In
                files.OrderBy(Function(f) f.relativePath, StringComparer.Ordinal)
                If String.IsNullOrEmpty(file.relativePath) Then Continue For
                Dim segments As String() = file.relativePath.Replace("\"c, "/"c).Split("/"c, StringSplitOptions.RemoveEmptyEntries)
                If segments.Length = 0 Then Continue For

                ' Walk or create the directory chain above the file.
                Dim current As ScanTreeNode = root
                Dim dirPath As String = String.Empty
                For i As Integer = 0 To segments.Length - 2
                    If dirPath.Length > 0 Then dirPath &= "/"
                    dirPath &= segments(i)

                    Dim child As ScanTreeNode = Nothing
                    If Not nodes.TryGetValue(dirPath, child) Then
                        Dim childPath As String = Path.Combine(rootPath, dirPath.Replace("/"c, Path.DirectorySeparatorChar))
                        child = New ScanTreeNode(segments(i), childPath, True)
                        nodes(dirPath) = child
                        current.AddChild(child)
                    End If
                    current = child
                Next

                ' Leaf file node.
                Dim fileName As String = segments(segments.Length - 1)
                Dim fullPath As String = Path.Combine(rootPath, file.relativePath.Replace("/"c, Path.DirectorySeparatorChar))
                Dim fileNode As New ScanTreeNode(fileName, fullPath, False) With {
                    .TokenCount = file.tokenCount,
                    .FileCount = 1,
                    .FileSize = file.length
                }
                current.AddChild(fileNode)
            Next

            AggregateNode(root)
            SortChildren(root)
            Return root
        End Function

        ' ------------------------------------------------------------------

        ''' <summary>
        ''' Handles a single file, opening it exactly once. A short head probe is read first and
        ''' checked for binary content (when enabled); text files then keep reading the rest of the
        ''' file on the same handle and are token-counted, so a counted file pays a single open.
        ''' Binary files are skipped after paying only the head read. Any read/decode/count failure
        ''' increments the error counter instead of aborting the scan.
        ''' </summary>
        Private Sub ProcessFile(relativePath As String,
                                fullPath As String,
                                length As Long,
                                results As ConcurrentBag(Of (relativePath As String, length As Long, tokenCount As Integer)))
            If ScanFilter.ShouldSkipFileSize(length, _options.MaxFileSizeBytes) Then
                ScanProgress.IncrementFilesSkipped()
                Return
            End If

            Dim headLen As Integer = CInt(Math.Min(length, BinaryHeadBytes))
            Dim probe As Byte() = ArrayPool(Of Byte).Shared.Rent(headLen)
            Dim buffer As Byte() = Nothing
            Try
                Dim bytesRead As Integer
                Dim isBinary As Boolean
                Using fs As New FileStream(fullPath, FileMode.Open, FileAccess.Read,
                                           FileShare.ReadWrite Or FileShare.Delete, 4096, FileOptions.SequentialScan)
                    bytesRead = ReadAtMost(fs, probe, 0, headLen)
                    isBinary = _options.CheckBinary AndAlso BinaryDetector.IsBinary(probe.AsMemory(0, bytesRead))
                    If Not isBinary Then
                        buffer = ArrayPool(Of Byte).Shared.Rent(CInt(length))
                        Array.Copy(probe, 0, buffer, 0, bytesRead)
                        bytesRead += ReadAtMost(fs, buffer, bytesRead, CInt(length) - bytesRead)
                    End If
                End Using

                If isBinary Then
                    ScanProgress.IncrementFilesSkipped()
                    Return
                End If

                Dim text As String = Encoding.UTF8.GetString(buffer, 0, bytesRead)
                Dim tokenCount As Integer = _tokenizer.EncodeCount(text)
                results.Add((relativePath, length, tokenCount))
                ScanProgress.IncrementFilesScanned()
                ScanProgress.AddTotalTokens(tokenCount)
            Catch ex As Exception
                ScanProgress.IncrementFilesWithErrors()
            Finally
                ArrayPool(Of Byte).Shared.Return(probe)
                If buffer IsNot Nothing Then ArrayPool(Of Byte).Shared.Return(buffer)
            End Try
        End Sub

        ''' <summary>
        ''' Reads up to <paramref name="count"/> bytes from <paramref name="fs"/> into
        ''' <paramref name="buffer"/> at <paramref name="offset"/>, tolerating short reads.
        ''' Returns the number of bytes actually read.
        ''' </summary>
        Private Shared Function ReadAtMost(fs As FileStream, buffer As Byte(), offset As Integer, count As Integer) As Integer
            Dim read As Integer = 0
            While read < count
                Dim n As Integer = fs.Read(buffer, offset + read, count - read)
                If n <= 0 Then Exit While
                read += n
            End While
            Return read
        End Function

        ''' <summary>Recursively collects files, pruning blacklisted folders by name at any depth.</summary>
        Private Sub EnumerateFiles(dirFullPath As String,
                                   relativePath As String,
                                   files As List(Of (relativePath As String, fullPath As String, length As Long)),
                                   ct As CancellationToken)
            ct.ThrowIfCancellationRequested()

            For Each subDirectory As String In Directory.EnumerateDirectories(dirFullPath)
                Dim name As String = Path.GetFileName(subDirectory)
                If ScanFilter.ShouldSkipFolder(name, _options.FolderBlacklist) Then Continue For
                Dim rel As String = If(relativePath.Length = 0, name, relativePath & "/" & name)
                EnumerateFiles(subDirectory, rel, files, ct)
            Next

            For Each filePath As String In Directory.EnumerateFiles(dirFullPath)
                Dim name As String = Path.GetFileName(filePath)
                Dim rel As String = If(relativePath.Length = 0, name, relativePath & "/" & name)
                Dim fi As New FileInfo(filePath)
                files.Add((rel, filePath, fi.Length))
            Next
        End Sub

        ''' <summary>Post-order pass that folds descendant counts into each directory node.</summary>
        Private Shared Function AggregateNode(node As ScanTreeNode) As (tokens As Long, files As Long, size As Long)
            If Not node.IsDirectory Then
                Return (node.TokenCount, node.FileCount, node.FileSize)
            End If

            Dim tokens As Long = 0
            Dim fileCount As Long = 0
            Dim size As Long = 0
            For Each child As ScanTreeNode In node.Children
                Dim agg = AggregateNode(child)
                tokens += agg.tokens
                fileCount += agg.files
                size += agg.size
            Next
            node.TokenCount = tokens
            node.FileCount = fileCount
            node.FileSize = size
            Return (tokens, fileCount, size)
        End Function

        ''' <summary>Recursively sorts each node's children by name for a deterministic ordering.</summary>
        Private Shared Sub SortChildren(node As ScanTreeNode)
            node.Children.Sort(Function(a As ScanTreeNode, b As ScanTreeNode) String.CompareOrdinal(a.Name, b.Name))
            For Each child As ScanTreeNode In node.Children
                If child.IsDirectory Then SortChildren(child)
            Next
        End Sub

    End Class
End Namespace
