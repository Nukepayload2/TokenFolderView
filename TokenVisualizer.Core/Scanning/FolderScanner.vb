Imports System
Imports System.Buffers
Imports System.Collections.Concurrent
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Threading
Imports System.Threading.Channels
Imports System.Threading.Tasks

Namespace Scanning

    ''' <summary>
    ''' Walks a directory tree, filters blacklisted / binary / oversized files, and counts tokens
    ''' for every remaining file using <see cref="Tokenizer.EncodeCount"/>. Enumeration and
    ''' tokenization run as a producer/consumer pipeline over a bounded channel; the pure
    ''' <see cref="BuildTree"/> turns the flat results into a <see cref="ScanTreeNode"/> hierarchy.
    ''' </summary>
    Public NotInheritable Class FolderScanner

        Private Const BinaryHeadBytes As Integer = 4096

        ''' <summary>Upper bound on files buffered between the enumeration task and the workers.</summary>
        Private Const ChannelCapacity As Integer = 1024

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
        ''' The enumeration task feeds a bounded channel that worker tasks drain in parallel;
        ''' <paramref name="ct"/> is honoured cooperatively by both sides.
        ''' </summary>
        Public Async Function ScanAsync(rootPath As String, Optional ct As CancellationToken = Nothing) As Task(Of ScanTreeNode)
            If String.IsNullOrWhiteSpace(rootPath) Then Throw New ArgumentException("Path must not be empty.", NameOf(rootPath))

            ' Time the whole load (enumeration + tokenization + tree build) so the status bar
            ' can report tokens/s and MB/s when the scan completes.
            ScanProgress.Start()

            ' Producer/consumer pipeline: the enumeration task pushes files into a bounded channel
            ' while the worker tasks tokenize them as they arrive. Enumeration overlaps tokenization
            ' (no "collect everything first" pause), and the bounded channel caps how far the
            ' producer can run ahead, bounding the memory held between the two stages.
            ' The variable is named "pipeline", not "channel": VB identifiers are case-insensitive,
            ' so a variable named "channel" would shadow the Channel type and break the factory call.
            Dim pipeline As Channel(Of (relativePath As String, fullPath As String, length As Long)) =
                Channel.CreateBounded(Of (relativePath As String, fullPath As String, length As Long))(
                    New BoundedChannelOptions(ChannelCapacity) With {
                        .FullMode = BoundedChannelFullMode.Wait,
                        .SingleWriter = True,
                        .SingleReader = False
                    })

            Dim results As New ConcurrentBag(Of (relativePath As String, length As Long, tokenCount As Integer))()

            ' Cap workers at one per physical core: logical processor count includes SMT siblings
            ' that add little for CPU-bound counting, and leaving half the machine free keeps the
            ' UI responsive while the scan runs.
            Dim workerCount As Integer = Math.Max(1, Environment.ProcessorCount \ 2)

            ' Producer: recursively enumerate, writing each file into the channel. The writer is
            ' always completed (even on error/cancel) so the consumers never hang. (The lambda is
            ' bound to a delegate first: VB forbids code after End Function on a multi-line lambda.)
            Dim produce As Func(Of Task) = Async Function()
                Try
                    Await EnumerateFilesAsync(rootPath, String.Empty, pipeline.Writer, ct)
                Finally
                    pipeline.Writer.Complete()
                End Try
            End Function
            Dim producer As Task = Task.Run(produce, ct)

            ' Consumers: one task per physical core reads files off the channel and token-counts them.
            ' (No Try/Finally here: VB forbids Await inside Finally, and the channel reader holds no
            ' OS resources, so abandoning the enumerator on cancellation is harmless.)
            Dim consume As Func(Of Task) = Async Function()
                Dim enumerator = pipeline.Reader.ReadAllAsync(ct).GetAsyncEnumerator()
                While Await enumerator.MoveNextAsync()
                    ProcessFile(enumerator.Current.relativePath,
                                enumerator.Current.fullPath,
                                enumerator.Current.length, results)
                End While
                Await enumerator.DisposeAsync()
            End Function
            Dim consumers As New List(Of Task)(workerCount)
            For i As Integer = 0 To workerCount - 1
                consumers.Add(Task.Run(consume, ct))
            Next

            ' Drain the consumers first so the whole channel is processed, then surface any
            ' producer fault (e.g. an I/O error or cancellation during enumeration) — without this
            ' await, a mid-enumeration error would be silently swallowed and the scan would return
            ' a partial tree.
            Await Task.WhenAll(consumers)
            Await producer

            ' All encoding workers are drained — no EncodeCount is in flight, so it is safe to drop
            ' the per-thread L1 word caches (freeing their memory on a client). The shared L2
            ' survives to warm the next scan, so a follow-up scan re-warms each worker's L1 from L2
            ' instead of starting cold.
            Dim bpe As Models.BpeModel = TryCast(_tokenizer.Model, Models.BpeModel)
            If bpe IsNot Nothing Then bpe.CompactWordCache()

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

        ''' <summary>
        ''' Recursively collects files (pruning blacklisted folders by name at any depth) and writes
        ''' each one into the channel for the worker tasks to consume. Backpressure on the bounded
        ''' channel naturally paces enumeration against tokenization.
        ''' </summary>
        Private Async Function EnumerateFilesAsync(dirFullPath As String,
                                                   relativePath As String,
                                                   writer As ChannelWriter(Of (relativePath As String, fullPath As String, length As Long)),
                                                   ct As CancellationToken) As Task
            ct.ThrowIfCancellationRequested()

            For Each subDirectory As String In Directory.EnumerateDirectories(dirFullPath)
                Dim name As String = Path.GetFileName(subDirectory)
                If ScanFilter.ShouldSkipFolder(name, _options.FolderBlacklist) Then Continue For
                Dim rel As String = If(relativePath.Length = 0, name, relativePath & "/" & name)
                Await EnumerateFilesAsync(subDirectory, rel, writer, ct)
            Next

            For Each filePath As String In Directory.EnumerateFiles(dirFullPath)
                Dim name As String = Path.GetFileName(filePath)
                Dim rel As String = If(relativePath.Length = 0, name, relativePath & "/" & name)
                Dim fi As New FileInfo(filePath)
                Await writer.WriteAsync((rel, filePath, fi.Length), ct)
            Next
        End Function

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
