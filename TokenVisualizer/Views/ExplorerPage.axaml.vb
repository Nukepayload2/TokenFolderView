Imports System.Buffers
Imports System.Collections.Concurrent
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
        Private WithEvents _searchTimer As DispatcherTimer

        ' Cap on nodes auto-expanded by a search. A broad query (e.g. a single character) can match a
        ' large share of the tree; TreeView is non-virtualizing, so expanding every ancestor realizes
        ' and measures the whole tree and freezes the UI. The budget bounds that work.
        Private Const MaxExpandedNodes As Integer = 500
        Private _expandBudget As Integer

        Public Sub New()
            InitializeComponent()
            _searchTimer = New DispatcherTimer With {
                .Interval = TimeSpan.FromMilliseconds(200)
            }
            _watchTimer = New DispatcherTimer With {
                .Interval = TimeSpan.FromMilliseconds(WatchDebounceMs)
            }
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

        ''' <summary>
        ''' Refreshes the scan state UI (button enablement, cancel visibility) and re-synchronizes the
        ''' file system watcher with the state.
        ''' </summary>
        Public Sub RefreshStatus()
            SyncWatcher()
            UpdateScanUi()
            UpdateTreeEmptyPlaceholder()
            ArmPendingRefresh()
        End Sub

        ''' <summary>
        ''' Shows the "打开文件夹" empty-state hint in the tree pane until a folder has been loaded
        ''' (RootNode exists) and no scan is currently running. Hides it again while scanning or once
        ''' a folder is on screen.
        ''' </summary>
        Private Sub UpdateTreeEmptyPlaceholder()
            Dim state = AppState.Current
            EmptyState.IsVisible = (Not state.IsScanning) AndAlso state.RootNode Is Nothing
        End Sub

        Private Sub UpdateScanUi()
            Dim scanning = AppState.Current.IsScanning
            BtnOpenFolder.IsEnabled = Not scanning
            BtnRescan.IsEnabled = (Not scanning) AndAlso Not String.IsNullOrEmpty(AppState.Current.CurrentScanPath)
        End Sub

        ' ------------------------------------------------------------------
        ' Open / rescan / cancel
        ' ------------------------------------------------------------------

        Private Async Sub BtnOpenFolder_Click() Handles BtnOpenFolder.Click, BtnOpenFolderEmpty.Click
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

        ''' <summary>
        ''' Number of the current whole-tree scan, bumped in the synchronous part of
        ''' <see cref="StartScanAsync"/>. Only the scan that still carries the current number may write
        ''' shared state (<c>IsScanning</c>, <c>RootNode</c>, the tree) on its way out - a superseded
        ''' scan's teardown would otherwise undo what its successor has already set up.
        ''' </summary>
        Private _scanGeneration As Integer

        Private Async Function StartScanAsync(path As String) As Task
            Dim tokenizer = AppState.Current.ActiveTokenizer
            If tokenizer Is Nothing Then Return

            ' This scan gets a number of its own. Everything below that writes shared state does so
            ' only while `generation` is still the current one: two quick clicks on "重新扫描" leave
            ' the first scan's teardown running after the second one has already set IsScanning = True,
            ' and an unguarded teardown clears the new scan's flag - the debounce tick then no longer
            ' sees a scan in progress, starts a refresh against a tree that is not there yet, and the
            ' batch is dropped with nothing left to re-arm it. The counter is bumped in this
            ' synchronous step, which no continuation can be inserted into.
            _scanGeneration += 1
            Dim generation As Integer = _scanGeneration

            ' Cancel any previous scan.
            If AppState.Current.ActiveScan IsNot Nothing Then
                Try
                    AppState.Current.ActiveScan.ScanProgress.Cancellation.Cancel()
                Catch
                End Try
            End If

            ' A refresh in flight is counting files of the tree this scan is about to replace. Its
            ' batch is worthless either way (it describes the old tree, and this scan counts
            ' everything from scratch), so it is cancelled rather than left to hold _countGate: the
            ' scan below waits for that gate, and letting a doomed batch finish first would stall the
            ' scan for as long as the batch is long.
            CancelRefreshInFlight()

            ' The incremental refresh patches the tree this scan is about to build, so it must keep
            ' using the very same options (blacklist / size / binary) - they are captured here and not
            ' re-read from the settings, or the tree and its filter rules would disagree. The scanner
            ' is constructed from the very same instance, so the counting side and the filtering side
            ' of a later refresh cannot drift apart.
            Dim options = BuildScanOptions()
            _scanOptions = options

            Dim scanner As New FolderScanner(tokenizer, options)
            AppState.Current.ActiveScan = scanner
            AppState.Current.RootNode = Nothing
            AppState.Current.IsScanning = True
            AppState.Current.CurrentScanPath = path
            ' Only a whole-tree scan makes the elapsed time and the tree agree again.
            AppState.Current.IncrementalRefreshed = False

            FileTree.ItemsSource = Nothing
            ClearContentView()
            RefreshStatus()

            Dim ct = scanner.ScanProgress.Cancellation.Token
            Dim gateHeld As Boolean = False
            Try
                ' One counting phase at a time: an incremental refresh that is counting right now
                ' holds the same gate, and letting the two overlap would run EncodeCount while
                ' ScanAsync drops the word caches at its end. The wait is deliberately not
                ' cancellable, so a superseded scan still releases the gate it is holding.
                Await _countGate.WaitAsync()
                gateHeld = True

                ' Enumeration in ScanAsync runs synchronously before its first Await, so run the
                ' whole thing on the thread pool to keep the UI responsive.
                Dim root = Await Task.Run(Function() scanner.ScanAsync(path, ct), ct)

                ' A superseded scan must not publish its tree: a newer scan has already cleared
                ' RootNode and shows its own progress, so writing here would put a stale tree back on
                ' screen (and leave it there, since the new scan clears it only once, up front).
                If generation <> _scanGeneration Then Return
                AppState.Current.RootNode = root
                ApplySearchFilter(_currentQuery)
            Catch ex As OperationCanceledException
                If generation = _scanGeneration Then
                    AppState.Current.RootNode = Nothing
                    FileTree.ItemsSource = Nothing
                End If
            Catch ex As Exception
                If generation = _scanGeneration Then
                    AppState.Current.RootNode = Nothing
                    FileTree.ItemsSource = Nothing
                    TokenLines.ShowText($"扫描失败：{ex.Message}")
                End If
            Finally
                ' The gate is always released, even by a scan that lost its slot: whoever waits on it
                ' next would otherwise wait for ever.
                If gateHeld Then _countGate.Release()
                ' Same guard as above, and the one that keeps two overlapping scans from clearing each
                ' other's state: only the current scan may end "scanning", re-evaluate the watcher and
                ' re-arm the debounce window for whatever arrived while it ran.
                If generation = _scanGeneration Then
                    AppState.Current.IsScanning = False
                    RefreshStatus()
                End If
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

        ' ------------------------------------------------------------------
        ' Incremental refresh (file system watcher)
        '
        ' The contract this section keeps (see the plan's concurrency section):
        '   * watcher callbacks only merge into _pendingChanges. They never look at the state of the
        '     page, so a change that arrives during a scan or during a refresh is never lost;
        '   * the debounce tick is the only place that takes a batch, and it refuses to run a second
        '     refresh while one is running;
        '   * the file system is only read on a pool thread, while _countGate is held: one counting
        '     phase at a time, and never one that overlaps a whole-tree scan;
        '   * the tree is patched on the UI thread, in one synchronous step with no Await in it, and
        '     only with values that were read before - so no callback can observe a half-made patch.
        ' ------------------------------------------------------------------

        ''' <summary>Debounce window: a batch is applied this long after the last reported change.</summary>
        Private Const WatchDebounceMs As Integer = 1000

        ''' <summary>The watcher of the scanned folder; Nothing while auto-refresh is off or idle.</summary>
        Private _watcher As FileSystemWatcher

        ''' <summary>Canonical root <see cref="_watcher"/> watches; event paths are relativized against it.</summary>
        Private _watchRoot As String

        ''' <summary>Re-arms the debounce window; restarted from a callback through the dispatcher.</summary>
        Private WithEvents _watchTimer As DispatcherTimer

        ''' <summary>
        ''' Reported changes waiting for the next refresh - the request queue. Only merged into by the
        ''' watcher callbacks and only drained by <see cref="WatchTimer_Tick"/>; the key is the
        ''' root-relative path, so a burst of events for one file collapses into a single entry.
        ''' </summary>
        ''' <remarks>
        ''' The keys are compared case-sensitively (the default) on purpose: a rename that only changes
        ''' the case of a name ("foo" to "Foo") arrives as one removal and one change of the *same*
        ''' path, and a case-insensitive set would merge the two into the removal alone, making the
        ''' entry vanish from the tree until the next scan. Two entries that differ only in case are
        ''' harmless: both are applied and the tree looks nodes up case-insensitively.
        ''' </remarks>
        Private ReadOnly _pendingChanges As New ConcurrentDictionary(Of String, PathChange)()

        ''' <summary>
        ''' The <see cref="ScanOptions"/> snapshot the current tree was scanned with. A refresh must
        ''' keep using it rather than re-reading the settings, or it would count the tree with filters
        ''' the tree itself does not follow. It is the very same instance that was handed to the
        ''' scanner which built the tree (see <see cref="StartScanAsync"/>), so the files a refresh
        ''' lists and the files the tree was built from are selected by one and the same rule set.
        ''' </summary>
        Private _scanOptions As ScanOptions

        ''' <summary>True while an incremental refresh is running (never two at a time).</summary>
        Private _refreshRunning As Boolean

        ''' <summary>
        ''' Number of the current refresh batch, bumped once per started batch. A batch compares its
        ''' number before it patches: a mismatch means a newer batch is already on its way, and the
        ''' older result describes a tree that is about to be replaced.
        ''' </summary>
        Private _refreshGeneration As Integer

        ''' <summary>Cancels the refresh in flight (the window is closing); Nothing while idle.</summary>
        Private _refreshCts As CancellationTokenSource

        ''' <summary>
        ''' Serializes the counting phases. Both the whole-tree scan and the incremental refresh call
        ''' <c>EncodeCount</c>, and the scan drops the model's word caches when it ends - which must
        ''' never happen while an encode is in flight.
        ''' </summary>
        Private ReadOnly _countGate As New SemaphoreSlim(1, 1)

        ''' <summary>
        ''' Brings the watcher in line with the state: watching the scanned folder while auto-refresh
        ''' is on and a folder is open, stopped and released otherwise. Called from
        ''' <see cref="RefreshStatus"/>, so showing the page and finishing a scan both re-evaluate it.
        ''' A running scan deliberately keeps the watcher alive: a scan only reads the files, and
        ''' stopping the watcher would lose every change made while it ran (events are not replayed).
        ''' </summary>
        Public Sub SyncWatcher()
            Dim path = AppState.Current.CurrentScanPath
            If String.IsNullOrEmpty(path) OrElse Not SettingsService.Load().AutoRefreshOnFileChanges Then
                StopWatching()
                ' Auto-refresh is off (or there is no folder): what was collected before is dropped
                ' rather than flushed later, which would refresh against the user's wish.
                _pendingChanges.Clear()
                Return
            End If

            Dim watchRoot As String
            Try
                ' Qualified: the local path variable shadows System.IO.Path for the compiler.
                watchRoot = System.IO.Path.GetFullPath(path)
            Catch
                StopWatching()
                Return
            End Try
            If _watcher IsNot Nothing AndAlso String.Equals(_watchRoot, watchRoot, StringComparison.OrdinalIgnoreCase) Then Return

            ' The pending set belongs to the folder it was collected in: its paths are relative to that
            ' root, so applying it to another folder's tree would patch - or delete - same-named nodes
            ' of the wrong tree. Switching folders therefore drops it.
            If _watchRoot IsNot Nothing AndAlso Not String.Equals(_watchRoot, watchRoot, StringComparison.OrdinalIgnoreCase) Then
                _pendingChanges.Clear()
            End If

            ' The folder changed (or the watcher was given up on): the old one goes, a fresh one is
            ' created for the current root.
            DisposeWatcher()
            Dim watcher As FileSystemWatcher = Nothing
            Try
                watcher = New FileSystemWatcher(watchRoot) With {
                    .IncludeSubdirectories = True,
                    .NotifyFilter = NotifyFilters.FileName Or NotifyFilters.DirectoryName Or
                                    NotifyFilters.LastWrite Or NotifyFilters.Size,
                    .InternalBufferSize = 64 * 1024
                }
                AddHandler watcher.Created, AddressOf OnWatcherEvent
                AddHandler watcher.Changed, AddressOf OnWatcherEvent
                AddHandler watcher.Deleted, AddressOf OnWatcherEvent
                AddHandler watcher.Renamed, AddressOf OnWatcherRenamed
                AddHandler watcher.Error, AddressOf OnWatcherError
                ' The fields are in place before events can be raised: a callback reads both.
                _watchRoot = watchRoot
                _watcher = watcher
                watcher.EnableRaisingEvents = True
            Catch
                ' The folder cannot be watched (deleted meanwhile, or not accessible): the page keeps
                ' working without auto-refresh until the next scan re-establishes the watcher.
                If watcher IsNot Nothing Then watcher.Dispose()
                StopWatching()
            End Try
        End Sub

        ''' <summary>
        ''' Stops the watcher, releases it, and cancels a refresh that is still in flight. Called when
        ''' auto-refresh is switched off or the folder is closed, and by the window's Closing handler
        ''' so that nothing keeps running once the UI is gone.
        ''' </summary>
        Public Sub StopWatching()
            _watchTimer.Stop()
            DisposeWatcher()
            CancelRefreshInFlight()
        End Sub

        ''' <summary>
        ''' Cancels the refresh in flight, if there is one, so that it gives up its batch. The batch is
        ''' dropped rather than resumed (its counting loop observes the token and bails out): it was
        ''' collected for a tree that is on its way out, and the rescan that cancels it recomputes
        ''' everything it could have contained.
        ''' </summary>
        Private Sub CancelRefreshInFlight()
            Dim cts = _refreshCts
            If cts IsNot Nothing Then
                Try
                    cts.Cancel()
                Catch
                End Try
            End If
        End Sub

        Private Sub DisposeWatcher()
            Dim watcher = _watcher
            _watcher = Nothing
            _watchRoot = Nothing
            If watcher IsNot Nothing Then watcher.Dispose()
        End Sub

        ''' <summary>
        ''' Watcher callback (pool thread). It does three things - relativize the path, drop
        ''' blacklisted noise, merge the change into the pending set - and looks at no other state of
        ''' the page: whatever is reported while a scan or a refresh runs is recorded either way.
        ''' </summary>
        Private Sub OnWatcherEvent(sender As Object, e As FileSystemEventArgs)
            Try
                ' A callback of a watcher that has been replaced or released (the folder was closed,
                ' auto-refresh was switched off) must not be attributed to the current one.
                If Not ReferenceEquals(sender, _watcher) Then Return
                Dim watchRoot = _watchRoot
                Dim options = _scanOptions
                If watchRoot Is Nothing OrElse options Is Nothing Then Return

                Dim relativePath As String = TryGetRelativePath(watchRoot, e.FullPath)
                If relativePath Is Nothing Then Return

                Select Case e.ChangeType
                    Case WatcherChangeTypes.Created
                        Dim isDirectory As Boolean = Directory.Exists(e.FullPath)
                        If ShouldSkipEvent(relativePath, isDirectory, options) Then Return
                        QueueChange(relativePath, isDirectory, isRemoval:=False)
                    Case WatcherChangeTypes.Changed
                        ' A directory reports a write change whenever a child file is created or
                        ' deleted in it, so a directory that reports Changed is *not* a directory
                        ' change: rescanning the parent subtree for every file edit would rebuild the
                        ' node of the very file that was edited and drop the selection with it. Only a
                        ' directory that appears (Created / Renamed) is a directory change.
                        If ShouldSkipEvent(relativePath, False, options) Then Return
                        If Directory.Exists(e.FullPath) Then Return
                        QueueChange(relativePath, isDirectory:=False, isRemoval:=False)
                    Case WatcherChangeTypes.Deleted
                        ' A deleted path is gone, so its kind cannot be determined any more.
                        ' Pruning a removal too eagerly would strand a node forever, so only the
                        ' parent segments are checked - a path below a blacklisted folder was never
                        ' part of the tree, and removing an unknown path is a no-op anyway.
                        If ScanFilter.ShouldSkipFile(relativePath, 0L, options) Then Return
                        QueueChange(relativePath, isDirectory:=False, isRemoval:=True)
                    Case WatcherChangeTypes.Renamed
                        Dim renamed = TryCast(e, RenamedEventArgs)
                        If renamed Is Nothing Then Return
                        QueueRename(renamed, watchRoot, options)
                    Case Else
                        Return
                End Select

                RequestRefresh()
            Catch
                ' A watcher callback runs on a pool thread, where a stray exception would take the
                ' process down. The change is dropped instead: the next event for that path, or a
                ' manual rescan, reports it again.
            End Try
        End Sub

        ''' <summary>
        ''' <see cref="FileSystemWatcher.Renamed"/> has a handler type of its own and shares the body
        ''' of the other change notifications.
        ''' </summary>
        Private Sub OnWatcherRenamed(sender As Object, e As RenamedEventArgs)
            OnWatcherEvent(sender, e)
        End Sub

        ''' <summary>
        ''' The watcher lost events (its internal buffer overflowed), so the pending set cannot be
        ''' trusted to describe the folder any more: it is discarded and the tree is rebuilt from
        ''' scratch, with a fresh watcher (a new one has a new buffer).
        ''' </summary>
        Private Sub OnWatcherError(sender As Object, e As ErrorEventArgs)
            If Not ReferenceEquals(sender, _watcher) Then Return
            PostToUi(Sub()
                         DisposeWatcher()
                         FallbackFullRescan()
                     End Sub)
        End Sub

        ''' <summary>
        ''' The <c>/</c>-separated root-relative form of a watcher path, or Nothing for anything that
        ''' is not inside the root (which the watcher does not report, but nothing outside the root
        ''' may ever reach the tree).
        ''' </summary>
        Private Shared Function TryGetRelativePath(watchRoot As String, fullPath As String) As String
            Dim relative As String
            Try
                relative = Path.GetRelativePath(watchRoot, fullPath)
            Catch
                Return Nothing
            End Try
            If String.IsNullOrEmpty(relative) Then Return Nothing

            relative = relative.Replace("\"c, "/"c)
            If relative.StartsWith("/", StringComparison.Ordinal) Then Return Nothing
            If relative.Length >= 2 AndAlso relative(1) = ":"c Then Return Nothing
            If relative = "." OrElse relative = ".." Then Return Nothing
            If relative.StartsWith("../", StringComparison.Ordinal) Then Return Nothing
            Return relative
        End Function

        ''' <summary>
        ''' Blacklist pruning of one reported change. A directory is pruned when any of its segments is
        ''' blacklisted (its own name is a folder name); a file only when one of its *parent* segments
        ''' is - a file is never skipped because of its own name.
        ''' </summary>
        Private Shared Function ShouldSkipEvent(relativePath As String, isDirectory As Boolean, options As ScanOptions) As Boolean
            If isDirectory Then Return ScanFilter.ShouldSkipPath(relativePath, options.FolderBlacklist)
            ' A length of 0 never trips the size check, so this is purely the parent-segment test.
            Return ScanFilter.ShouldSkipFile(relativePath, 0L, options)
        End Function

        ''' <summary>
        ''' Merges one change into the pending set: the newest report about a path wins. A file that
        ''' was deleted and created again is a change rather than a removal, and a rename is one
        ''' removal plus one change, which is exactly what the editor expects.
        ''' </summary>
        Private Sub QueueChange(relativePath As String, isDirectory As Boolean, isRemoval As Boolean)
            _pendingChanges(relativePath) = New PathChange(relativePath, isDirectory, isRemoval)
        End Sub

        ''' <summary>
        ''' Queues a rename as the two changes it is. The kind of the entry is taken from the new path
        ''' (the old one is gone already) and used to prune both sides.
        ''' </summary>
        Private Sub QueueRename(e As RenamedEventArgs, watchRoot As String, options As ScanOptions)
            Dim isDirectory As Boolean = Directory.Exists(e.FullPath)

            Dim newRelative As String = TryGetRelativePath(watchRoot, e.FullPath)
            If newRelative IsNot Nothing AndAlso Not ShouldSkipEvent(newRelative, isDirectory, options) Then
                QueueChange(newRelative, isDirectory, isRemoval:=False)
            End If

            Dim oldRelative As String = TryGetRelativePath(watchRoot, e.OldFullPath)
            If oldRelative IsNot Nothing AndAlso Not ShouldSkipEvent(oldRelative, isDirectory, options) Then
                QueueChange(oldRelative, isDirectory, isRemoval:=True)
            End If
        End Sub

        ''' <summary>
        ''' Restarts the debounce window on the UI thread, so that the batch is applied one interval
        ''' after the *last* reported change rather than one interval after the first.
        ''' </summary>
        Private Sub RequestRefresh()
            PostToUi(Sub() RestartWatchTimer())
        End Sub

        Private Sub RestartWatchTimer()
            _watchTimer.Stop()
            _watchTimer.Start()
        End Sub

        ''' <summary>Runs an action on the UI thread, tolerating a callback that arrives while the app shuts down.</summary>
        Private Shared Sub PostToUi(action As Action)
            Try
                Dispatcher.UIThread.Post(action)
            Catch
                ' The dispatcher is gone: no refresh is wanted any more.
            End Try
        End Sub

        ''' <summary>
        ''' Debounce tick (UI thread): applies everything reported so far. A tick during a whole-tree
        ''' scan does nothing at all - the scan's teardown flushes the pending set, since a scan must
        ''' not be interleaved with a patch of the tree it is about to replace - and a tick during a
        ''' refresh only re-arms the timer, so no second refresh starts and the next batch never starts
        ''' earlier than one interval after the last event of this one.
        ''' </summary>
        Private Async Sub WatchTimer_Tick() Handles _watchTimer.Tick
            _watchTimer.Stop()

            ' A tick during a whole-tree scan does nothing at all - and deliberately does not re-arm
            ' the timer: the scan's teardown flushes the pending set through ArmPendingRefresh, which
            ' is also what makes the batch meet the tree that scan has just built. Re-arming here
            ' would only spin the timer for as long as the scan runs.
            If AppState.Current.IsScanning Then Return

            ' A tick during a refresh must not start a second one (one counting phase at a time), but
            ' the debounce window has to be re-armed so that the next batch never starts earlier than
            ' one interval after the last event of this one - the running refresh's own duration must
            ' not shrink the window. Only a non-empty set is re-armed; a later event re-arms the timer
            ' from the watcher callback anyway.
            If _refreshRunning Then
                If Not _pendingChanges.IsEmpty Then _watchTimer.Start()
                Return
            End If

            ' The batch is taken one key at a time. Copying the whole set and clearing it afterwards
            ' would drop every change that lands between the two steps; TryRemove takes exactly what
            ' this loop has seen, and a later change of the same path is queued again for the next
            ' batch instead (nothing is lost, nothing is applied twice).
            Dim batch As New List(Of PathChange)()
            For Each key As String In _pendingChanges.Keys
                Dim change As PathChange = Nothing
                If _pendingChanges.TryRemove(key, change) Then batch.Add(change)
            Next
            If batch.Count = 0 Then Return

            Await RunRefreshAsync(batch)
        End Sub

        ''' <summary>
        ''' Schedules the refresh of what was reported while something else was running. The debounce
        ''' tick does nothing during a scan (a scan leaves the watcher running so that no change is
        ''' lost), and this is where that set meets the tree the scan has just built.
        ''' </summary>
        Private Sub ArmPendingRefresh()
            If AppState.Current.IsScanning OrElse _refreshRunning OrElse _pendingChanges.IsEmpty Then Return
            RestartWatchTimer()
        End Sub

        ''' <summary>
        ''' Applies one batch of reported changes. The file system is read first, on a pool thread with
        ''' <see cref="_countGate"/> held; the tree is then patched on the UI thread in one synchronous
        ''' step whose only inputs are the values read before (no I/O, no Await).
        ''' </summary>
        ''' <param name="batch">The changes taken out of <see cref="_pendingChanges"/> for this run.</param>
        Private Async Function RunRefreshAsync(batch As IReadOnlyList(Of PathChange)) As Task
            Dim root = AppState.Current.RootNode
            Dim options = _scanOptions
            ' The scanner that built this tree is the one to count with, not a fresh one built from
            ' AppState.Current.ActiveTokenizer. Switching the active tokenizer in the settings does not
            ' rescan (and does not even touch the watcher), so a scanner built "now" can carry a
            ' different tokenizer than the tree: the patch would fold a second tokenizer's counts into
            ' the first one's tree, and the grand total - this app's headline number - would silently
            ' become a mix of two definitions. ActiveScan is assigned in the synchronous part of the
            ' scan that produced RootNode, so a non-null RootNode implies a scanner that matches it,
            ' and it carries the tokenizer and the options snapshot the tree was built with.
            Dim scanner = AppState.Current.ActiveScan
            If root Is Nothing OrElse scanner Is Nothing OrElse options Is Nothing Then Return

            ' The batch is numbered like the scans are: a batch that is superseded while it counts
            ' must not patch the tree its replacement has already put in place. Bumped before the
            ' first Await, so no continuation can slip in between the check and the bump.
            _refreshGeneration += 1
            Dim refreshGeneration As Integer = _refreshGeneration

            _refreshRunning = True
            Dim cts As New CancellationTokenSource()
            _refreshCts = cts
            Dim ct As CancellationToken = cts.Token
            Try
                ' ---- Counting phase: pool thread, one at a time. ----
                Dim data As New RefreshBatch()
                Dim gateHeld As Boolean = False
                Try
                    Await _countGate.WaitAsync(ct)
                    gateHeld = True
                    ct.ThrowIfCancellationRequested()

                    ' Taking the gate can mean waiting for a whole-tree scan to finish, and that scan
                    ' has replaced the tree: re-checking the guards here saves counting a batch that
                    ' is going to be thrown away below anyway.
                    If Not IsBatchStillValid(root, refreshGeneration) Then Return

                    ' The scanner and the options are passed by value into the counting body on
                    ' purpose: both were read before the wait, and reading them again inside the
                    ' lambda could pick up the state of a scan that started meanwhile.
                    Dim rootPath As String = root.FullPath
                    Await Task.Run(Sub() BuildRefreshData(rootPath, batch, options, scanner, ct, data), ct)
                Catch ex As OperationCanceledException
                    ' Superseded (the window is closing, or a scan took over): the batch describes a
                    ' tree that is on its way out, so it is dropped rather than applied.
                    Return
                Catch ex As Exception
                    ' Nothing has been written to the tree yet, so giving the batch up leaves a
                    ' consistent (if stale) tree. A whole-tree rescan is ruled out here: it is meant
                    ' for lost watcher events, and it would throw the tree away when the failure is a
                    ' persistent one (a folder that cannot be listed at all).
                    Return
                Finally
                    If gateHeld Then _countGate.Release()
                End Try

                ' ---- Patch phase: UI thread from here on, and no Await before the tree is patched. ----
                ' The three guards again, at the last moment before the tree is touched. Counting a
                ' whole batch gives a scan all the time it needs to start, so the batch is re-checked
                ' rather than trusted: the values it holds belong to a tree that may no longer exist.
                If Not IsBatchStillValid(root, refreshGeneration) Then Return

                Try
                    ScanTreeEditor.ApplyChanges(root, data.Applied, AddressOf data.LookupListing, AddressOf data.LookupCount)
                Catch ex As Exception
                    ' A patch is not atomic, so a tree that failed half-way is only repaired by a full
                    ' rescan. The rescan is posted: this run owns the refresh slot until it returns.
                    PostToUi(Sub() FallbackFullRescan())
                    Return
                End Try

                ' The tree no longer matches the elapsed time of the last whole-tree scan, so the
                ' status bar must not derive a speed from the two any more.
                AppState.Current.IncrementalRefreshed = True

                ' A filtered view is a rebuilt copy of the tree and has to be recomputed. The plain
                ' tree is left untouched: the ObservableCollection notifications add and remove
                ' exactly the affected nodes, so the expanded nodes and the selection survive
                ' (assigning ItemsSource again would reset all of them).
                If Not String.IsNullOrEmpty(_currentQuery) Then ApplySearchFilter(_currentQuery)
            Finally
                _refreshRunning = False
                _refreshCts = Nothing
                cts.Dispose()
                ' Changes reported while this batch ran are waiting: they get their own debounce
                ' window, which is what keeps a steady stream of events down to one batch at a time.
                If Not _pendingChanges.IsEmpty AndAlso Not AppState.Current.IsScanning Then
                    RestartWatchTimer()
                End If
            End Try
        End Function

        ''' <summary>
        ''' The three guards a refresh batch has to pass before it may patch the tree: it is still the
        ''' current batch, the tree is still the one it was counted against, and no whole-tree scan has
        ''' taken over. A batch that fails any of them describes a tree that has been (or is being)
        ''' replaced, so the whole batch is given up rather than written.
        ''' </summary>
        ''' <remarks>
        ''' Checking all three is not redundant. <c>RootNode = Nothing</c> and <c>IsScanning = True</c>
        ''' are set in one synchronous step of <see cref="StartScanAsync"/>, so either test catches a
        ''' scan that has started - but a scan that was superseded and one that was started again can
        ''' leave a *different* tree behind, and only the generation tells one batch from another.
        ''' Both call sites are on the UI thread: the first one right after the gate has been taken
        ''' (its continuation is posted back to the UI thread), the second one after the counting
        ''' phase has returned there.
        ''' </remarks>
        Private Function IsBatchStillValid(root As ScanTreeNode, refreshGeneration As Integer) As Boolean
            Dim current = AppState.Current
            Return refreshGeneration = _refreshGeneration AndAlso
                   ReferenceEquals(current.RootNode, root) AndAlso
                   Not current.IsScanning
        End Function

        ''' <summary>
        ''' Counting phase of a refresh: everything the batch implies is read here, on a pool thread,
        ''' and the results are stored so that the delegates the patch runs never do I/O. An entry that
        ''' cannot be prepared completely is left out of <see cref="RefreshBatch.Applied"/> instead of
        ''' being applied half-way.
        ''' </summary>
        ''' <remarks>
        ''' Leaving a directory entry out is deliberate. The editor rebuilds a directory subtree from
        ''' the listing it is given, so a partial (or missing) listing would silently delete
        ''' everything the listing did not mention - and the aggregates would look plausible afterwards.
        ''' The entry is dropped rather than turned into a full rescan because a rescan is reserved for
        ''' lost watcher events (see the plan) and would lose the whole tree when a listing keeps
        ''' failing.
        ''' </remarks>
        Private Shared Sub BuildRefreshData(rootPath As String,
                                           batch As IReadOnlyList(Of PathChange),
                                           options As ScanOptions,
                                           scanner As FolderScanner,
                                           ct As CancellationToken,
                                           data As RefreshBatch)
            For Each change As PathChange In batch
                ct.ThrowIfCancellationRequested()
                If change Is Nothing Then Continue For

                If change.IsRemoval Then
                    ' Nothing to read: the editor drops the node by its path.
                    data.Applied.Add(change)
                    Continue For
                End If

                Dim relativePath As String = change.RelativePath
                If change.IsDirectory Then
                    If Not Directory.Exists(CombinePath(rootPath, relativePath)) Then
                        ' Created and deleted again inside one debounce window: there is nothing to
                        ' list, and creating an empty directory node for it would leave a ghost.
                        Continue For
                    End If

                    Dim files As IReadOnlyList(Of String) = Nothing
                    Try
                        files = EnumerateSubtreeFiles(rootPath, relativePath, options)
                    Catch ex As Exception
                        ' "The complete list or an exception" is the delegate's contract: a directory
                        ' whose files cannot all be listed must not reach the editor at all.
                        Continue For
                    End Try

                    data.Listings(relativePath) = files
                    For Each file As String In files
                        ct.ThrowIfCancellationRequested()
                        data.Counts(file) = CountBatchFile(scanner, CombinePath(rootPath, file))
                    Next
                Else
                    data.Counts(relativePath) = CountBatchFile(scanner, CombinePath(rootPath, relativePath))
                End If

                data.Applied.Add(change)
            Next
        End Sub

        ''' <summary>
        ''' Counts one file of a batch: a path that is gone reports Missing, anything that cannot even
        ''' be inspected reports Error, which keeps the node the tree already has.
        ''' </summary>
        Private Shared Function CountBatchFile(scanner As FolderScanner, fullPath As String) As FileCountResult
            Try
                Dim fi As New FileInfo(fullPath)
                If Not fi.Exists Then Return FileCountResult.Missing
                Return scanner.CountFile(fullPath, fi.Length)
            Catch ex As Exception
                Return FileCountResult.Error
            End Try
        End Function

        ''' <summary>
        ''' Lists the <c>/</c>-separated root-relative paths of every file in a directory subtree,
        ''' skipping blacklisted folder names exactly like the whole-tree scan does. The list is
        ''' complete, or the method throws - a partial answer is never returned.
        ''' </summary>
        Private Shared Function EnumerateSubtreeFiles(rootPath As String,
                                                     relativeDirPath As String,
                                                     options As ScanOptions) As IReadOnlyList(Of String)
            Dim files As New List(Of String)()
            CollectSubtreeFiles(rootPath, relativeDirPath, options, files)
            Return files
        End Function

        Private Shared Sub CollectSubtreeFiles(rootPath As String,
                                               relativeDirPath As String,
                                               options As ScanOptions,
                                               files As List(Of String))
            Dim fullPath As String = CombinePath(rootPath, relativeDirPath)
            For Each subDirectory As String In Directory.EnumerateDirectories(fullPath)
                Dim name As String = Path.GetFileName(subDirectory)
                If ScanFilter.ShouldSkipFolder(name, options.FolderBlacklist) Then Continue For
                CollectSubtreeFiles(rootPath, If(relativeDirPath.Length = 0, name, relativeDirPath & "/" & name), options, files)
            Next
            For Each filePath As String In Directory.EnumerateFiles(fullPath)
                Dim name As String = Path.GetFileName(filePath)
                files.Add(If(relativeDirPath.Length = 0, name, relativeDirPath & "/" & name))
            Next
        End Sub

        ''' <summary>Full path of a <c>/</c>-separated root-relative path.</summary>
        Private Shared Function CombinePath(rootPath As String, relativePath As String) As String
            Return Path.Combine(rootPath, relativePath.Replace("/"c, Path.DirectorySeparatorChar))
        End Function

        ''' <summary>
        ''' Rebuilds the tree from scratch because the incremental path can no longer be trusted (the
        ''' watcher lost events, or a patch failed half-way). The pending set is superseded by the
        ''' rescan and is discarded.
        ''' </summary>
        Private Async Sub FallbackFullRescan()
            Dim path = AppState.Current.CurrentScanPath
            If String.IsNullOrEmpty(path) Then Return
            _pendingChanges.Clear()
            Await StartScanAsync(path)
        End Sub

        ''' <summary>
        ''' What one refresh batch needs to patch the tree: the counted files, the listed directory
        ''' subtrees, and the entries that could be prepared completely. It is filled on a pool thread
        ''' and read on the UI thread by the delegates given to
        ''' <see cref="ScanTreeEditor.ApplyChanges"/>, which is why they never do I/O of their own.
        ''' </summary>
        Private NotInheritable Class RefreshBatch

            ''' <summary>Counted files, by <c>/</c>-separated root-relative path.</summary>
            Public ReadOnly Counts As New Dictionary(Of String, FileCountResult)(StringComparer.OrdinalIgnoreCase)

            ''' <summary>Listed files of every rescanned directory, by root-relative directory path.</summary>
            Public ReadOnly Listings As New Dictionary(Of String, IReadOnlyList(Of String))(StringComparer.OrdinalIgnoreCase)

            ''' <summary>The batch entries this run can apply.</summary>
            Public ReadOnly Applied As New List(Of PathChange)()

            ''' <summary>Counter delegate: a lookup, never a read of the file system.</summary>
            Public Function LookupCount(relativePath As String) As FileCountResult
                Dim result As FileCountResult = Nothing
                If Counts.TryGetValue(relativePath, result) Then Return result
                ' Unreachable by construction (every path of the batch is counted above). Keeping the
                ' node is the safe answer for a path that slipped through anyway.
                Return FileCountResult.Error
            End Function

            ''' <summary>Enumerator delegate: a lookup, never a read of the file system.</summary>
            Public Function LookupListing(relativeDirPath As String) As IEnumerable(Of String)
                Dim files As IReadOnlyList(Of String) = Nothing
                If Listings.TryGetValue(relativeDirPath, files) Then Return files
                Return Array.Empty(Of String)()
            End Function

        End Class

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
        ' Search debounce (search box lives on this page now)
        ' ------------------------------------------------------------------

        Private Sub SearchBox_TextChanged() Handles SearchBox.TextChanged
            _searchTimer.Stop()
            _searchTimer.Start()
        End Sub

        Private Sub SearchTimer_Tick() Handles _searchTimer.Tick
            _searchTimer.Stop()
            ApplySearchFilter(If(SearchBox.Text, "").Trim())
        End Sub

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
