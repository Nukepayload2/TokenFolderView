Imports System.Collections.Generic
Imports Avalonia
Imports Avalonia.Controls
Imports Avalonia.Interactivity
Imports Avalonia.Styling
Imports Avalonia.Threading
Imports FluentAvalonia.UI.Controls
Imports TokenVisualizer.Services

Namespace Views

    Partial Class SettingsPage
        Inherits UserControl

        Private _settings As AppSettings
        Private _loading As Boolean = True
        Private WithEvents _blacklistTimer As DispatcherTimer

        Public Sub New()
            InitializeComponent()
            _blacklistTimer = New DispatcherTimer With {
                .Interval = TimeSpan.FromMilliseconds(400)
            }
        End Sub

        Private Sub SettingsPage_Loaded() Handles Me.Loaded
            _settings = SettingsService.Load()
            ApplySettingsToUi()
            _loading = False
        End Sub

        Private Sub ApplySettingsToUi()
            Dim maxSize As Double = _settings.MaxFileSizeMb
            If maxSize < 0.1 Then maxSize = 0.1
            If maxSize > 10240 Then maxSize = 10240
            NumMaxSize.Value = maxSize

            TglCheckBinary.IsChecked = _settings.CheckBinary

            TxtBlacklist.Text = String.Join(vbLf, _settings.BlacklistedFolderNames)

            Select Case _settings.ThemeName
                Case "Light" : CboTheme.SelectedIndex = 1
                Case "Dark" : CboTheme.SelectedIndex = 2
                Case Else : CboTheme.SelectedIndex = 0
            End Select
        End Sub

        ' ------------------------------------------------------------------
        ' 扫描
        ' ------------------------------------------------------------------

        Private Sub NumMaxSize_ValueChanged(sender As FANumberBox, e As FANumberBoxValueChangedEventArgs) Handles NumMaxSize.ValueChanged
            If _loading OrElse _settings Is Nothing Then Return
            _settings.MaxFileSizeMb = NumMaxSize.Value
            SettingsService.Save(_settings)
        End Sub

        Private Sub TglCheckBinary_IsCheckedChanged(sender As Object, e As RoutedEventArgs) Handles TglCheckBinary.IsCheckedChanged
            If _loading OrElse _settings Is Nothing Then Return
            _settings.CheckBinary = TglCheckBinary.IsChecked.GetValueOrDefault(True)
            SettingsService.Save(_settings)
        End Sub

        Private Sub TxtBlacklist_TextChanged(sender As Object, e As TextChangedEventArgs) Handles TxtBlacklist.TextChanged
            If _loading Then Return
            _blacklistTimer.Stop()
            _blacklistTimer.Start()
        End Sub

        Private Sub BlacklistTimer_Tick() Handles _blacklistTimer.Tick
            _blacklistTimer.Stop()
            If _settings Is Nothing Then Return

            Dim names As New List(Of String)()
            Dim text As String = If(TxtBlacklist.Text, "")
            For Each line As String In text.Replace(vbCr, vbLf).Split({vbLf}, StringSplitOptions.RemoveEmptyEntries)
                Dim name As String = line.Trim()
                If name.Length > 0 Then names.Add(name)
            Next
            _settings.BlacklistedFolderNames = names
            SettingsService.Save(_settings)
        End Sub

        Private Sub BtnResetBlacklist_Click() Handles BtnResetBlacklist.Click
            If _settings Is Nothing Then Return
            _settings.BlacklistedFolderNames = New List(Of String) From {"bin", "obj", "node_modules", ".vs", ".git", "dist", "target"}
            _loading = True
            TxtBlacklist.Text = String.Join(vbLf, _settings.BlacklistedFolderNames)
            _loading = False
            SettingsService.Save(_settings)
        End Sub

        ' ------------------------------------------------------------------
        ' 外观
        ' ------------------------------------------------------------------

        Private Sub CboTheme_SelectionChanged(sender As Object, e As SelectionChangedEventArgs) Handles CboTheme.SelectionChanged
            If _loading OrElse _settings Is Nothing Then Return
            Dim tag As String = GetSelectedTag(CboTheme)
            Dim themeName As String = "System"
            Select Case tag
                Case "Light"
                    Application.Current.RequestedThemeVariant = ThemeVariant.Light
                    themeName = "Light"
                Case "Dark"
                    Application.Current.RequestedThemeVariant = ThemeVariant.Dark
                    themeName = "Dark"
                Case Else
                    Application.Current.RequestedThemeVariant = Nothing
                    themeName = "System"
            End Select
            _settings.ThemeName = themeName
            SettingsService.Save(_settings)
        End Sub

        Private Shared Function GetSelectedTag(cbo As ComboBox) As String
            Dim item = TryCast(cbo.SelectedItem, ComboBoxItem)
            If item Is Nothing Then Return Nothing
            Return item.Tag?.ToString()
        End Function

    End Class
End Namespace
