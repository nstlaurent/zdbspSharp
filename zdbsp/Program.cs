using zdbsp;
using zdbspSharp;

ArgData argData = ParseCommandArgs(Environment.GetCommandLineArgs());

// TODO temporary
if (File.Exists(argData.Output))
    File.Delete(argData.Output);

using FileStream inStream = File.Open(argData.Input, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
using FileStream outStream = File.Open(argData.Output, FileMode.CreateNew);

using FWadReader inwad = new(inStream);
using FWadWriter outwad = new(outStream, inwad.IsIWAD());

if (argData.Options.GLOnly)
{    
    argData.Options.BuildGLNodes = true;
    argData.Options.ConformNodes = false;
}

int lump = 0;
int max = inwad.NumLumps() - 1;

while (lump < max)
{
    if (inwad.IsMap(lump) &&
        (string.IsNullOrEmpty(argData.Map) || inwad.LumpName(lump).EqualsIgnoreCase(argData.Map)))
    {
        FProcessor builder = new(inwad, lump, argData.Options);
        builder.Write(outwad);
        lump = inwad.LumpAfterMap(lump);
    }
    else if (inwad.IsGLNodes(lump))
    {
        // Ignore GL nodes from the input for any maps we process.
        if (argData.Options.BuildNodes && (string.IsNullOrEmpty(argData.Map) || inwad.LumpName(lump).Substring(3).EqualsIgnoreCase(argData.Map)))
        {
            lump = inwad.SkipGLNodes(lump);
        }
        else
        {
            outwad.CopyLump(inwad, lump);
            ++lump;
        }
    }
    else
    {
        outwad.CopyLump(inwad, lump);
        ++lump;
    }
}

static ArgData ParseCommandArgs(string[] args)
{
    ArgData argData = new();
    args = args.Skip(1).ToArray();
    for (int i =0; i < args.Length; i++)
    {
        string[] split = args[i].Split('=', StringSplitOptions.RemoveEmptyEntries);
        if (split.Length == 0)
            continue;

        string arg = split[0];
        if (ArgEquals(arg, "--gl-only"))
            argData.Options.GLOnly = true;
        else if (ArgEquals(arg, "--output"))
            argData.Output = args[++i];
        else if (ArgEquals(arg, "--map"))
        {
            if (split.Length >1)
                argData.Map = split[1];
        }
        else
            argData.Input = arg;
    }

    return argData;
}

static bool ArgEquals(string arg, string other) => arg.Equals(other, StringComparison.OrdinalIgnoreCase);