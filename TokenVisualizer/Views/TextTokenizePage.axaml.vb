Imports System.Collections.Generic
Imports System.Threading.Tasks
Imports Avalonia.Controls
Imports Tokenizers
Imports TokenVisualizer.Controls
Imports TokenVisualizer.Services

Namespace Views

    Partial Class TextTokenizePage
        Inherits UserControl

        Public Sub New()
            InitializeComponent()
        End Sub

        Private Async Sub TextTokenizePage_Loaded() Handles Me.Loaded
            Await Task.Run(Sub() AppState.EnsureActiveTokenizer())
            UpdateTokenizerStatus()
        End Sub

        Private Sub UpdateTokenizerStatus()
            Dim state = AppState.Current
            If state.ActiveTokenizer Is Nothing Then
                TblStats.Text = "未找到 tokenizer.json"
                BtnAnalyze.IsEnabled = False
            Else
                TblStats.Text = $"分词器：{state.ActiveTokenizerName}"
                BtnAnalyze.IsEnabled = True
            End If
        End Sub

        Private Async Sub BtnAnalyze_Click() Handles BtnAnalyze.Click
            AppState.EnsureActiveTokenizer()
            Dim tokenizer = AppState.Current.ActiveTokenizer
            If tokenizer Is Nothing Then
                UpdateTokenizerStatus()
                Return
            End If

            Dim text = TxtInput.Text
            If String.IsNullOrEmpty(text) Then
                TokenView.ItemsSource = Nothing
                TokenView.IsEmptyVisible = True
                TblStats.Text = "请输入文本"
                Return
            End If

            TokenView.IsEmptyVisible = True
            TokenView.ResetScroll()
            Try
                Dim result = Await Task.Run(Function() BuildResult(text, tokenizer))
                TokenView.ItemsSource = result.lines
                TokenView.IsEmptyVisible = False
                TblStats.Text = $"{result.charCount:N0} 字符 · {result.tokenCount:N0} tokens"
            Catch ex As Exception
                TokenView.ShowText($"分词失败：{ex.Message}")
                TblStats.Text = ""
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
