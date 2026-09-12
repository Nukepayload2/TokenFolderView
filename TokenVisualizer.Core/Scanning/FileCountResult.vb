Imports System

Namespace Scanning

    ''' <summary>Outcome kind of counting a single file.</summary>
    Public Enum FileCountStatus
        ''' <summary>The file was read and token-counted; the counts are meaningful.</summary>
        Counted
        ''' <summary>The file is larger than the configured limit and was skipped without being read.</summary>
        SkippedSize
        ''' <summary>The file head looked like binary content and the file was skipped.</summary>
        SkippedBinary
        ''' <summary>The file (or the directory it should live in) does not exist any more.</summary>
        Missing
        ''' <summary>Counting failed for a reason that may well be transient (e.g. the file is locked).</summary>
        ''' <remarks>The name is a Visual Basic keyword, so it is escaped as a member name.</remarks>
        [Error]
    End Enum

    ''' <summary>
    ''' The result of counting one file. Only <see cref="FileCountStatus.Counted"/> carries counts;
    ''' every other kind is a data-free shared instance, so callers switch on <see cref="Status"/>
    ''' rather than inspecting <see cref="TokenCount"/> / <see cref="Length"/>. A result is never
    ''' <c>Nothing</c> for a well-behaved counter.
    ''' </summary>
    ''' <remarks>
    ''' A reference type rather than a structure: a default-initialized structure would silently look
    ''' like a counted file of zero tokens and zero bytes, which would let a ghost file (0 tokens, 0
    ''' bytes) into the tree - exactly the corruption the callers must avoid.
    ''' </remarks>
    Public NotInheritable Class FileCountResult

        Private Shared ReadOnly _skippedSize As New FileCountResult(FileCountStatus.SkippedSize, 0, 0)
        Private Shared ReadOnly _skippedBinary As New FileCountResult(FileCountStatus.SkippedBinary, 0, 0)
        Private Shared ReadOnly _missing As New FileCountResult(FileCountStatus.Missing, 0, 0)
        Private Shared ReadOnly _error As New FileCountResult(FileCountStatus.Error, 0, 0)

        Private Sub New(status As FileCountStatus, tokenCount As Long, length As Long)
            Me.Status = status
            Me.TokenCount = tokenCount
            Me.Length = length
        End Sub

        ''' <summary>The outcome kind; only <see cref="FileCountStatus.Counted"/> carries counts.</summary>
        Public ReadOnly Property Status As FileCountStatus

        ''' <summary>Number of tokens in the file (meaningful only when <see cref="Status"/> is Counted).</summary>
        Public ReadOnly Property TokenCount As Long

        ''' <summary>Size of the file in bytes (meaningful only when <see cref="Status"/> is Counted).</summary>
        Public ReadOnly Property Length As Long

        ''' <summary>A counted file with its token count and size in bytes.</summary>
        Public Shared Function Counted(tokenCount As Long, length As Long) As FileCountResult
            Return New FileCountResult(FileCountStatus.Counted, tokenCount, length)
        End Function

        ''' <summary>The file exceeded the size limit.</summary>
        Public Shared ReadOnly Property SkippedSize As FileCountResult
            Get
                Return _skippedSize
            End Get
        End Property

        ''' <summary>The file looked like binary content.</summary>
        Public Shared ReadOnly Property SkippedBinary As FileCountResult
            Get
                Return _skippedBinary
            End Get
        End Property

        ''' <summary>The file (or its directory) is gone.</summary>
        Public Shared ReadOnly Property Missing As FileCountResult
            Get
                Return _missing
            End Get
        End Property

        ''' <summary>Counting failed; the file may simply be in use right now.</summary>
        Public Shared ReadOnly Property [Error] As FileCountResult
            Get
                Return _error
            End Get
        End Property

    End Class
End Namespace
