Imports Tokenizers
Imports Tokenizers.Scanning

Namespace Services

    ''' <summary>
    ''' Tiny process-wide shared state for the Explorer page and the MainWindow status bar.
    ''' No DI: MainWindow and ExplorerPage both read/write <see cref="Current"/> directly.
    ''' The active tokenizer is managed through <see cref="TokenizerRegistry"/>.
    ''' </summary>
    Public NotInheritable Class AppState

        Private Shared ReadOnly _current As New AppState()
        Private Shared ReadOnly _tokenizerLock As New Object()
        Private Shared ReadOnly _registryLock As New Object()
        Private Shared _registry As TokenizerRegistry

        Private Sub New()
        End Sub

        ''' <summary>The singleton instance.</summary>
        Public Shared ReadOnly Property Current As AppState
            Get
                Return _current
            End Get
        End Property

        ''' <summary>The shared tokenizer registry (lazily created on first access).</summary>
        Public Shared ReadOnly Property Registry As TokenizerRegistry
            Get
                SyncLock _registryLock
                    If _registry Is Nothing Then
                        _registry = New TokenizerRegistry()
                    End If
                    Return _registry
                End SyncLock
            End Get
        End Property

        ''' <summary>The tokenizer used for scanning and the colored file view.</summary>
        Public Property ActiveTokenizer As Tokenizer

        ''' <summary>Display name of the active tokenizer (e.g. "deepseek-v4-flash").</summary>
        Public Property ActiveTokenizerName As String = "未加载"

        ''' <summary>The currently running (or last-run) folder scanner.</summary>
        Public Property ActiveScan As FolderScanner

        ''' <summary>The aggregated root node of the most recent completed scan.</summary>
        Public Property RootNode As ScanTreeNode

        ''' <summary>True while a scan is running (drives the status bar + page cancel button).</summary>
        Public Property IsScanning As Boolean

        ''' <summary>The folder path of the most recent scan (used by the rescan button).</summary>
        Public Property CurrentScanPath As String

        ''' <summary>
        ''' Loads the active tokenizer from the registry (registering the bundled deepseek
        ''' tokenizer on first run). Idempotent and thread-safe.
        ''' </summary>
        Public Shared Sub EnsureActiveTokenizer()
            If _current.ActiveTokenizer IsNot Nothing Then Return
            SyncLock _tokenizerLock
                If _current.ActiveTokenizer IsNot Nothing Then Return
                Try
                    Dim tokenizerRegistry As TokenizerRegistry = Registry
                    _current.ActiveTokenizer = tokenizerRegistry.GetActiveTokenizer()
                    _current.ActiveTokenizerName = tokenizerRegistry.GetActiveName()
                Catch
                    _current.ActiveTokenizer = Nothing
                    _current.ActiveTokenizerName = "未找到 tokenizer"
                End Try
            End SyncLock
        End Sub

        ''' <summary>Clears and reloads the active tokenizer after the active definition changes.</summary>
        Public Shared Sub ReloadActiveTokenizer()
            SyncLock _tokenizerLock
                _current.ActiveTokenizer = Nothing
                _current.ActiveTokenizerName = "未加载"
            End SyncLock
            EnsureActiveTokenizer()
        End Sub

    End Class
End Namespace
