Imports System
Imports System.Diagnostics
Imports System.Threading

Namespace Scanning

    ''' <summary>
    ''' Live counters for a folder scan. All fields are read/written through
    ''' <see cref="Interlocked"/>, so the UI can poll them from another thread while
    ''' <see cref="FolderScanner"/> runs the parallel tokenization pass.
    ''' </summary>
    Public NotInheritable Class ScanProgress

        Private _filesScanned As Long
        Private _filesSkipped As Long
        Private _totalTokens As Long
        Private _filesWithErrors As Long
        Private ReadOnly _stopwatch As New Stopwatch()

        ''' <summary>The token source the UI uses to request cancellation of a running scan.</summary>
        Public ReadOnly Property Cancellation As CancellationTokenSource = New CancellationTokenSource()

        Public Sub IncrementFilesScanned()
            Interlocked.Increment(_filesScanned)
        End Sub

        Public Sub IncrementFilesSkipped()
            Interlocked.Increment(_filesSkipped)
        End Sub

        Public Sub IncrementFilesWithErrors()
            Interlocked.Increment(_filesWithErrors)
        End Sub

        Public Sub AddTotalTokens(count As Long)
            Interlocked.Add(_totalTokens, count)
        End Sub

        Public Function ReadFilesScanned() As Long
            Return Interlocked.Read(_filesScanned)
        End Function

        Public Function ReadFilesSkipped() As Long
            Return Interlocked.Read(_filesSkipped)
        End Function

        Public Function ReadTotalTokens() As Long
            Return Interlocked.Read(_totalTokens)
        End Function

        Public Function ReadFilesWithErrors() As Long
            Return Interlocked.Read(_filesWithErrors)
        End Function

        ''' <summary>Starts the elapsed-time measurement. Called once at the start of <see cref="FolderScanner.ScanAsync"/>.</summary>
        Public Sub Start()
            _stopwatch.Restart()
        End Sub

        ''' <summary>Freezes the elapsed-time measurement. Called when the scan finishes so the UI can report load speed.</summary>
        Public Sub [Stop]()
            _stopwatch.Stop()
        End Sub

        ''' <summary>Elapsed scan time (stable once <see cref="Stop"/> has been called).</summary>
        Public ReadOnly Property Elapsed As TimeSpan
            Get
                Return _stopwatch.Elapsed
            End Get
        End Property

    End Class
End Namespace
