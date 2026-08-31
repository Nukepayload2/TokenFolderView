Imports System.Collections.Generic
Imports System.Threading.Tasks
Imports Avalonia
Imports Avalonia.Controls
Imports Avalonia.Input
Imports Avalonia.Media
Imports Avalonia.Threading
Imports Avalonia.VisualTree
Imports FluentAvalonia.UI.Controls
Imports Tokenizers.Scanning
Imports TokenVisualizer.Services

Namespace Views
    Partial Class MainWindow
        Inherits Window

        Private _currentPage As Control
        Private _explorerPage As ExplorerPage
        Private _textTokenizePage As TextTokenizePage
        Private _tokenizerPage As TokenizerPage
        Private _settingsPage As SettingsPage
        Private _searchQuery As String = ""
        Private WithEvents _searchTimer As DispatcherTimer
        Private WithEvents _statusTimer As DispatcherTimer

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

        Private Sub TitleBarBorder_PointerPressed(sender As Object, e As PointerPressedEventArgs) Handles TitleBarBorder.PointerPressed
            If e.GetCurrentPoint(Me).Properties.IsLeftButtonPressed Then
                BeginMoveDrag(e)
            End If
        End Sub

        Private Sub Window_Loaded() Handles Me.Loaded
            BuildNavigationMenu()

            ' Hide the platform's Fullscreen caption button (kept out of the UI by design).
            ScheduleHideFullScreenButton()

            ' Preload the active tokenizer in the background so the Explorer page has it.
            Task.Run(Sub() AppState.EnsureActiveTokenizer())
        End Sub

        ' ---- Navigation ----

        Private Sub BuildNavigationMenu()
            Dim exploreItem As New FANavigationViewItem With {
                .Content = "浏览",
                .Tag = "explore",
                .IconSource = New FASymbolIconSource With {.Symbol = FASymbol.OpenFolder}
            }
            Dim textTokenizeItem As New FANavigationViewItem With {
                .Content = "文本分词",
                .Tag = "text-tokenize",
                .IconSource = New FASymbolIconSource With {.Symbol = FASymbol.Edit}
            }
            Dim tokenizerItem As New FANavigationViewItem With {
                .Content = "分词器",
                .Tag = "tokenizers",
                .IconSource = New FASymbolIconSource With {.Symbol = FASymbol.Setting}
            }
            NavView.MenuItemsSource = {exploreItem, textTokenizeItem, tokenizerItem}
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
                Case "text-tokenize"
                    ShowTextTokenizePage()
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

        Private Sub ShowTextTokenizePage()
            If _textTokenizePage Is Nothing Then
                _textTokenizePage = New TextTokenizePage()
            End If
            _currentPage = _textTokenizePage
            NavView.Content = _textTokenizePage
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
                TxtScanStatus.Text = "完成" & BuildSpeedSuffix(progress, root)
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

        ''' <summary>
        ''' Appends the last completed scan's load speed (" · 2.1M tokens/s · 84.2 MB/s") to the
        ''' status label. Elapsed time is measured inside the scanner (<see cref="ScanProgress"/>),
        ''' so the figure is exact even though the UI only polls every 200 ms. Returns "" when no
        ''' scan has run or it finished too fast to measure.
        ''' </summary>
        Private Shared Function BuildSpeedSuffix(progress As ScanProgress, root As ScanTreeNode) As String
            If progress Is Nothing Then Return ""
            Dim elapsed As Double = progress.Elapsed.TotalSeconds
            If elapsed <= 0 Then Return ""
            Dim tokensPerSec As Double = root.TokenCount / elapsed
            Dim bytesPerSec As Double = root.FileSize / elapsed
            Return $" · {tokensPerSec:N0} tokens/s · {FormatBytes(bytesPerSec)}/s"
        End Function

        Private Shared Function FormatBytes(bytes As Double) As String
            If bytes < 1024 Then Return $"{bytes:N0} B"
            If bytes < 1024 * 1024 Then Return $"{bytes / 1024.0:N1} KB"
            If bytes < 1024L * 1024 * 1024 Then Return $"{bytes / (1024.0 * 1024.0):N1} MB"
            Return $"{bytes / (1024.0 * 1024.0 * 1024.0):N1} GB"
        End Function

        Private Sub BtnCancelScan_Click() Handles BtnCancelScan.Click
            Dim scan = AppState.Current.ActiveScan
            If scan IsNot Nothing Then
                Try
                    scan.ScanProgress.Cancellation.Cancel()
                Catch
                End Try
            End If
        End Sub

        ''' <summary>
        ''' Hides the Fullscreen caption button that Avalonia 12's native window decorations
        ''' (WindowDrawnDecorations) render alongside Minimize/Maximize/Close. There is no
        ''' SystemDecorations level between "BorderOnly" and "Full", so we hide the button by
        ''' walking the visual tree. The decorations live under the window's VisualRoot (the
        ''' TopLevelHost), not under the Window control itself, so we search there too. The
        ''' decorations template applies after Loaded, so the walk is retried on a short timer
        ''' until the button is found.
        ''' </summary>
        Private WithEvents _fullScreenHideTimer As DispatcherTimer
        Private _fullScreenHideRetries As Integer = 0

        Private Sub ScheduleHideFullScreenButton()
            _fullScreenHideTimer = New DispatcherTimer With {
                .Interval = TimeSpan.FromMilliseconds(250)
            }
            _fullScreenHideTimer.Start()
        End Sub

        Private Sub FullScreenHideTimer_Tick() Handles _fullScreenHideTimer.Tick
            _fullScreenHideRetries += 1
            If HideFullScreenButtons() Then
                _fullScreenHideTimer.Stop()
            ElseIf _fullScreenHideRetries >= 16 Then
                ' Give up after ~4s; the decorations may not have applied on this platform.
                _fullScreenHideTimer.Stop()
            End If
        End Sub

        Private Function HideFullScreenButtons() As Boolean
            Dim found As Boolean = False
            Dim roots As New List(Of Visual)()
            roots.Add(Me)
            If Me.VisualRoot IsNot Nothing Then roots.Add(Me.VisualRoot)
            For Each child As Visual In Me.GetVisualChildren()
                roots.Add(child)
            Next
            For Each rootVisual As Visual In roots
                Dim stack As New Stack(Of Visual)()
                stack.Push(rootVisual)
                While stack.Count > 0
                    Dim v = stack.Pop()
                    Dim b = TryCast(v, Button)
                    If b IsNot Nothing AndAlso IsFullScreenCaptionButton(b) Then
                        b.IsVisible = False
                        found = True
                    End If
                    For Each child As Visual In v.GetVisualChildren()
                        stack.Push(child)
                    Next
                End While
            Next
            Return found
        End Function

        Private Shared Function IsFullScreenCaptionButton(b As Button) As Boolean
            Return String.Equals(b.Name, "PART_FullScreenButton", StringComparison.OrdinalIgnoreCase) OrElse
                   String.Equals(b.Name, "PART_PopoverFullScreenButton", StringComparison.OrdinalIgnoreCase) OrElse
                   String.Equals(b.Name, "Fullscreen", StringComparison.OrdinalIgnoreCase)
        End Function
    End Class
End Namespace
