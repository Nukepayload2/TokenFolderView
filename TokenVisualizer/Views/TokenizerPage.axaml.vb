Imports System.IO
Imports System.Threading.Tasks
Imports Avalonia
Imports Avalonia.Controls
Imports Avalonia.Layout
Imports Avalonia.Media
Imports Avalonia.Platform.Storage
Imports FluentAvalonia.UI.Controls
Imports Tokenizers
Imports TokenVisualizer.Services

Namespace Views

    Partial Class TokenizerPage
        Inherits UserControl

        Public Sub New()
            InitializeComponent()
        End Sub

        Private Sub TokenizerPage_Loaded() Handles Me.Loaded
            RefreshList()
        End Sub

        ' ------------------------------------------------------------------
        ' List rendering (code-built cards, matching the reference NavView pattern)
        ' ------------------------------------------------------------------

        Private Sub RefreshList()
            Dim registry As TokenizerRegistry = AppState.Registry
            TokenizerList.Items.Clear()

            Dim definitions As List(Of TokenizerSettings) = registry.Definitions
            For i As Integer = 0 To definitions.Count - 1
                TokenizerList.Items.Add(BuildCard(registry, definitions, i))
            Next

            If definitions.Count = 0 Then
                TokenizerList.Items.Add(New TextBlock With {
                    .Text = "还没有分词器。点击右上角「添加分词器…」，选择 tokenizer.json 与 tokenizer_config.json。",
                    .Opacity = 0.5,
                    .TextWrapping = TextWrapping.Wrap,
                    .Margin = New Thickness(8, 16)
                })
            End If
        End Sub

        Private Function BuildCard(registry As TokenizerRegistry,
                                  definitions As List(Of TokenizerSettings),
                                  index As Integer) As Control
            Dim def As TokenizerSettings = definitions(index)
            Dim isActive As Boolean = (index = registry.ActiveIndex)

            ' Left: name + active badge + detail line.
            Dim namePanel As New StackPanel With {.Orientation = Orientation.Horizontal, .Spacing = 8}
            namePanel.Children.Add(New TextBlock With {
                .Text = def.Name,
                .FontSize = 16,
                .FontWeight = FontWeight.Bold
            })
            If isActive Then
                namePanel.Children.Add(BuildActiveBadge())
            End If

            Dim detail As String
            Try
                detail = registry.Describe(index)
            Catch
                detail = "无法读取分词器"
            End Try
            Dim detailText As New TextBlock With {
                .Text = detail,
                .FontSize = 12,
                .Opacity = 0.6,
                .TextWrapping = TextWrapping.Wrap
            }

            Dim leftPanel As New StackPanel With {.Spacing = 4, .VerticalAlignment = VerticalAlignment.Center}
            leftPanel.Children.Add(namePanel)
            leftPanel.Children.Add(detailText)

            ' Right: action buttons.
            Dim btnUse As New Button With {.Content = "使用", .Padding = New Thickness(12, 6)}
            btnUse.Classes.Add("accent")
            btnUse.IsVisible = Not isActive
            AddHandler btnUse.Click, Sub() SetActive(index)

            Dim btnDelete As New Button With {.Content = "删除", .Padding = New Thickness(12, 6)}
            btnDelete.IsEnabled = Not def.IsBundled
            btnDelete.IsVisible = Not def.IsBundled
            AddHandler btnDelete.Click, Sub() Delete(index)

            Dim rightPanel As New StackPanel With {.Orientation = Orientation.Horizontal, .Spacing = 8, .VerticalAlignment = VerticalAlignment.Center}
            rightPanel.Children.Add(btnUse)
            rightPanel.Children.Add(btnDelete)

            Dim grid As New Grid With {.ColumnDefinitions = New ColumnDefinitions("*,Auto")}
            grid.Children.Add(leftPanel)
            Grid.SetColumn(rightPanel, 1)
            grid.Children.Add(rightPanel)

            Return New Border With {
                .Background = FindBrush("SystemControlBackgroundAltHighBrush"),
                .CornerRadius = New CornerRadius(8),
                .Padding = New Thickness(16, 12),
                .Margin = New Thickness(0, 0, 0, 8),
                .Child = grid
            }
        End Function

        Private Function BuildActiveBadge() As Control
            Dim badge As New Border With {
                .Background = New SolidColorBrush(FindAccentColor()),
                .CornerRadius = New CornerRadius(10),
                .Padding = New Thickness(8, 2),
                .VerticalAlignment = VerticalAlignment.Center
            }
            badge.Child = New TextBlock With {
                .Text = "使用中",
                .FontSize = 11,
                .Foreground = Brushes.White,
                .VerticalAlignment = VerticalAlignment.Center
            }
            Return badge
        End Function

        ' ------------------------------------------------------------------
        ' Actions
        ' ------------------------------------------------------------------

        Private Async Sub BtnAdd_Click() Handles BtnAdd.Click
            Dim tl As TopLevel = TopLevel.GetTopLevel(Me)
            If tl Is Nothing Then Return

            Dim tokenizerJson As String = Await PickFileAsync(tl, "选择 tokenizer.json", "*.json")
            If tokenizerJson Is Nothing Then Return

            Dim configJson As String = Await PickFileAsync(tl, "选择 tokenizer_config.json", "*.json")
            If configJson Is Nothing Then
                Await ShowErrorAsync("添加失败", "需要同时选择 tokenizer.json 与 tokenizer_config.json。")
                Return
            End If

            ' Validate the tokenizer.json before registering it.
            Dim validationError As String = Nothing
            Try
                Tokenizer.FromFile(tokenizerJson)
            Catch ex As Exception
                validationError = $"tokenizer.json 加载失败：{ex.Message}"
            End Try
            If validationError IsNot Nothing Then
                Await ShowErrorAsync("无法加载", validationError)
                Return
            End If

            Dim name As String = GetTokenizerName(tokenizerJson)
            Dim registerError As String = Nothing
            Try
                AppState.Registry.Register(name, tokenizerJson, configJson)
            Catch ex As Exception
                registerError = ex.Message
            End Try
            If registerError IsNot Nothing Then
                Await ShowErrorAsync("添加失败", registerError)
                Return
            End If
            RefreshList()
        End Sub

        Private Sub SetActive(index As Integer)
            Try
                AppState.Registry.SetActive(index)
                AppState.ReloadActiveTokenizer()
            Catch
            End Try
            RefreshList()
        End Sub

        Private Sub Delete(index As Integer)
            Try
                AppState.Registry.Remove(index)
                AppState.ReloadActiveTokenizer()
            Catch
            End Try
            RefreshList()
        End Sub

        ' ------------------------------------------------------------------
        ' Helpers
        ' ------------------------------------------------------------------

        Private Async Function PickFileAsync(tl As TopLevel, title As String, pattern As String) As Task(Of String)
            Dim files = Await tl.StorageProvider.OpenFilePickerAsync(
                New FilePickerOpenOptions With {
                    .Title = title,
                    .AllowMultiple = False,
                    .FileTypeFilter = {
                        New FilePickerFileType("JSON 文件") With {.Patterns = {pattern}},
                        FilePickerFileTypes.All
                    }
                })
            If files Is Nothing OrElse files.Count < 1 Then Return Nothing
            Return files(0).Path.LocalPath
        End Function

        Private Shared Function GetTokenizerName(tokenizerJson As String) As String
            Dim dir As String = Path.GetDirectoryName(tokenizerJson)
            Dim folder As String = If(String.IsNullOrEmpty(dir), "", Path.GetFileName(dir))
            If String.IsNullOrEmpty(folder) Then folder = "tokenizer"
            Return folder
        End Function

        Private Async Function ShowErrorAsync(title As String, message As String) As Task
            Try
                Dim dlg As New FATaskDialog With {
                    .Title = title,
                    .Header = message
                }
                dlg.Buttons.Add(New FATaskDialogButton("确定", FATaskDialogStandardResult.OK))
                Await dlg.ShowAsync(False)
            Catch
                ' Error dialogs must never crash the page; fall back silently.
            End Try
        End Function

        Private Shared Function FindBrush(key As String) As IBrush
            Dim o As Object = Nothing
            If Application.Current IsNot Nothing AndAlso Application.Current.TryFindResource(key, Nothing, o) AndAlso TypeOf o Is IBrush Then
                Return DirectCast(o, IBrush)
            End If
            Return New SolidColorBrush(Color.FromRgb(45, 45, 48))
        End Function

        Private Shared Function FindAccentColor() As Color
            Dim o As Object = Nothing
            If Application.Current IsNot Nothing AndAlso Application.Current.TryFindResource("SystemAccentColor", Nothing, o) AndAlso TypeOf o Is Color Then
                Return DirectCast(o, Color)
            End If
            Return Color.FromRgb(0, 120, 212)
        End Function

    End Class
End Namespace
