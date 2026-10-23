namespace G7Bridge.Validation;

// Pure state of one observation connection; shared with the offline tests.
internal sealed class ButtonCycle
{
    private bool armed;
    public bool PressSeen { get; private set; }
    public bool Accept(bool down)
    {
        if(!armed){armed=!down;return false;}
        if(down)PressSeen=true;
        return PressSeen && !down;
    }
}
