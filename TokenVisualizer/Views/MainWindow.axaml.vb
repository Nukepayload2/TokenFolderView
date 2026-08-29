Imports Avalonia.Controls
Imports Avalonia.Input
Imports Avalonia.Media
Imports Avalonia.Threading
Imports FluentAvalonia.UI.Controls
Imports TokenVisualizer.Services

Namespace Views
    Partial Class MainWindow
        Inherits Window

        Private _currentPage As Control
        Private _explorerPage As ExplorerPage
        Private _tokenizerPage As TokenizerPage
        Private _settingsPage As SettingsPage
        Private _searchQuery As String = ""
        Private WithEvents _searchTimer As DispatcherTimer
        Private WithEvents _statusTimer As DispatcherTimer
        Private _currentBackdrop As String = "Simple"

        Public Sub New()
            InitializeComponent()

            _searchTimer = New DispatcherTimer With {
                .Interval = TimeSpan.FromMilliseconds(200)
            }
            _statusTimer = New DispatcherTimer With {
                .Interval = TimeSpan.FromMilliseconds(200)
            }
            _statusTimer.Start()
        End Sub

        ' ---- Backdrop (Mica / Acrylic / Simple) ----

        Public Sub ApplyBackdrop()
            ApplyBackdrop(_currentBackdrop)
        End Sub

        Public Sub ApplyBackdrop(backdrop As String)
            _currentBackdrop = backdrop
            Select Case backdrop
                Case "Mica"
                    TransparencyLevelHint = {WindowTransparencyLevel.Mica}
                    Background = Brushes.Transparent
                Case "Acrylic"
                    TransparencyLevelHint = {WindowTransparencyLevel.AcrylicBlur}
                    Background = Brushes.Transparent
                Case Else ' Simple
                    TransparencyLevelHint = {WindowTransparencyLevel.None}
                    Background = Nothing
            End Select
        End Sub

        Private Sub TitleBarBorder_PointerPressed(sender As Object, e As PointerPressedEventArgs) Handles TitleBarBorder.PointerPressed
            If e.GetCurrentPoint(Me).Properties.IsLeftButtonPressed Then
                BeginMoveDrag(e)
            End If
        End Sub

        Private Sub Window_Loaded() Handles Me.Loaded
            BuildNavigationMenu()

            ' Apply the saved backdrop from settings.
            Try
                ApplyBackdrop(SettingsService.Load().BackdropName)
            Catch
            End Try

            ' Preload the active tokenizer in the background so the Explorer page has it.
            Threading.Tasks.Task.Run(Sub() AppState.EnsureActiveTokenizer())
        End Sub

        ' ---- Navigation ----

        Private Sub BuildNavigationMenu()
            Dim exploreItem As New FANavigationViewItem With {
                .Content = "浏览",
                .Tag = "explore",
                .IconSource = New FASymbolIconSource With {.Symbol = FASymbol.OpenFolder}
            }
            Dim tokenizerItem As New FANavigationViewItem With {
                .Content = "分词器",
                .Tag = "tokenizers",
                .IconSource = New FASymbolIconSource With {.Symbol = FASymbol.Setting}
            }
            NavView.MenuItemsSource = {exploreItem, tokenizerItem}
            NavView.SelectedItem = exploreItem
        End Sub

        Private Sub NavView_SelectionChanged(sender As Object, e As FANavigationViewSelectionChangedEventArgs) Handles NavView.SelectionChanged
            If e.IsSettingsSelected Then
                ShowPage("settings")
            Else
                Dim nvi = TryCast(e.SelectedItemContainer, FANavigationViewItem)
                Dim kind As String = Nothing
                If nvi IsNot Nothing Then
                    kind = TryCast(nvi.Tag, String)
                End If
                If kind Is Nothing Then
                    Dim raw = TryCast(e.SelectedItem, FANavigationViewItem)
                    If raw IsNot Nothing Then
                        kind = TryCast(raw.Tag, String)
                    End If
                End If
                ShowPage(kind)
            End If
        End Sub

        Private Sub ShowPage(kind As String)
            Select Case kind
                Case "tokenizers"
                    ShowTokenizerPage()
                Case "settings"
                    ShowSettingsPage()
                Case Else
                    ShowExplorerPage()
            End Select
        End Sub

        Private Sub ShowExplorerPage()
            If _explorerPage Is Nothing Then
                _explorerPage = New ExplorerPage()
            End If
            _currentPage = _explorerPage
            NavView.Content = _explorerPage
            _explorerPage.RefreshStatus()
            _explorerPage.ApplySearchFilter(_searchQuery)
        End Sub

        Private Sub ShowTokenizerPage()
            If _tokenizerPage Is Nothing Then
                _tokenizerPage = New TokenizerPage()
            End If
            _currentPage = _tokenizerPage
            NavView.Content = _tokenizerPage
        End Sub

        Private Sub ShowSettingsPage()
            If _settingsPage Is Nothing Then
                _settingsPage = New SettingsPage()
            End If
            _currentPage = _settingsPage
            NavView.Content = _settingsPage
        End Sub

        ' ---- Global Search (debounced; filters the Explorer tree) ----

        Private Sub SearchBox_TextChanged() Handles SearchBox.TextChanged
            _searchTimer.Stop()
            _searchTimer.Start()
        End Sub

        Private Sub SearchTimer_Tick() Handles _searchTimer.Tick
            _searchTimer.Stop()
            _searchQuery = If(SearchBox.Text, "").Trim()
            _explorerPage?.ApplySearchFilter(_searchQuery)
        End Sub

        ' ---- Status polling (reads the shared AppState) ----

        Private Sub StatusTimer_Tick() Handles _statusTimer.Tick
            UpdateStatus()
        End Sub

        Private Sub UpdateStatus()
            Dim state = AppState.Current
            Dim root = state.RootNode
            Dim progress = If(state.ActiveScan IsNot Nothing, state.ActiveScan.ScanProgress, Nothing)

            If state.IsScanning AndAlso progress IsNot Nothing Then
                TxtTotalTokens.Text = $"总计 {progress.ReadTotalTokens():N0} tokens"
                TxtFilesScanned.Text = $"文件 {progress.ReadFilesScanned():N0}"
                TxtFilesSkipped.Text = $"跳过 {progress.ReadFilesSkipped():N0}"
                TxtScanStatus.Text = "扫描中…"
                ScanProgressBar.IsVisible = True
                ScanProgressBar.IsIndeterminate = True
                BtnCancelScan.IsVisible = True
            ElseIf root IsNot Nothing Then
                TxtTotalTokens.Text = $"总计 {root.TokenCount:N0} tokens"
                TxtFilesScanned.Text = $"文件 {root.FileCount:N0}"
                TxtFilesSkipped.Text = $"跳过 {If(progress IsNot Nothing, progress.ReadFilesSkipped(), 0):N0}"
                TxtScanStatus.Text = "完成"
                ScanProgressBar.IsVisible = False
                BtnCancelScan.IsVisible = False
            Else
                TxtTotalTokens.Text = "总计 0 tokens"
                TxtFilesScanned.Text = "文件 0"
                TxtFilesSkipped.Text = "跳过 0"
                TxtScanStatus.Text = "就绪"
                ScanProgressBar.IsVisible = False
                BtnCancelScan.IsVisible = False
            End If
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
    End Class
End Namespace
