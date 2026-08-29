Imports System.IO
Imports System.Text.Json
Imports Tokenizers

Namespace Services

    ''' <summary>
    ''' Manages the registered tokenizer definitions (persisted through <see cref="SettingsService"/>)
    ''' and the active tokenizer. Owns a cached <see cref="Tokenizer"/> for the active definition.
    ''' On first run (no tokenizers), registers the bundled deepseek tokenizer by absolute path.
    ''' </summary>
    Public NotInheritable Class TokenizerRegistry

        Private ReadOnly _lock As New Object()
        Private _settings As AppSettings
        Private _cachedTokenizer As Tokenizer
        Private _cachedIndex As Integer = -1

        ''' <summary>Creates a registry backed by the saved settings.</summary>
        Public Sub New()
            _settings = SettingsService.Load()
            If _settings.Tokenizers Is Nothing Then _settings.Tokenizers = New List(Of TokenizerSettings)()
            If _settings.Tokenizers.Count = 0 AndAlso EnsureDefaultRegistered(_settings) Then
                SettingsService.Save(_settings)
            End If
        End Sub

        ''' <summary>Reloads the definitions from disk (after external changes).</summary>
        Public Sub Reload()
            SyncLock _lock
                _settings = SettingsService.Load()
                If _settings.Tokenizers Is Nothing Then _settings.Tokenizers = New List(Of TokenizerSettings)()
                InvalidateCache()
            End SyncLock
        End Sub

        ''' <summary>The registered tokenizer definitions.</summary>
        Public ReadOnly Property Definitions As List(Of TokenizerSettings)
            Get
                SyncLock _lock
                    Return _settings.Tokenizers
                End SyncLock
            End Get
        End Property

        ''' <summary>The index of the active tokenizer.</summary>
        Public ReadOnly Property ActiveIndex As Integer
            Get
                SyncLock _lock
                    Return _settings.ActiveTokenizerIndex
                End SyncLock
            End Get
        End Property

        ''' <summary>
        ''' Loads (and caches) the active tokenizer. Throws with a clear message when the
        ''' tokenizer.json file is missing or cannot be loaded.
        ''' </summary>
        Public Function GetActiveTokenizer() As Tokenizer
            SyncLock _lock
                Dim index As Integer = NormalizeIndex(_settings.ActiveTokenizerIndex)
                If _cachedTokenizer IsNot Nothing AndAlso _cachedIndex = index Then
                    Return _cachedTokenizer
                End If

                Dim def As TokenizerSettings = _settings.Tokenizers(index)
                If String.IsNullOrEmpty(def.TokenizerJsonPath) OrElse Not File.Exists(def.TokenizerJsonPath) Then
                    Dim display As String = If(String.IsNullOrEmpty(def.TokenizerJsonPath), "(未设置)", def.TokenizerJsonPath)
                    Throw New FileNotFoundException($"找不到分词器文件：{display}")
                End If

                Dim tokenizer As Tokenizer = Tokenizer.FromFile(def.TokenizerJsonPath)
                _cachedTokenizer = tokenizer
                _cachedIndex = index
                Return tokenizer
            End SyncLock
        End Function

        ''' <summary>Returns the display name of the active tokenizer.</summary>
        Public Function GetActiveName() As String
            SyncLock _lock
                If _settings.Tokenizers.Count = 0 Then Return "未加载"
                Dim index As Integer = NormalizeIndex(_settings.ActiveTokenizerIndex)
                Return _settings.Tokenizers(index).Name
            End SyncLock
        End Function

        ''' <summary>Adds a user tokenizer definition, persists it, and returns its index.</summary>
        Public Function Register(name As String, tokenizerJson As String, configJson As String) As Integer
            SyncLock _lock
                _settings.Tokenizers.Add(New TokenizerSettings With {
                    .Name = name,
                    .TokenizerJsonPath = tokenizerJson,
                    .TokenizerConfigJsonPath = If(configJson, ""),
                    .IsBundled = False
                })
                Dim index As Integer = _settings.Tokenizers.Count - 1
                SettingsService.Save(_settings)
                InvalidateCache()
                Return index
            End SyncLock
        End Function

        ''' <summary>Removes a user-added tokenizer. Bundled entries are locked and ignored.</summary>
        Public Sub Remove(index As Integer)
            SyncLock _lock
                If index < 0 OrElse index >= _settings.Tokenizers.Count Then Return
                If _settings.Tokenizers(index).IsBundled Then Return
                _settings.Tokenizers.RemoveAt(index)
                If _settings.ActiveTokenizerIndex >= _settings.Tokenizers.Count Then
                    _settings.ActiveTokenizerIndex = Math.Max(0, _settings.Tokenizers.Count - 1)
                End If
                SettingsService.Save(_settings)
                InvalidateCache()
            End SyncLock
        End Sub

        ''' <summary>Sets the active tokenizer index and persists.</summary>
        Public Sub SetActive(index As Integer)
            SyncLock _lock
                If index < 0 OrElse index >= _settings.Tokenizers.Count Then Return
                _settings.ActiveTokenizerIndex = index
                SettingsService.Save(_settings)
                InvalidateCache()
            End SyncLock
        End Sub

        ''' <summary>
        ''' Human-readable summary for a tokenizer, e.g. "BPE · 128,000 vocab · C:\...\tokenizer.json".
        ''' Parses model.type and the model vocab size cheaply without a full pipeline build.
        ''' </summary>
        Public Function Describe(index As Integer) As String
            SyncLock _lock
                If index < 0 OrElse index >= _settings.Tokenizers.Count Then Return ""
                Dim def As TokenizerSettings = _settings.Tokenizers(index)
                Try
                    If String.IsNullOrEmpty(def.TokenizerJsonPath) OrElse Not File.Exists(def.TokenizerJsonPath) Then
                        Return $"文件不存在 · {If(def.TokenizerJsonPath, "(未设置)")}"
                    End If
                    Dim json As String = File.ReadAllText(def.TokenizerJsonPath)
                    Dim meta As (modelType As String, vocabSize As Integer?) = ReadMeta(json)
                    Dim parts As New List(Of String)()
                    If Not String.IsNullOrEmpty(meta.modelType) Then parts.Add(meta.modelType)
                    If meta.vocabSize.HasValue Then parts.Add($"{meta.vocabSize.Value:N0} vocab")
                    Dim summary As String = String.Join(" · ", parts)
                    If String.IsNullOrEmpty(summary) Then summary = "未知模型"
                    Return $"{summary} · {def.TokenizerJsonPath}"
                Catch ex As Exception
                    Return $"无法读取 · {def.TokenizerJsonPath}"
                End Try
            End SyncLock
        End Function

        ''' <summary>
        ''' On a first run (no tokenizers registered), registers the repo's bundled
        ''' deepseek-v4-flash/tokenizer.json by absolute path (no copy). Returns True when a
        ''' default was added.
        ''' </summary>
        Public Shared Function EnsureDefaultRegistered(appSettings As AppSettings) As Boolean
            If appSettings Is Nothing OrElse appSettings.Tokenizers Is Nothing Then Return False
            If appSettings.Tokenizers.Count > 0 Then Return False

            Dim bundledPath As String = ResolveBundledTokenizerPath()
            If bundledPath Is Nothing Then Return False

            appSettings.Tokenizers.Add(New TokenizerSettings With {
                .Name = "deepseek-v4-flash",
                .TokenizerJsonPath = bundledPath,
                .TokenizerConfigJsonPath = Path.Combine(Path.GetDirectoryName(bundledPath), "tokenizer_config.json"),
                .IsBundled = True
            })
            appSettings.ActiveTokenizerIndex = 0
            Return True
        End Function

        Private Sub InvalidateCache()
            _cachedTokenizer = Nothing
            _cachedIndex = -1
        End Sub

        Private Function NormalizeIndex(index As Integer) As Integer
            If _settings.Tokenizers.Count = 0 Then
                Throw New InvalidOperationException("还没有注册任何分词器。")
            End If
            If index < 0 Then index = 0
            If index >= _settings.Tokenizers.Count Then index = _settings.Tokenizers.Count - 1
            Return index
        End Function

        Private Shared Function ResolveBundledTokenizerPath() As String
            ' 1. Bundled copies next to the executable.
            Dim bundled As String() = {
                Path.Combine(AppContext.BaseDirectory, "deepseek-v4-flash", "tokenizer.json"),
                Path.Combine(AppContext.BaseDirectory, "tokenizer.json")
            }
            For Each candidate As String In bundled
                If File.Exists(candidate) Then Return candidate
            Next

            ' 2. Walk up from the executable to find the repo's deepseek-v4-flash folder.
            Dim dir As DirectoryInfo = New DirectoryInfo(AppContext.BaseDirectory)
            For i As Integer = 0 To 10
                Dim candidate As String = Path.Combine(dir.FullName, "deepseek-v4-flash", "tokenizer.json")
                If File.Exists(candidate) Then Return candidate
                If dir.Parent Is Nothing Then Exit For
                dir = dir.Parent
            Next
            Return Nothing
        End Function

        ''' <summary>Reads model.type and the model vocab size from tokenizer.json without a full load.</summary>
        Private Shared Function ReadMeta(json As String) As (modelType As String, vocabSize As Integer?)
            Dim modelType As String = Nothing
            Dim vocabSize As Integer? = Nothing
            Try
                Using doc As JsonDocument = JsonDocument.Parse(json)
                    Dim root As JsonElement = doc.RootElement
                    Dim model As JsonElement
                    If root.TryGetProperty("model", model) Then
                        Dim typeEl As JsonElement
                        If model.TryGetProperty("type", typeEl) Then
                            modelType = typeEl.GetString()
                        End If
                        Dim vsEl As JsonElement
                        If model.TryGetProperty("vocab_size", vsEl) AndAlso vsEl.ValueKind = JsonValueKind.Number Then
                            vocabSize = vsEl.GetInt32()
                        End If
                        If Not vocabSize.HasValue Then
                            Dim vocabEl As JsonElement
                            If model.TryGetProperty("vocab", vocabEl) AndAlso vocabEl.ValueKind = JsonValueKind.Object Then
                                Dim count As Integer = 0
                                For Each prop As JsonProperty In vocabEl.EnumerateObject()
                                    count += 1
                                Next
                                vocabSize = count
                            End If
                        End If
                    End If
                End Using
            Catch
            End Try
            Return (modelType, vocabSize)
        End Function

    End Class

End Namespace
