using zdbspSharp;

namespace zdbsp
{
    public class ArgData
    {
        public ArgData()
        {
            Output = "tmp.wad";
        }

        public string Output { get; set; } = string.Empty;
        public string Input { get; set; } = string.Empty;
        public string Map { get; set; } = string.Empty;
        public ProcessorOptions Options { get; } = new ProcessorOptions();
    }
}
