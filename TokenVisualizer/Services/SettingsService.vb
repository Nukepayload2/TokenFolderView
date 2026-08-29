Imports System.IO
Imports System.Text.Encodings.Web
Imports System.Text.Json

Namespace Services

    ''' <summary>
    ''' Loads and saves application settings to %LocalAppData%\TokenVisualizer\settings.json.
    ''' Missing or corrupt files fall back to defaults.
    ''' </summary>
    Public NotInheritable Class SettingsService

        ''' <summary>Full path of the settings file.</summary>
        Public Shared ReadOnly SettingsPath As String =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "TokenVisualizer", "settings.json")

        ''' <summary>Loads settings, or returns a default instance if the file is missing/corrupt.</summary>
        Public Shared Function Load() As AppSettings
            Try
                If File.Exists(SettingsPath) Then
                    Dim json As String = File.ReadAllText(SettingsPath)
                    Dim options As New JsonSerializerOptions With {
                        .PropertyNameCaseInsensitive = True,
                        .ReadCommentHandling = JsonCommentHandling.Skip,
                        .AllowTrailingCommas = True
                    }
                    Dim result As AppSettings = JsonSerializer.Deserialize(Of AppSettings)(json, options)
                    If result IsNot Nothing Then
                        If result.Tokenizers Is Nothing Then result.Tokenizers = New List(Of TokenizerSettings)()
                        If result.BlacklistedFolderNames Is Nothing Then result.BlacklistedFolderNames = New List(Of String)()
                        Return result
                    End If
                End If
            Catch
            End Try
            Return New AppSettings()
        End Function

        ''' <summary>Persists settings as indented JSON with relaxed escaping for non-ASCII text.</summary>
        Public Shared Sub Save(appSettings As AppSettings)
            Try
                Dim dir As String = Path.GetDirectoryName(SettingsPath)
                If Not String.IsNullOrEmpty(dir) AndAlso Not Directory.Exists(dir) Then
                    Directory.CreateDirectory(dir)
                End If
                Dim options As New JsonSerializerOptions With {
                    .WriteIndented = True,
                    .Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                }
                File.WriteAllText(SettingsPath, JsonSerializer.Serialize(appSettings, options))
            Catch
            End Try
        End Sub

    End Class

    ''' <summary>Application-wide settings persisted in settings.json.</summary>
    Public Class AppSettings

        ''' <summary>Folder names skipped at any depth during a scan.</summary>
        Public Property BlacklistedFolderNames As List(Of String) =
            New List(Of String) From {"bin", "obj", "node_modules", ".vs", ".git", "dist", "target"}

        ''' <summary>Maximum file size (in MB) that is tokenized; larger files are skipped.</summary>
        Public Property MaxFileSizeMb As Double = 10

        ''' <summary>When True, the head of each candidate file is sniffed for binary content.</summary>
        Public Property CheckBinary As Boolean = True

        ''' <summary>UI theme: "System", "Light" or "Dark".</summary>
        Public Property ThemeName As String = "System"

        ''' <summary>Window backdrop: "Simple", "Mica" or "Acrylic".</summary>
        Public Property BackdropName As String = "Simple"

        ''' <summary>Index of the active tokenizer in <see cref="Tokenizers"/>.</summary>
        Public Property ActiveTokenizerIndex As Integer = 0

        ''' <summary>Registered tokenizer definitions.</summary>
        Public Property Tokenizers As List(Of TokenizerSettings) = New List(Of TokenizerSettings)()

    End Class

    ''' <summary>A registered tokenizer definition (tokenizer.json + optional tokenizer_config.json).</summary>
    Public Class TokenizerSettings

        ''' <summary>Display name of the tokenizer.</summary>
        Public Property Name As String = ""

        ''' <summary>Absolute path of the tokenizer.json file.</summary>
        Public Property TokenizerJsonPath As String = ""

        ''' <summary>Absolute path of the tokenizer_config.json file (optional; may be "").</summary>
        Public Property TokenizerConfigJsonPath As String = ""

        ''' <summary>True for the bundled tokenizer that cannot be removed by the user.</summary>
        Public Property IsBundled As Boolean = False

    End Class

End Namespace
