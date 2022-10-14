namespace zdbspSharp;

public sealed class ProcessorOptions
{
    public bool BuildNodes { get; set; } = true;
    public bool BuildGLNodes { get; set; } = false;
    public bool ConformNodes { get; set; } = false;
    public bool NoPrune { get; set; } = false;
    public EBlockmapMode BlockmapMode { get; set; } = EBlockmapMode.EBM_Rebuild;
    public ERejectMode RejectMode { get; set; } = ERejectMode.ERM_DontTouch;
    public bool WriteComments { get; set; } = false;
    public int MaxSegs { get; set; } = 64;
    public int SplitCost { get; set; } = 8;
    public int AAPreference { get; set; } = 16;
    public bool CheckPolyobjs { get; set; } = true;
    public bool ShowMap { get; set; } = false;
    public bool ShowWarnings { get; set; } = false;
    public bool NoTiming { get; set; } = false;
    public bool CompressNodes { get; set; } = false;
    public bool CompressGLNodes { get; set; } = false;
    public bool ForceCompression { get; set; } = false;
    public bool GLOnly { get; set; } = false;
    public bool V5GLNodes { get; set; } = false;
    public bool HaveSSE1 { get; set; }
    public bool HaveSSE2 { get; set; }
    public int SSELevel { get; set; }
}