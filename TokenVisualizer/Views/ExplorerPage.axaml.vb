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

        ' Cap on nodes auto-expanded by a search. A broad query (e.g. a single character) can match a
        ' large share of the tree; TreeView is non-virtualizing, so expanding every ancestor realizes
        ' and measures the whole tree and freezes the UI. The budget bounds that work.
        Private Const MaxExpandedNodes As Integer = 500
        Private _expandBudget As Integer

        Public Sub New()
            InitializeComponent()
        End Sub

        ' ------------------------------------------------------------------
        ' Lifetime / tokenizer bootstrap
        ' ------------------------------------------------------------------

        Private Async Sub ExplorerPage_Loaded() Handles Me.Loaded
            Await Task.Run(Sub() AppState.EnsureActiveTokenizer())
            RefreshStatus()
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
                ' No search: show the whole tree but only expand the root node.
                FileTree.ItemsSource = {root}
                ExpandRootOnly()
            Else
                Dim filtered = FilterNode(root, _currentQuery)
                If filtered Is Nothing Then
                    FileTree.ItemsSource = Nothing
                Else
                    FileTree.ItemsSource = {filtered}
                    ' Every node in the filtered tree is either a match or an ancestor of one;
                    ' expanding them all reveals every match.
                    ExpandToMatches()
                End If
            End If
        End Sub

        ''' <summary>Refreshes the scan state UI (button enablement, cancel visibility).</summary>
        Public Sub RefreshStatus()
            UpdateScanUi()
        End Sub

        Private Sub UpdateScanUi()
            Dim scanning = AppState.Current.IsScanning
            BtnOpenFolder.IsEnabled = Not scanning
            BtnRescan.IsEnabled = (Not scanning) AndAlso Not String.IsNullOrEmpty(AppState.Current.CurrentScanPath)
        End Sub

        ' ------------------------------------------------------------------
        ' Open / rescan / cancel
        ' ------------------------------------------------------------------

        Private Async Sub BtnOpenFolder_Click() Handles BtnOpenFolder.Click
            AppState.EnsureActiveTokenizer()
            If AppState.Current.ActiveTokenizer Is Nothing Then
                TokenLines.ShowText("未找到分词器，请先在「设置」中添加。")
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
                TokenLines.ShowText("未找到分词器，请先在「设置」中添加。")
                Return
            End If
            Await StartScanAsync(path)
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
            Catch ex As OperationCanceledException
                AppState.Current.RootNode = Nothing
                FileTree.ItemsSource = Nothing
            Catch ex As Exception
                AppState.Current.RootNode = Nothing
                FileTree.ItemsSource = Nothing
                TokenLines.ShowText($"扫描失败：{ex.Message}")
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

        Private Sub ExpandToMatches()
            _expandBudget = MaxExpandedNodes
            Dispatcher.UIThread.Post(Sub() ExpandContainers(FileTree), DispatcherPriority.Loaded)
        End Sub

        ''' <summary>
        ''' Expands only the root container, leaving the rest of the tree collapsed. Used when the
        ''' search box is empty. The container is realized after the layout pass that follows the
        ''' ItemsSource assignment, so the expansion is posted at Loaded priority.
        ''' </summary>
        Private Sub ExpandRootOnly()
            Dispatcher.UIThread.Post(Sub()
                Dim c = TryCast(FileTree.ContainerFromIndex(0), TreeViewItem)
                If c IsNot Nothing Then c.IsExpanded = True
            End Sub, DispatcherPriority.Loaded)
        End Sub

        Private Sub ExpandContainers(tree As TreeView)
            For i As Integer = 0 To tree.ItemCount - 1
                Dim c = TryCast(tree.ContainerFromIndex(i), TreeViewItem)
                If c IsNot Nothing Then ExpandItem(c)
            Next
        End Sub

        Private Sub ExpandItem(item As TreeViewItem)
            Dim node = TryCast(item.DataContext, ScanTreeNode)
            If node Is Nothing Then Return

            ' A node whose name matches the query is itself a result: its parent is already expanded
            ' so it is visible, but it must not be expanded — otherwise a matched folder dumps its
            ' whole subtree into the results.
            If IsMatch(node) Then Return

            ' Bound the total work; stop auto-expanding once the budget is spent.
            If _expandBudget <= 0 Then Return
            _expandBudget -= 1

            item.IsExpanded = True
            ' Child containers are realized only after the next layout pass, which runs at Render
            ' priority before the next Loaded job (MediaContext schedules the render at Render).
            ' Post each level to Loaded so a layout pass happens in between; a synchronous
            ' recursion here would find no child containers and expand only the first level.
            Dispatcher.UIThread.Post(Sub()
                For i As Integer = 0 To item.ItemCount - 1
                    Dim c = TryCast(item.ContainerFromIndex(i), TreeViewItem)
                    If c IsNot Nothing Then ExpandItem(c)
                Next
            End Sub, DispatcherPriority.Loaded)
        End Sub

        Private Function IsMatch(node As ScanTreeNode) As Boolean
            Return node.Name.IndexOf(_currentQuery, StringComparison.OrdinalIgnoreCase) >= 0
        End Function

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
            TokenLines.IsEmptyVisible = True
        End Sub

        Private Async Sub LoadFileAsync(node As ScanTreeNode)
            Dim gen = Interlocked.Increment(_loadGeneration)
            Dim tokenizer = AppState.Current.ActiveTokenizer
            If tokenizer Is Nothing Then Return

            TokenLines.IsEmptyVisible = True
            TokenLines.ItemsSource = Nothing
            TokenLines.ResetScroll()

            Try
                Dim lines = Await Task.Run(Function() BuildLines(node.FullPath, tokenizer))
                If gen <> _loadGeneration Then Return ' stale load, superseded by a newer selection

                TokenLines.ItemsSource = lines
                TokenLines.IsEmptyVisible = False
            Catch ex As OperationCanceledException
            Catch ex As Exception
                If gen <> _loadGeneration Then Return
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
                                           tokenizer As Tokenizer) As List(Of TokenLine)
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
                Return TokenizedTextView.BuildLines(text, spans)
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

    End Class
End Namespace
