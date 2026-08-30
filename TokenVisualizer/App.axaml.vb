Imports System.Linq
Imports Avalonia
Imports Avalonia.Controls.ApplicationLifetimes
Imports Avalonia.Markup.Xaml
Imports Avalonia.Styling
Imports FluentAvalonia.Styling
Imports TokenVisualizer.Services

Partial Public Class App
	Inherits Application

	Public Overrides Sub Initialize()
		AvaloniaXamlLoader.Load(Me)
	End Sub

	Public Overrides Sub OnFrameworkInitializationCompleted()
		ApplyThemeName(SettingsService.Load().ThemeName)

		Dim desktop = TryCast(ApplicationLifetime, IClassicDesktopStyleApplicationLifetime)
		If desktop IsNot Nothing Then
			desktop.MainWindow = New Views.MainWindow
		End If

		MyBase.OnFrameworkInitializationCompleted()
	End Sub

	''' <summary>
	''' Applies a theme name ("System"/"Light"/"Dark") to the FluentAvaloniaTheme.
	''' For an explicit Light/Dark we also turn PreferSystemTheme off; otherwise the
	''' theme's ColorValuesChanged handler (FluentAvaloniaTheme.OnPlatformColorValuesChanged)
	''' forces Application.RequestedThemeVariant back to the system theme whenever
	''' Windows reports a color change (system theme toggle, accent change, ...),
	''' undoing the user's selection.
	''' </summary>
	Public Shared Sub ApplyThemeName(themeName As String)
		Dim theme = Application.Current.Styles.OfType(Of FluentAvaloniaTheme)().FirstOrDefault()
		If theme Is Nothing Then Return
		Select Case themeName
			Case "Light"
				theme.PreferSystemTheme = False
				Application.Current.RequestedThemeVariant = ThemeVariant.Light
			Case "Dark"
				theme.PreferSystemTheme = False
				Application.Current.RequestedThemeVariant = ThemeVariant.Dark
			Case Else
				theme.PreferSystemTheme = True
				Application.Current.RequestedThemeVariant = Nothing
		End Select
	End Sub
End Class
