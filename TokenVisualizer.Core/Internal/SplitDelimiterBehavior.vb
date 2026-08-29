Namespace Internal

    ''' <summary>
    ''' Defines the expected behavior for the delimiter of a Split pattern.
    ''' When splitting on '-' for example, with input "the-final--countdown":
    '''   - Removed          =&gt; [ "the", "final", "countdown" ]
    '''   - Isolated         =&gt; [ "the", "-", "final", "-", "-", "countdown" ]
    '''   - MergedWithPrevious =&gt; [ "the-", "final-", "-", "countdown" ]
    '''   - MergedWithNext   =&gt; [ "the", "-final", "-", "-countdown" ]
    '''   - Contiguous       =&gt; [ "the", "-", "final", "--", "countdown" ]
    ''' </summary>
    Public Enum SplitDelimiterBehavior
        Removed
        Isolated
        MergedWithPrevious
        MergedWithNext
        Contiguous
    End Enum

End Namespace
