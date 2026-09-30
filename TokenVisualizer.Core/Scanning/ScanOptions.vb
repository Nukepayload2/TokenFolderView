Imports System.Collections.Generic

Namespace Scanning

    ''' <summary>
    ''' Options controlling how <see cref="FolderScanner"/> walks a directory tree: which folder
    ''' names are skipped at any depth, the maximum file size that is tokenized, whether file
    ''' heads are sniffed for binary content, which encoding text is read with, and whether the
    ''' shared BPE word cache survives the scan.
    ''' </summary>
    Public NotInheritable Class ScanOptions

        ''' <summary>Folder names that are skipped at any depth (compared by name only, case-insensitive).</summary>
        Public Property FolderBlacklist As IReadOnlyList(Of String)

        ''' <summary>Files strictly larger than this many bytes are skipped without being tokenized.</summary>
        Public Property MaxFileSizeBytes As Long

        ''' <summary>When True, the first 4 KiB of each candidate file is checked for binary content.</summary>
        Public Property CheckBinary As Boolean

        ''' <summary>When False (the default), the shared BPE word cache is dropped when the scan ends instead of warming the next one.</summary>
        Public Property RetainWordCache As Boolean

        ''' <summary>
        ''' Encoding file text is read with, resolved once per scan by <see cref="FileEncoding.Resolve"/>.
        ''' It also decides what counts as binary: see <see cref="BinaryDetector"/>.
        ''' </summary>
        Public Property TextEncoding As Global.System.Text.Encoding

        Public Sub New()
            FolderBlacklist = New List(Of String) From {
                "bin", "obj", "node_modules", ".vs", ".git", "dist", "target"
            }
            MaxFileSizeBytes = 10 * 1024 * 1024
            CheckBinary = True
            ' Explicit, so it cannot drift from AppSettings.RetainWordCache's default.
            RetainWordCache = False
            ' Explicit, so it cannot drift from AppSettings.FileEncoding's default.
            TextEncoding = Global.System.Text.Encoding.UTF8
        End Sub

        Private Shared ReadOnly _default As New ScanOptions()

        ''' <summary>The shared default options instance.</summary>
        Public Shared ReadOnly Property [Default] As ScanOptions
            Get
                Return _default
            End Get
        End Property

    End Class
End Namespace
