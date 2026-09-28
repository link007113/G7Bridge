using System.Runtime.InteropServices;

namespace G7Bridge.Windows;

// Since Windows 10 2004 a process only gets fine-grained waits after requesting
// them itself; otherwise a 1-4 ms wait can take the default ~15.6 ms tick.
// Held only while an input session runs, and released with it.
internal sealed class TimerResolution : IDisposable
{
    [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint period);
    [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint period);
    private readonly uint period;
    private bool active;
    private TimerResolution(uint milliseconds){period=milliseconds;active=timeBeginPeriod(period)==0;}
    internal static TimerResolution Request(uint milliseconds)=>new(milliseconds);
    public void Dispose(){if(active){active=false;timeEndPeriod(period);}}
}
