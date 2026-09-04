Imports System.IO
Imports System.Text.Encodings.Web
Imports System.Text.Json
Imports System.Text.Json.Nodes

Namespace Services

    ''' <summary>
    ''' Loads and saves application settings to %LocalAppData%\TokenVisualizer\settings.json.
    ''' Missing or corrupt files fall back to defaults.
    '''
    ''' Persistence is mapped by hand between JsonElement/JsonObject and the POCOs because the
    ''' reflection-based JsonSerializer is not usable from VB under Native AOT: its source
    ''' generator emits C#-only partials, and the reflection path itself is RequiresDynamicCode.
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
                    Return ParseSettings(File.ReadAllText(SettingsPath))
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

                Dim root As New JsonObject()

                If appSettings.BlacklistedFolderNames IsNot Nothing Then
                    Dim names As New List(Of JsonNode)()
                    For Each name As String In appSettings.BlacklistedFolderNames
                        names.Add(JsonValue.Create(name))
                    Next
                    root("BlacklistedFolderNames") = New JsonArray(names.ToArray())
                End If

                root("MaxFileSizeMb") = JsonValue.Create(appSettings.MaxFileSizeMb)
                root("CheckBinary") = JsonValue.Create(appSettings.CheckBinary)
                root("ThemeName") = JsonValue.Create(If(appSettings.ThemeName, ""))
                root("ActiveTokenizerIndex") = JsonValue.Create(appSettings.ActiveTokenizerIndex)

                Dim tokenizerNodes As New List(Of JsonNode)()
                If appSettings.Tokenizers IsNot Nothing Then
                    For Each t As TokenizerSettings In appSettings.Tokenizers
                        Dim item As New JsonObject()
                        item("Name") = JsonValue.Create(If(t.Name, ""))
                        item("TokenizerJsonPath") = JsonValue.Create(If(t.TokenizerJsonPath, ""))
                        item("TokenizerConfigJsonPath") = JsonValue.Create(If(t.TokenizerConfigJsonPath, ""))
                        item("IsBundled") = JsonValue.Create(t.IsBundled)
                        tokenizerNodes.Add(item)
                    Next
                End If
                root("Tokenizers") = New JsonArray(tokenizerNodes.ToArray())

                Dim options As New JsonSerializerOptions With {
                    .WriteIndented = True,
                    .Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                }
                File.WriteAllText(SettingsPath, root.ToJsonString(options))
            Catch
            End Try
        End Sub

        ' ------------------------------------------------------------------
        ' AOT-safe manual mapping
        ' ------------------------------------------------------------------

        Private Shared Function ParseSettings(json As String) As AppSettings
            Dim docOptions As New JsonDocumentOptions With {
                .CommentHandling = JsonCommentHandling.Skip,
                .AllowTrailingCommas = True
            }
            Using doc As JsonDocument = JsonDocument.Parse(json, docOptions)
                Dim root As JsonElement = doc.RootElement
                If root.ValueKind <> JsonValueKind.Object Then Throw New JsonException("Settings root is not an object.")

                Dim settings As New AppSettings()

                Dim v As JsonElement? = TryGetProperty(root, "BlacklistedFolderNames")
                If v.HasValue AndAlso v.Value.ValueKind = JsonValueKind.Array Then
                    settings.BlacklistedFolderNames = ReadStringList(v.Value)
                End If

                v = TryGetProperty(root, "MaxFileSizeMb")
                If v.HasValue AndAlso v.Value.ValueKind = JsonValueKind.Number Then
                    settings.MaxFileSizeMb = v.Value.GetDouble()
                End If

                v = TryGetProperty(root, "CheckBinary")
                If v.HasValue AndAlso IsBool(v.Value) Then
                    settings.CheckBinary = v.Value.GetBoolean()
                End If

                v = TryGetProperty(root, "ThemeName")
                If v.HasValue AndAlso v.Value.ValueKind = JsonValueKind.String Then
                    settings.ThemeName = v.Value.GetString()
                End If

                v = TryGetProperty(root, "ActiveTokenizerIndex")
                If v.HasValue AndAlso v.Value.ValueKind = JsonValueKind.Number Then
                    settings.ActiveTokenizerIndex = v.Value.GetInt32()
                End If

                v = TryGetProperty(root, "Tokenizers")
                If v.HasValue AndAlso v.Value.ValueKind = JsonValueKind.Array Then
                    settings.Tokenizers = ReadTokenizerList(v.Value)
                End If

                Return settings
            End Using
        End Function

        ''' <summary>
        ''' Finds a root property case-insensitively. JsonDocument itself is case-sensitive and
        ''' offers no name-matching option, so the same tolerance as the old
        ''' PropertyNameCaseInsensitive serializer is re-implemented here.
        ''' </summary>
        Private Shared Function TryGetProperty(obj As JsonElement, name As String) As JsonElement?
            If obj.ValueKind <> JsonValueKind.Object Then Return Nothing
            For Each prop As JsonProperty In obj.EnumerateObject()
                If String.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase) Then
                    Return prop.Value
                End If
            Next
            Return Nothing
        End Function

        Private Shared Function IsBool(el As JsonElement) As Boolean
            Return el.ValueKind = JsonValueKind.True OrElse el.ValueKind = JsonValueKind.False
        End Function

        Private Shared Function ReadStringList(el As JsonElement) As List(Of String)
            Dim list As New List(Of String)()
            For Each item As JsonElement In el.EnumerateArray()
                If item.ValueKind = JsonValueKind.String Then
                    list.Add(item.GetString())
                End If
            Next
            Return list
        End Function

        Private Shared Function ReadTokenizerList(el As JsonElement) As List(Of TokenizerSettings)
            Dim list As New List(Of TokenizerSettings)()
            For Each item As JsonElement In el.EnumerateArray()
                If item.ValueKind <> JsonValueKind.Object Then Continue For

                Dim t As New TokenizerSettings()
                Dim v As JsonElement? = TryGetProperty(item, "Name")
                If v.HasValue AndAlso v.Value.ValueKind = JsonValueKind.String Then
                    t.Name = v.Value.GetString()
                End If
                v = TryGetProperty(item, "TokenizerJsonPath")
                If v.HasValue AndAlso v.Value.ValueKind = JsonValueKind.String Then
                    t.TokenizerJsonPath = v.Value.GetString()
                End If
                v = TryGetProperty(item, "TokenizerConfigJsonPath")
                If v.HasValue AndAlso v.Value.ValueKind = JsonValueKind.String Then
                    t.TokenizerConfigJsonPath = v.Value.GetString()
                End If
                v = TryGetProperty(item, "IsBundled")
                If v.HasValue AndAlso IsBool(v.Value) Then
                    t.IsBundled = v.Value.GetBoolean()
                End If
                list.Add(t)
            Next
            Return list
        End Function

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
