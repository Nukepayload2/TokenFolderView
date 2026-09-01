Imports System.IO
Imports System.Collections.Generic
Imports System.Threading.Tasks
Imports Avalonia
Imports Avalonia.Controls
Imports Avalonia.Interactivity
Imports Avalonia.Platform.Storage
Imports FluentAvalonia.UI.Controls
Imports Tokenizers
Imports TokenVisualizer.Services

Namespace Controls

    ''' <summary>
    ''' Inline tokenizer-management section for the settings page: lists the registered tokenizers
    ''' with activate/delete actions and an add flow that imports tokenizer.json + tokenizer_config.json.
    ''' Card buttons are handled through a single bubbled Click handler using each card's
    ''' <see cref="TokenizerListItem"/> as the DataContext.
    ''' </summary>
    Partial Public Class TokenizerSettingsSection
        Inherits UserControl

        Public Sub New()
            InitializeComponent()
            TokenizerList.AddHandler(Button.ClickEvent, New EventHandler(Of RoutedEventArgs)(AddressOf OnCardButtonClick))
        End Sub

        Private Sub TokenizerSettingsSection_Loaded() Handles Me.Loaded
            RefreshList()
        End Sub

        ''' <summary>Rebuilds the tokenizer list from the registry.</summary>
        Private Sub RefreshList()
            Dim registry As TokenizerRegistry = AppState.Registry
            Dim definitions As List(Of TokenizerSettings) = registry.Definitions

            Dim items As New List(Of TokenizerListItem)()
            For i As Integer = 0 To definitions.Count - 1
                Dim detail As String
                Try
                    detail = registry.Describe(i)
                Catch
                    detail = "无法读取分词器"
                End Try
                items.Add(New TokenizerListItem With {
                    .Name = definitions(i).Name,
                    .Detail = detail,
                    .IsActive = (i = registry.ActiveIndex),
                    .IsBundled = definitions(i).IsBundled,
                    .Index = i
                })
            Next
            TokenizerList.ItemsSource = items
            TblEmpty.IsVisible = (definitions.Count = 0)
        End Sub

        ''' <summary>
        ''' Handles the card action buttons. The Click routed event bubbles up from each Button inside
        ''' the item DataTemplate; the sender is the Button and its DataContext is the backing row.
        ''' </summary>
        Private Sub OnCardButtonClick(sender As Object, e As RoutedEventArgs)
            Dim btn As Button = DirectCast(sender, Button)
            Dim item = TryCast(btn.DataContext, TokenizerListItem)
            If item Is Nothing Then Return
            If btn.Content?.ToString() = "使用" Then
                SetActive(item.Index)
            Else
                Delete(item.Index)
            End If
        End Sub

        ''' <summary>Marks the tokenizer at <paramref name="index"/> as active and reloads it.</summary>
        Private Sub SetActive(index As Integer)
            Try
                AppState.Registry.SetActive(index)
                AppState.ReloadActiveTokenizer()
            Catch
            End Try
            RefreshList()
        End Sub

        ''' <summary>Removes the tokenizer at <paramref name="index"/> and reloads the active one.</summary>
        Private Sub Delete(index As Integer)
            Try
                AppState.Registry.Remove(index)
                AppState.ReloadActiveTokenizer()
            Catch
            End Try
            RefreshList()
        End Sub

        ''' <summary>Opens the file pickers and registers a new tokenizer.</summary>
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

    End Class

    ''' <summary>Row model for a tokenizer card in <see cref="TokenizerSettingsSection"/>.</summary>
    Public Class TokenizerListItem

        ''' <summary>Display name of the tokenizer.</summary>
        Public Property Name As String

        ''' <summary>Human-readable summary line (model type · vocab · path).</summary>
        Public Property Detail As String

        ''' <summary>True when this tokenizer is the currently active one.</summary>
        Public Property IsActive As Boolean

        ''' <summary>True for the bundled tokenizer that cannot be removed.</summary>
        Public Property IsBundled As Boolean

        ''' <summary>Index of this definition in the registry.</summary>
        Public Property Index As Integer

    End Class

End Namespace
