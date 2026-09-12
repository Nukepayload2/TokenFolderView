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
                    ' Counting is inlined here on purpose: this is the per-file hot path, so it pays
                    ' exactly one call (CountFile) and maps the status to the progress counters
                    ' locally instead of going through a second helper. CountFile itself never
                    ' touches ScanProgress, which keeps the two concerns apart.
                    Dim result As FileCountResult = CountFile(enumerator.Current.fullPath, enumerator.Current.length)
                    Select Case result.Status
                        Case FileCountStatus.Counted
                            results.Add((enumerator.Current.relativePath, result.Length, CInt(result.TokenCount)))
                            ScanProgress.IncrementFilesScanned()
                            ScanProgress.AddTotalTokens(result.TokenCount)
                        Case FileCountStatus.SkippedSize, FileCountStatus.SkippedBinary
                            ScanProgress.IncrementFilesSkipped()
                        Case FileCountStatus.Missing, FileCountStatus.Error
                            ' A file that vanished between enumeration and reading used to throw
                            ' inside ProcessFile and was therefore counted as an error; keep that.
                            ScanProgress.IncrementFilesWithErrors()
                        Case Else
                            ' Unknown status: treat it as an error rather than silently dropping it.
                            ScanProgress.IncrementFilesWithErrors()
                    End Select
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

            ScanTreeEditor.Reaggregate(root)
            SortChildrenOfUnboundTree(root)
            Return root
        End Function

        ' ------------------------------------------------------------------

        ''' <summary>
        ''' Counts the tokens of a single file and reports what happened as a
        ''' <see cref="FileCountResult"/> instead of throwing. Opens the file exactly once: a short
        ''' head probe is read first and checked for binary content (when enabled); text files then
        ''' keep reading the rest of the file on the same handle, so a counted file pays a single
        ''' open. Binary and oversized files are skipped after paying only the head read (or nothing
        ''' at all), and any read/decode/count failure becomes a status rather than an exception so
        ''' one unreadable file never aborts a scan.
        ''' </summary>
        ''' <param name="fullPath">Absolute path of the file to read.</param>
        ''' <param name="length">File size in bytes, as observed while enumerating it.</param>
        ''' <remarks>
        ''' This method deliberately does not touch <see cref="ScanProgress"/>: the caller owns the
        ''' counters (see the inlined mapping in <see cref="ScanAsync"/>), which keeps the per-file
        ''' hot path at exactly one call. Also used by the incremental refresh path, which needs the
        ''' status without disturbing the whole-scan counters.
        ''' </remarks>
        Public Function CountFile(fullPath As String, length As Long) As FileCountResult
            ' This check must stay ahead of every CInt(length) below: narrowing a length beyond
            ' Integer.MaxValue would misbehave (a huge file must be skipped, not overflowed).
            If ScanFilter.ShouldSkipFileSize(length, _options.MaxFileSizeBytes) Then Return FileCountResult.SkippedSize

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

                If isBinary Then Return FileCountResult.SkippedBinary

                Dim text As String = Encoding.UTF8.GetString(buffer, 0, bytesRead)
                Return FileCountResult.Counted(_tokenizer.EncodeCount(text), length)
            Catch ex As FileNotFoundException
                ' The file (or its directory) went away between enumeration and this read. The
                ' incremental refresh path deletes the node for this status; a whole-tree scan
                ' counts it as an error, exactly as it did when this threw.
                Return FileCountResult.Missing
            Catch ex As DirectoryNotFoundException
                Return FileCountResult.Missing
            Catch ex As Exception
                ' Possibly transient (e.g. the file is locked right now), so the caller must not
                ' treat this as "the file is gone".
                Return FileCountResult.Error
            Finally
                ArrayPool(Of Byte).Shared.Return(probe)
                If buffer IsNot Nothing Then ArrayPool(Of Byte).Shared.Return(buffer)
            End Try
        End Function

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

        ''' <summary>Recursively sorts each node's children by name for a deterministic ordering.</summary>
        ''' <remarks>
        ''' DANGER: sorting is done by rebuilding each child collection (Clear + Add), which raises a
        ''' Reset on every list. It is only safe while the tree is still unbound, i.e. during
        ''' <see cref="BuildTree"/> right after assembly. Calling it on a tree that is bound to a
        ''' <c>TreeView</c> recreates every item container, so all expanded nodes and the selection are
        ''' lost - incremental updates must insert/remove instead (see <see cref="ScanTreeEditor"/>).
        ''' </remarks>
        Private Shared Sub SortChildrenOfUnboundTree(node As ScanTreeNode)
            Dim ordered As List(Of ScanTreeNode) =
                node.Children.OrderBy(Function(child As ScanTreeNode) child.Name, StringComparer.Ordinal).ToList()
            node.Children.Clear()
            For Each child As ScanTreeNode In ordered
                node.Children.Add(child)
            Next
            For Each child As ScanTreeNode In node.Children
                If child.IsDirectory Then SortChildrenOfUnboundTree(child)
            Next
        End Sub

    End Class
End Namespace
