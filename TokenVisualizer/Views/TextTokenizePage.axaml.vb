Imports System.Collections.Generic
Imports System.Threading
Imports System.Threading.Tasks
Imports Avalonia.Controls
Imports Avalonia.Threading
Imports Tokenizers
Imports TokenVisualizer.Controls
Imports TokenVisualizer.Services

Namespace Views

    Partial Class TextTokenizePage
        Inherits UserControl

        Private WithEvents _analyzeTimer As DispatcherTimer
        Private _runGeneration As Integer
        Private _statsText As String = ""

        ''' <summary>
        ''' Latest tokenization summary ("N 字符 · M tokens"), surfaced by the MainWindow status bar
        ''' while this page is active. Empty when there is nothing to report (no text / no tokenizer /
        ''' an error — those are shown in the view itself).
        ''' </summary>
        Public ReadOnly Property StatusSummary As String
            Get
                Return _statsText
            End Get
        End Property

        Public Sub New()
            InitializeComponent()
            _analyzeTimer = New DispatcherTimer With {
                .Interval = TimeSpan.FromMilliseconds(500)
            }
        End Sub

        Private Async Sub TextTokenizePage_Loaded() Handles Me.Loaded
            Await Task.Run(Sub() AppState.EnsureActiveTokenizer())
            ' Show any text left over from a previous visit immediately.
            RunAnalysisAsync()
        End Sub

        Private Sub TxtInput_TextChanged() Handles TxtInput.TextChanged
            _analyzeTimer.Stop()
            _analyzeTimer.Start()
        End Sub

        Private Sub AnalyzeTimer_Tick() Handles _analyzeTimer.Tick
            _analyzeTimer.Stop()
            RunAnalysisAsync()
        End Sub

        ''' <summary>
        ''' Debounced entry point: encode the current text off the UI thread and show the token-colored
        ''' result. A generation counter drops results from runs superseded by newer input, so fast
        ''' typing can never paint stale output. Content stays visible while a run is in flight (no
        ''' flicker); the scroll position is left alone so reading isn't disturbed.
        ''' </summary>
        Private Async Sub RunAnalysisAsync()
            AppState.EnsureActiveTokenizer()
            Dim tokenizer = AppState.Current.ActiveTokenizer
            If tokenizer Is Nothing Then
                TokenView.ShowText("未找到分词器，请先在「设置」中添加。")
                _statsText = ""
                Return
            End If

            Dim text = TxtInput.Text
            Dim gen = Interlocked.Increment(_runGeneration)
            If String.IsNullOrEmpty(text) Then
                TokenView.ItemsSource = Nothing
                TokenView.IsEmptyVisible = True
                _statsText = ""
                Return
            End If

            Try
                Dim result = Await Task.Run(Function() BuildResult(text, tokenizer))
                If gen <> _runGeneration Then Return ' superseded by newer input
                TokenView.ItemsSource = result.lines
                TokenView.IsEmptyVisible = False
                _statsText = $"{result.charCount:N0} 字符 · {result.tokenCount:N0} tokens"
            Catch ex As Exception
                If gen <> _runGeneration Then Return
                TokenView.ShowText($"分词失败：{ex.Message}")
                _statsText = ""
            End Try
        End Sub

        ''' <summary>
        ''' Heavy work off the UI thread: encode the text to token spans, then build the virtualized
        ''' line list. Only cheap integer structures are built here; the per-line inlines are computed
        ''' lazily on the UI thread by <see cref="TokenLine"/> when a virtualized container is
        ''' materialized.
        ''' </summary>
        Private Shared Function BuildResult(text As String,
                                            tokenizer As Tokenizer) As (charCount As Integer, tokenCount As Integer, lines As List(Of TokenLine))
            Dim spans = tokenizer.EncodeWithSpans(text)
            Dim lines = TokenizedTextView.BuildLines(text, spans)
            Return (text.Length, spans.Count, lines)
        End Function

    End Class

End Namespace
