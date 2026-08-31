Imports System.Buffers
Imports System.IO
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports Avalonia
Imports Avalonia.Controls
Imports Avalonia.Platform.Storage
Imports Avalonia.Threading
Imports Tokenizers
Imports Tokenizers.Scanning
Imports TokenVisualizer.Controls
Imports TokenVisualizer.Services

Namespace Views

    Partial Class ExplorerPage
        Inherits UserControl

        ' Generation counter guards the async file-view load: a click on file B invalidates any
        ' in-flight load started for file A.
        Private _loadGeneration As Integer

        Private _currentQuery As String = ""

        Public Sub New()
            InitializeComponent()
            TxtActiveTokenizer.Text = "正在加载…"
        End Sub

        ' ------------------------------------------------------------------
        ' Lifetime / tokenizer bootstrap
        ' ------------------------------------------------------------------

        Private Async Sub ExplorerPage_Loaded() Handles Me.Loaded
            Await Task.Run(Sub() AppState.EnsureActiveTokenizer())
            UpdateTokenizerLabel()
            RefreshStatus()
        End Sub

        Private Sub UpdateTokenizerLabel()
            Dim state = AppState.Current
            If state.ActiveTokenizer IsNot Nothing Then
                TxtActiveTokenizer.Text = state.ActiveTokenizerName
            Else
                TxtActiveTokenizer.Text = "未找到 tokenizer.json"
            End If
        End Sub

        ' ------------------------------------------------------------------
        ' Public API used by MainWindow
        ' ------------------------------------------------------------------

        ''' <summary>
        ''' Filters the visible tree to nodes whose name matches the query (case-insensitive),
        ''' preserving hierarchy. A directory that matches by name keeps its whole subtree; a
        ''' directory that only contains matches is wrapped with just those descendants.
        ''' </summary>
        Public Sub ApplySearchFilter(query As String)
            _currentQuery = If(query, "").Trim()
            Dim root = AppState.Current.RootNode
            If root Is Nothing Then Return

            If String.IsNullOrEmpty(_currentQuery) Then
                FileTree.ItemsSource = {root}
                Return
            End If

            Dim filtered = FilterNode(root, _currentQuery)
            If filtered Is Nothing Then
                FileTree.ItemsSource = Nothing
            Else
                FileTree.ItemsSource = {filtered}
            End If
        End Sub

        ''' <summary>Refreshes the root-count label and the scan state UI.</summary>
        Public Sub RefreshStatus()
            Dim root = AppState.Current.RootNode
            If root IsNot Nothing Then
                TblRootCount.Text = $"{root.FileCount:N0} 个文件 · {root.TokenCount:N0} tokens"
            Else
                TblRootCount.Text = ""
            End If
            UpdateScanUi()
        End Sub

        Private Sub UpdateScanUi()
            Dim scanning = AppState.Current.IsScanning
            BtnCancelScan.IsVisible = scanning
            BtnOpenFolder.IsEnabled = Not scanning
            BtnRescan.IsEnabled = (Not scanning) AndAlso Not String.IsNullOrEmpty(AppState.Current.CurrentScanPath)
        End Sub

        ' ------------------------------------------------------------------
        ' Open / rescan / cancel
        ' ------------------------------------------------------------------

        Private Async Sub BtnOpenFolder_Click() Handles BtnOpenFolder.Click
            AppState.EnsureActiveTokenizer()
            If AppState.Current.ActiveTokenizer Is Nothing Then
                UpdateTokenizerLabel()
                Return
            End If

            Dim tl = TopLevel.GetTopLevel(Me)
            If tl Is Nothing Then Return

            Dim folders = Await tl.StorageProvider.OpenFolderPickerAsync(
                New FolderPickerOpenOptions With {
                    .Title = "选择要扫描的文件夹",
                    .AllowMultiple = False
                })
            If folders.Count < 1 Then Return

            Dim path = folders(0).Path.LocalPath
            If String.IsNullOrEmpty(path) Then Return

            AppState.Current.CurrentScanPath = path
            Await StartScanAsync(path)
        End Sub

        Private Async Sub BtnRescan_Click() Handles BtnRescan.Click
            Dim path = AppState.Current.CurrentScanPath
            If String.IsNullOrEmpty(path) Then Return
            AppState.EnsureActiveTokenizer()
            If AppState.Current.ActiveTokenizer Is Nothing Then
                UpdateTokenizerLabel()
                Return
            End If
            Await StartScanAsync(path)
        End Sub

        Private Sub BtnCancelScan_Click() Handles BtnCancelScan.Click
            Dim scan = AppState.Current.ActiveScan
            If scan IsNot Nothing Then
                Try
                    scan.ScanProgress.Cancellation.Cancel()
                Catch
                End Try
            End If
        End Sub

        ' ------------------------------------------------------------------
        ' Scan pipeline
        ' ------------------------------------------------------------------

        Private Async Function StartScanAsync(path As String) As Task
            Dim tokenizer = AppState.Current.ActiveTokenizer
            If tokenizer Is Nothing Then Return

            ' Cancel any previous scan.
            If AppState.Current.ActiveScan IsNot Nothing Then
                Try
                    AppState.Current.ActiveScan.ScanProgress.Cancellation.Cancel()
                Catch
                End Try
            End If

            Dim scanner As New FolderScanner(tokenizer, BuildScanOptions())
            AppState.Current.ActiveScan = scanner
            AppState.Current.RootNode = Nothing
            AppState.Current.IsScanning = True
            AppState.Current.CurrentScanPath = path

            FileTree.ItemsSource = Nothing
            ClearContentView()
            RefreshStatus()

            Dim ct = scanner.ScanProgress.Cancellation.Token
            Try
                ' Enumeration in ScanAsync runs synchronously before its first Await, so run the
                ' whole thing on the thread pool to keep the UI responsive.
                Dim root = Await Task.Run(Function() scanner.ScanAsync(path, ct), ct)

                AppState.Current.RootNode = root
                ApplySearchFilter(_currentQuery)
                ExpandAllNodes()
            Catch ex As OperationCanceledException
                AppState.Current.RootNode = Nothing
                FileTree.ItemsSource = Nothing
            Catch ex As Exception
                AppState.Current.RootNode = Nothing
                FileTree.ItemsSource = Nothing
                TblRootCount.Text = ""
                TxtActiveTokenizer.Text = $"扫描失败：{ex.Message}"
            Finally
                AppState.Current.IsScanning = False
                RefreshStatus()
            End Try
        End Function

        ''' <summary>
        ''' Builds the scanner options from the persisted settings (folder blacklist, max file size,
        ''' binary detection). Settings live in <see cref="AppSettings"/>; ScanOptions is the
        ''' scanner's per-scan value snapshot, so each scan reads the latest saved values.
        ''' </summary>
        Private Shared Function BuildScanOptions() As ScanOptions
            Dim settings = SettingsService.Load()
            Return New ScanOptions With {
                .FolderBlacklist = settings.BlacklistedFolderNames,
                .MaxFileSizeBytes = CLng(settings.MaxFileSizeMb * 1024 * 1024),
                .CheckBinary = settings.CheckBinary
            }
        End Function

        Private Sub ExpandAllNodes()
            Dispatcher.UIThread.Post(Sub() ExpandContainers(FileTree), DispatcherPriority.Loaded)
        End Sub

        Private Sub ExpandContainers(tree As TreeView)
            For i As Integer = 0 To tree.ItemCount - 1
                Dim c = TryCast(tree.ContainerFromIndex(i), TreeViewItem)
                If c IsNot Nothing Then
                    c.IsExpanded = True
                    ExpandChildren(c)
                End If
            Next
        End Sub

        Private Sub ExpandChildren(item As TreeViewItem)
            For i As Integer = 0 To item.ItemCount - 1
                Dim c = TryCast(item.ContainerFromIndex(i), TreeViewItem)
                If c IsNot Nothing Then
                    c.IsExpanded = True
                    ExpandChildren(c)
                End If
            Next
        End Sub

        ' ------------------------------------------------------------------
        ' File selection + colored token view
        ' ------------------------------------------------------------------

        Private Sub FileTree_SelectionChanged(sender As Object, e As SelectionChangedEventArgs) Handles FileTree.SelectionChanged
            Dim node = TryCast(FileTree.SelectedItem, ScanTreeNode)
            If node Is Nothing OrElse node.IsDirectory Then
                ClearContentView()
                Return
            End If
            LoadFileAsync(node)
        End Sub

        Private Sub ClearContentView()
            Interlocked.Increment(_loadGeneration)
            TokenLines.ItemsSource = Nothing
            TblFileName.Text = ""
            TblFileMeta.Text = ""
            TokenLines.IsEmptyVisible = True
        End Sub

        Private Async Sub LoadFileAsync(node As ScanTreeNode)
            Dim gen = Interlocked.Increment(_loadGeneration)
            Dim tokenizer = AppState.Current.ActiveTokenizer
            If tokenizer Is Nothing Then Return

            TblFileName.Text = node.Name
            TblFileMeta.Text = "加载中…"
            TokenLines.IsEmptyVisible = True
            TokenLines.ItemsSource = Nothing
            TokenLines.ResetScroll()

            Try
                Dim result = Await Task.Run(Function() BuildLines(node.FullPath, tokenizer))
                If gen <> _loadGeneration Then Return ' stale load, superseded by a newer selection

                TblFileName.Text = result.fileName
                TblFileMeta.Text = $"{FormatBytes(result.byteLength)} · {result.charCount:N0} 字符 · {result.tokenCount:N0} tokens"
                TokenLines.ItemsSource = result.lines
                TokenLines.IsEmptyVisible = False
            Catch ex As OperationCanceledException
            Catch ex As Exception
                If gen <> _loadGeneration Then Return
                TblFileMeta.Text = ""
                TokenLines.ShowText($"无法读取文件：{ex.Message}")
            End Try
        End Sub

        ''' <summary>
        ''' Heavy work for the colored view, run off the UI thread. Only cheap integer structures are
        ''' built here (decoded text, token spans, per-line records); the per-line run tuples and
        ''' Avalonia inlines are computed lazily on the UI thread by <see cref="TokenLine"/> when a
        ''' virtualized container is materialized.
        ''' </summary>
        Private Shared Function BuildLines(fullPath As String,
                                           tokenizer As Tokenizer) As (fileName As String, byteLength As Long, charCount As Integer, tokenCount As Integer, lines As List(Of TokenLine))
            Dim fileName = Path.GetFileName(fullPath)
            Dim fi As New FileInfo(fullPath)
            Dim length = fi.Length
            If length > Integer.MaxValue Then
                Throw New IOException("文件过大，无法在视图中显示。")
            End If

            Dim buffer As Byte() = ArrayPool(Of Byte).Shared.Rent(CInt(length))
            Try
                Dim bytesRead = ReadFilePrefix(fullPath, buffer, CInt(length))

                ' Lenient decode: valid UTF-8 yields the exact text; invalid bytes become U+FFFD.
                ' This is behaviourally identical to a strict decode with a lenient fallback, but
                ' never throws (a strict decoder would allocate a DecoderFallbackException).
                Dim text As String = Encoding.UTF8.GetString(buffer, 0, bytesRead)

                Dim spans = tokenizer.EncodeWithSpans(text)
                Dim lines = TokenizedTextView.BuildLines(text, spans)
                Return (fileName, length, text.Length, spans.Count, lines)
            Finally
                ArrayPool(Of Byte).Shared.Return(buffer)
            End Try
        End Function

        Private Shared Function ReadFilePrefix(path As String, buffer As Byte(), desiredBytes As Integer) As Integer
            If desiredBytes = 0 Then Return 0
            Using fs As New FileStream(path, FileMode.Open, FileAccess.Read,
                                       FileShare.ReadWrite Or FileShare.Delete, 4096, FileOptions.SequentialScan)
                Dim offset As Integer = 0
                While offset < desiredBytes
                    Dim read = fs.Read(buffer, offset, desiredBytes - offset)
                    If read <= 0 Then Exit While
                    offset += read
                End While
                Return offset
            End Using
        End Function

        ' ------------------------------------------------------------------
        ' Search filtering
        ' ------------------------------------------------------------------

        Private Shared Function FilterNode(node As ScanTreeNode, query As String) As ScanTreeNode
            Dim matches = node.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0

            If Not node.IsDirectory Then
                Return If(matches, node, Nothing)
            End If

            ' A directory that matches by name keeps its whole subtree.
            If matches Then Return node

            Dim kids As New List(Of ScanTreeNode)()
            For Each child In node.Children
                Dim f = FilterNode(child, query)
                If f IsNot Nothing Then kids.Add(f)
            Next
            If kids.Count = 0 Then Return Nothing

            Dim result As New ScanTreeNode(node.Name, node.FullPath, True) With {
                .TokenCount = node.TokenCount,
                .FileCount = node.FileCount,
                .FileSize = node.FileSize
            }
            For Each k In kids
                result.Children.Add(k)
            Next
            Return result
        End Function

        Private Shared Function FormatBytes(bytes As Long) As String
            If bytes < 1024 Then Return $"{bytes} B"
            If bytes < 1024 * 1024 Then Return $"{bytes / 1024.0:N1} KB"
            If bytes < 1024L * 1024 * 1024 Then Return $"{bytes / (1024.0 * 1024.0):N1} MB"
            Return $"{bytes / (1024.0 * 1024.0 * 1024.0):N1} GB"
        End Function

    End Class
End Namespace
