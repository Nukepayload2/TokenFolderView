Namespace Internal

    ''' <summary>
    ''' Thrown when a no-track (alignment-free) <see cref="NormalizedString"/> is asked to perform
    ''' an operation that requires the per-byte alignment list it was told to skip building (e.g.
    ''' a partial-range transform such as <c>Prepend(" ")</c>, or slicing a no-track slice whose
    ''' alignment list is empty).
    '''
    ''' This is an internal control-flow signal: the offset-free
    ''' <see cref="Tokenizer.EncodeFast"/> / <see cref="Tokenizer.EncodeCount"/> fast path catches
    ''' it and re-runs the whole pipeline fully tracked, so any tokenizer configuration produces a
    ''' correct result. It inherits <see cref="InvalidOperationException"/> so existing callers
    ''' that catch that exception still behave; callers must catch this exact type and never a
    ''' broad <see cref="InvalidOperationException"/> (which would mask genuine bugs).
    ''' </summary>
    Friend NotInheritable Class OffsetTrackingRequiredException
        Inherits InvalidOperationException

        Public Sub New(message As String)
            MyBase.New(message)
        End Sub
    End Class

End Namespace
