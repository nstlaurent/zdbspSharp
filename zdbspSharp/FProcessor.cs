using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
namespace zdbspSharp;

public sealed class FProcessor
{
    readonly ref struct Property(ReadOnlySpan<char> name, ReadOnlySpan<char> value)
    {
        public readonly ReadOnlySpan<char> Name = name;
        public readonly ReadOnlySpan<char> Value = value;
    }

    private readonly FLevel Level = new();

	private readonly List<FPolyStart> PolyStarts = new();
	private readonly List<FPolyStart> PolyAnchors = new();

	private bool Extended;
	private readonly bool isUDMF;

	private readonly FWadReader Wad;
	private readonly int Lump;

	private readonly ProcessorOptions m_options;

	public FProcessor(FWadReader inwad, int lump, ProcessorOptions options)
	{
		Wad = inwad;
		Lump = lump;
		m_options = options;
		//printf("----%s----\n", Wad.LumpName(Lump));

		isUDMF = Wad.isUDMF(lump);

		if (isUDMF)
		{
			Extended = false;
			LoadUDMF(Lump + 1);
		}
		else
		{
			Extended = Wad.MapHasBehavior(lump);
			LoadThings();
			LoadVertices();
			LoadLines();
			LoadSides();
			LoadSectors();
		}

		if (Level.NumLines() == 0 || Level.NumVertices == 0 || Level.NumSides() == 0 || Level.NumSectors() == 0)
		{
			//printf("   Map is incomplete\n");
		}
		else
		{
			// Removing extra vertices is done by the node builder.
			Level.RemoveExtraLines();
			if (!m_options.NoPrune)
			{
				Level.RemoveExtraSides();
				Level.RemoveExtraSectors();
			}

			if (m_options.BuildNodes)
				GetPolySpots();
            Level.FindMapBounds();
		}
	}

    private void LoadUDMF(int lump)
    {
        DynamicArray<WideVertex> Vertices = new(2048);

        var text = Encoding.UTF8.GetString(Util.ReadLumpBytes(Wad, lump));
        var parser = new SimpleParser();
		parser.Parse(text);
		ParseMapProperties(parser);

        while (!parser.IsDone())
        {
			var item = parser.ConsumeStringSpan();
			parser.Consume('{');
            if (item.EqualsIgnoreCase("thing"))
            {
				Level.Things.EnsureCapacity(Level.Things.Length + 1);
                ref var th = ref Level.Things.Data[Level.Things.Length];
				ParseThing(parser, ref th);
                Level.Things.Length++;
            }
            else if (item.EqualsIgnoreCase("linedef"))
            {
				Level.Lines.EnsureCapacity(Level.Lines.Length + 1);
                ref var ld = ref Level.Lines.Data[Level.Lines.Length];
                ParseLinedef(parser, ref ld);
				Level.Lines.Length++;
            }
            else if (item.EqualsIgnoreCase("sidedef"))
            {
				Level.Sides.EnsureCapacity(Level.Sides.Length + 1);
                ref var sd = ref Level.Sides.Data[Level.Sides.Length];
                ParseSidedef(parser, ref sd);
				Level.Sides.Length++;
            }
            else if (item.EqualsIgnoreCase("sector"))
            {
				Level.Sectors.EnsureCapacity(Level.Sectors.Length + 1);
                ref var sec = ref Level.Sectors.Data[Level.Sectors.Length];
				ParseSector(parser, ref sec);
                Level.Sectors.Length++;
            }
            else if (item.EqualsIgnoreCase("vertex"))
            {
				Vertices.EnsureCapacity(Vertices.Length + 1);
                ref var vt = ref Vertices.Data[Vertices.Length];
				Level.VertexProps.EnsureCapacity(Level.VertexProps.Length + 1);
                ref var vtp = ref Level.VertexProps.Data[Level.VertexProps.Length];
                vt.index = Vertices.Length;
                Level.VertexProps.Length++;
                Vertices.Length++;
                ParseVertex(parser, ref vt, ref vtp);
            }
			parser.Consume('}');
        }
        
		Level.Vertices = new WideVertex[Vertices.Length];
        Level.NumVertices = Vertices.Length;

		for (int i = 0; i <  Vertices.Length; i++)
			Level.Vertices[i] = Vertices[i];
    }

    private void ParseMapProperties(SimpleParser parser)
    {
        parser.ConsumeString("namespace");
        parser.Consume('=');
        var ns = parser.ConsumeStringSpan();
        parser.Consume(';');
        Extended = ns.EqualsIgnoreCase("\"ZDoom\"") || ns.EqualsIgnoreCase("\"Hexen") || ns.EqualsIgnoreCase("\"Vavoom\"");
    }

    private static void ConsumeBlock(SimpleParser parser)
    {
        while (parser.PeekString() != "}")
            parser.ConsumeLineSpan();
    }

    private static void ParseThing(SimpleParser parser, ref IntThing th)
    {
        th.props ??= [];
        while (!IsBlockComplete(parser))
        {
            var prop = ParseProperty(parser);
            th.props.Add(new(prop.Name.ToString(), prop.Value.ToString()));
        }
    }

    private static void ParseSector(SimpleParser parser, ref IntSector sec)
    {
        sec.props ??= [];
        while (!IsBlockComplete(parser))
        {
            var prop = ParseProperty(parser);
            sec.props.Add(new(prop.Name.ToString(), prop.Value.ToString()));
        }
    }

    private static void ParseSidedef(SimpleParser parser, ref IntSideDef sd)
    {
        sd.sector = Constants.MAX_INT;
        while (!IsBlockComplete(parser))
        {
            var prop = ParseProperty(parser);
            if (prop.Name.EqualsIgnoreCase("sector"))
            {
                sd.sector = parser.ParseInt(prop.Value);
				continue;
            }

            sd.props ??= [];
            sd.props.Add(new(prop.Name.ToString(), prop.Value.ToString()));
        }
    }

    private static void ParseVertex(SimpleParser parser, ref WideVertex vt, ref IntVertex vtp)
    {
		vtp.props = [];
        while (!IsBlockComplete(parser))
        {
            var prop = ParseProperty(parser);
            if (prop.Name.EqualsIgnoreCase("x"))
            {
                vt.x = (int)(parser.ParseDouble(prop.Value) * (1 << 16));
            }
            else if (prop.Name.EqualsIgnoreCase("y"))
            {
                vt.y = (int)(parser.ParseDouble(prop.Value) * (1 << 16));
            }

			vtp.props.Add(new(prop.Name.ToString(), prop.Value.ToString()));
        }
    }

    private unsafe void ParseLinedef(SimpleParser parser, ref IntLineDef ld)
    {
        ld.v1 = ld.v2 = ld.sidenum[0] = ld.sidenum[1] = Constants.MAX_INT;
        ld.special = 0;
        while (!IsBlockComplete(parser))
        {
			var prop = ParseProperty(parser);
            if (prop.Name.EqualsIgnoreCase("v1"))
            {
                ld.v1 = parser.ParseInt(prop.Value);
                continue;   // do not store in props
            }
            else if (prop.Name.EqualsIgnoreCase("v2"))
            {
				ld.v2 = parser.ParseInt(prop.Value);
                continue;   // do not store in props
            }
            else if (Extended && prop.Name.EqualsIgnoreCase("special"))
            {
                ld.special = parser.ParseInt(prop.Value);
            }
            else if (Extended && prop.Name.EqualsIgnoreCase("arg0"))
            {
                ld.args[0] = parser.ParseInt(prop.Value);
            }
            if (prop.Name.EqualsIgnoreCase("sidefront"))
            {
                ld.sidenum[0] = parser.ParseInt(prop.Value);
                continue;   // do not store in props
            }
            else if (prop.Name.EqualsIgnoreCase("sideback"))
            {
                ld.sidenum[1] = parser.ParseInt(prop.Value);
                continue;   // do not store in props
            }

			ld.props ??= [];
			ld.props.Add(new(prop.Name.ToString(), prop.Value.ToString()));
		}
    }

    private static Property ParseProperty(SimpleParser parser)
    {
        var type = parser.ConsumeStringSpan();
        parser.Consume('=');
        var value = parser.ConsumeStringSpan();
        parser.Consume(';');
        return new(type, value);
    }

    private static bool IsBlockComplete(SimpleParser parser) => parser.Peek('}');

    public void Write(FWadWriter writer)
    {
        if (Level.NumLines() == 0 || Level.NumSides() == 0 || Level.NumSectors() == 0 || Level.NumVertices == 0)
        {
            WriteAll(writer);
            return;
        }

        if (m_options.BuildNodes)
            BuildNodes();

        if (!isUDMF)
            BuildBlockMapAndReject();

        WriteWadData(writer);
    }

    private void WriteWadData(FWadWriter writer)
    {
        bool compress;
        bool compressGL;
        bool gl5 = false;

        if (Level.GLNodes != null)
        {
            gl5 = m_options.V5GLNodes || (Level.GLVertices.Length > 32767) || (Level.GLSegs.Length > 65534) || (Level.GLNodes.Length > 32767) || (Level.GLSubsectors.Length > 32767);
            // Not supported
            //compressGL = Globals.CompressGLNodes || (Level.NumVertices > 32767);
            compressGL = false;
        }
        else
        {
            compressGL = false;
        }

        if (isUDMF)
        {
            writer.CopyLump(Wad, Lump);
			WriteUDMF(writer);
			WriteGLData(writer, compressGL, gl5);
            return;
        }

        // If the GL nodes are compressed, then the regular nodes must also be compressed.
        compress = m_options.CompressNodes || compressGL || (Level.NumVertices > 65535) || (Level.Segs.Length > 65535) || (Level.Subsectors.Length > 32767) || (Level.Nodes.Length > 32767);

        writer.CopyLump(Wad, Lump);
        writer.CopyLump(Wad, Wad.FindMapLump("THINGS", Lump));
        WriteLines(writer);
        WriteSides(writer);
        WriteVertices(writer, compress || m_options.GLOnly ? Level.NumOrgVerts : Level.NumVertices);

        if (m_options.BuildNodes)
        {
            if (!compress)
            {
                if (!m_options.GLOnly)
                {
                    WriteSegs(writer);
                    WriteSSectors(writer);
                    WriteNodes(writer);
                }
                else
                {
                    writer.CreateLabel("SEGS");
                    writer.CreateLabel("SSECTORS");
                    writer.CreateLabel("NODES");
                }
            }
            else
            {
                writer.CreateLabel("SEGS");
                if (compressGL)
                {
                    if (m_options.ForceCompression)
                        WriteGLBSPZ(writer, "SSECTORS");
                    else
                        WriteGLBSPX(writer, "SSECTORS");
                }
                else
                {
                    writer.CreateLabel("SSECTORS");
                }
                if (!m_options.GLOnly)
                {
                    if (m_options.ForceCompression)
                        WriteBSPZ(writer, "NODES");
                    else
                        WriteBSPX(writer, "NODES");
                }
                else
                {
                    writer.CreateLabel("NODES");
                }
            }
        }
        else
        {
            writer.CopyLump(Wad, Wad.FindMapLump("SEGS", Lump));
            writer.CopyLump(Wad, Wad.FindMapLump("SSECTORS", Lump));
            writer.CopyLump(Wad, Wad.FindMapLump("NODES", Lump));
        }

        WriteSectors(writer);
        WriteReject(writer);
        WriteBlockmap(writer);

        if (Extended)
        {
            writer.CopyLump(Wad, Wad.FindMapLump("BEHAVIOR", Lump));
            writer.CopyLump(Wad, Wad.FindMapLump("SCRIPTS", Lump));
        }

        WriteGLData(writer, compressGL, gl5);
    }

    private void WriteUDMF(FWadWriter writer)
    {
		writer.StartWritingLump("TEXTMAP");
		// TODO actually write namespace
		writer.AddToLump("namespace = zdoom;");

		for (int i = 0; i < Level.Things.Length; i++)
			WriteThingUDMF(writer, ref Level.Things.Data[i]);

		for (int i = 0; i < Level.NumOrgVerts; i++)
		{
			ref var vt = ref Level.Vertices[i];
			WriteVertexUDMF(writer, ref Level.VertexProps.Data[vt.index], i);
		}

		for (int i = 0; i < Level.Lines.Length; i++)
			WriteLineUDMF(writer, ref Level.Lines.Data[i]);

		for (int i = 0; i < Level.Sides.Length; i++)
			WriteSideUDMF(writer, ref Level.Sides.Data[i]);

        for (int i = 0; i < Level.Sectors.Length; i++)
            WriteSectorUDMF(writer, ref Level.Sectors.Data[i]);
    }


    private static readonly byte[] StartBrace = Encoding.UTF8.GetBytes("\n{\n");
    private static readonly byte[] EndBrace = Encoding.UTF8.GetBytes("\n}\n");
	private static readonly byte[] EqualsBytes = Encoding.UTF8.GetBytes(" = ");
    private static readonly byte[] Semicolon = Encoding.UTF8.GetBytes(";\n");
	private static readonly byte[] ThingBytes = Encoding.UTF8.GetBytes("thing");
    private static readonly byte[] VertexBytes = Encoding.UTF8.GetBytes("vertex");
    private static readonly byte[] LinedefBytes = Encoding.UTF8.GetBytes("linedef");
    private static readonly byte[] SidedefBytes = Encoding.UTF8.GetBytes("sidedef");
    private static readonly byte[] SectorBytes = Encoding.UTF8.GetBytes("sector");


    private static void WriteSectorUDMF(FWadWriter writer, ref IntSector s)
    {
        writer.AddToLump(SectorBytes);
        writer.AddToLump(StartBrace);
        WriteProps(writer, s.props);
        writer.AddToLump(EndBrace);
    }

    private static void WriteSideUDMF(FWadWriter writer, ref IntSideDef sd)
    {
        writer.AddToLump(SidedefBytes);
        writer.AddToLump(StartBrace);
        WriteIntProp(writer, "sector", sd.sector);
        WriteProps(writer, sd.props);
        writer.AddToLump(EndBrace);
    }

    private unsafe void WriteLineUDMF(FWadWriter writer, ref IntLineDef ld)
    {
        writer.AddToLump(LinedefBytes);
        writer.AddToLump(StartBrace);
        WriteIntProp(writer, "v1", ld.v1);
        WriteIntProp(writer, "v2", ld.v2);
		if (ld.sidenum[0] != Constants.MAX_INT)
			WriteIntProp(writer, "sidefront", ld.sidenum[0]);
        if (ld.sidenum[1] != Constants.MAX_INT)
            WriteIntProp(writer, "sideback", ld.sidenum[1]);
        WriteProps(writer, ld.props);
        writer.AddToLump(EndBrace);
    }

    private static void WriteVertexUDMF(FWadWriter writer, ref IntVertex v, int num)
    {
        writer.AddToLump(VertexBytes);
        writer.AddToLump(StartBrace);
        WriteProps(writer, v.props);
        writer.AddToLump(EndBrace);
    }

    private static void WriteThingUDMF(FWadWriter writer, ref IntThing th)
    {
		writer.AddToLump(ThingBytes);
		writer.AddToLump(StartBrace);
        WriteProps(writer, th.props);
        writer.AddToLump(EndBrace);
    }

    private static void WriteProps(FWadWriter writer, List<UDMFKey>? props)
    {
		if (props == null)
			return;

		for (int i = 0; i < props.Count; i++)
		{
			writer.AddToLump(props[i].key);
			writer.AddToLump(EqualsBytes);
			writer.AddToLump(props[i].value);
            writer.AddToLump(Semicolon);
        }
    }
    private static void WriteIntProp(FWadWriter writer, string name, int value)
    {
        writer.AddToLump(name);
        writer.AddToLump(EqualsBytes);
        writer.AddToLump(value.ToString());
        writer.AddToLump(Semicolon);
    }

    private void WriteGLData(FWadWriter writer, bool compressGL, bool gl5)
    {
        if (Level.GLNodes != null && !compressGL)
        {
            string glname = "GL_" + Wad.LumpName(Lump);
            writer.CreateLabel(glname);
            WriteGLVertices(writer, gl5);
            WriteGLSegs(writer, gl5);
            WriteGLSSect(writer, gl5);
            WriteGLNodes(writer, gl5);
        }
    }

    private void BuildBlockMapAndReject()
    {
		FBlockmapBuilder bbuilder = new(Level);
		DynamicArray<ushort> blocks = bbuilder.GetBlockmap();
		Level.Blockmap = new ushort[blocks.Length];
		for (int i = 0; i < Level.Blockmap.Length; i++)
			Level.Blockmap[i] = blocks[i];

		Level.RejectSize = (Level.NumSectors() * Level.NumSectors() + 7) / 8;
		Level.Reject = null;

		switch (m_options.RejectMode)
		{
			case ERejectMode.ERM_Rebuild:
			//FRejectBuilder reject(Level);
			//Level.Reject = reject.GetReject();
			//printf("   Rebuilding the reject is unsupported.\n");
			// Intentional fall-through

			case ERejectMode.ERM_DontTouch:
				{
					int lump = Wad.FindMapLump("REJECT", Lump);

					if (lump >= 0)
					{
                        Level.Reject = Util.ReadLumpBytes(Wad, lump);
						Level.RejectSize = Level.Reject.Length;

                        if (Level.RejectSize != (Level.NumOrgSectors * Level.NumOrgSectors + 7) / 8)
						{
							// If the reject is the wrong size, don't use it.
							Level.Reject = null;
							Level.Reject = null;
							if (Level.RejectSize != 0)
							{ // Do not warn about 0-length rejects
								//printf("   REJECT is the wrong size, so it will be removed.\n");
							}
							Level.RejectSize = 0;
						}
						else if (Level.NumOrgSectors != Level.NumSectors())
						{
							// Some sectors have been removed, so fix the reject.
							byte[] newreject = FixReject(Level.Reject);
							Level.Reject = null;
							Level.Reject = newreject;
							Level.RejectSize = (Level.NumSectors() * Level.NumSectors() + 7) / 8;
						}
					}
				}
				break;

			case ERejectMode.ERM_Create0:
				break;

			case ERejectMode.ERM_CreateZeroes:
				Level.Reject = new byte[Level.RejectSize];
				break;
		}
	}

	private void WriteAll(FWadWriter writer)
    {
        if (!isUDMF)
        {
            // Map is empty, so just copy it as-is
            writer.CopyLump(Wad, Lump);
            writer.CopyLump(Wad, Wad.FindMapLump("THINGS", Lump));
            writer.CopyLump(Wad, Wad.FindMapLump("LINEDEFS", Lump));
            writer.CopyLump(Wad, Wad.FindMapLump("SIDEDEFS", Lump));
            writer.CopyLump(Wad, Wad.FindMapLump("VERTEXES", Lump));
            writer.CreateLabel("SEGS");
            writer.CreateLabel("SSECTORS");
            writer.CreateLabel("NODES");
            writer.CopyLump(Wad, Wad.FindMapLump("SECTORS", Lump));
            writer.CreateLabel("REJECT");
			writer.CreateLabel("BLOCKMAP");
            if (Extended)
            {
				writer.CopyLump(Wad, Wad.FindMapLump("BEHAVIOR", Lump));
				writer.CopyLump(Wad, Wad.FindMapLump("SCRIPTS", Lump));
            }
        }
        else
        {
            for (int i = Lump; Wad.LumpName(i).EqualsIgnoreCase("ENDMAP") && i < Wad.NumLumps(); i++)
				writer.CopyLump(Wad, i);
			writer.CreateLabel("ENDMAP");
        }
    }

    private void BuildNodes()
    {
        // ZDoom's UDMF spec requires compressed GL nodes.
        // No other UDMF spec has defined anything regarding nodes yet.
        if (isUDMF)
        {
            m_options.BuildGLNodes = true;
            m_options.ConformNodes = false;
            m_options.GLOnly = true;
			m_options.CompressGLNodes = true;
        }

        if (m_options.HaveSSE2)
			m_options.SSELevel = 2;
        else if (m_options.HaveSSE1)
			m_options.SSELevel = 1;
        else
			m_options.SSELevel = 0;

        FNodeBuilder builder = new(Level, PolyStarts, PolyAnchors, Wad.LumpName(Lump), m_options.BuildGLNodes, m_options.MaxSegs, m_options.SplitCost, m_options.AAPreference);
        if (builder == null)
            throw new System.Exception("   Not enough memory to build nodes!");

        Level.Vertices = builder.GetVertices();

        if (m_options.ConformNodes)
        {
            // When the nodes are "conformed", the normal and GL nodes use the same
            // basic information. This creates normal nodes that are less "good" than
            // possible, but it makes it easier to compare the two sets of nodes to
            // determine the correctness of the GL nodes.
            builder.GetNodes(out MapNodeEx[] nodes, out MapSegEx[] segs, out MapSubsectorEx[] subs);
            Level.Nodes = nodes;
            Level.Segs = segs;
            Level.Subsectors = subs;
            Level.Vertices = builder.GetVertices();

            builder.GetGLNodes(out MapNodeEx[] glnodes, out MapSegGLEx[] glsegs, out MapSubsectorEx[] glsubs);
            Level.GLNodes = glnodes;
            Level.GLSegs = glsegs;
            Level.GLSubsectors = glsubs;
        }
        else
        {
            if (m_options.BuildGLNodes)
            {
                Level.GLVertices = builder.GetVertices();
                builder.GetGLNodes(out MapNodeEx[] glnodes, out MapSegGLEx[] glsegs, out MapSubsectorEx[] glsubs);
                Level.GLNodes = glnodes;
                Level.GLSegs = glsegs;
                Level.GLSubsectors = glsubs;

                if (!m_options.GLOnly)
                {
                    // Now repeat the process to obtain regular nodes
                    if (builder != null)
                        builder.Dispose();
                    builder = new FNodeBuilder(Level, PolyStarts, PolyAnchors, Wad.LumpName(Lump), false, m_options.MaxSegs, m_options.SplitCost, m_options.AAPreference);
                    if (builder == null)
                        throw new System.Exception("   Not enough memory to build regular nodes!");
                    Level.Vertices = builder.GetVertices();
                }
            }

            if (!m_options.GLOnly)
            {
                builder.GetNodes(out MapNodeEx[] nodes, out MapSegEx[] segs, out MapSubsectorEx[] subs);
                Level.Nodes = nodes;
                Level.Segs = segs;
                Level.Subsectors = subs;
            }
        }

		builder.Dispose();
    }

    private unsafe void LoadThings()
	{
		if (Extended)
		{
			MapThing2[] Things = Util.ReadMapLump<MapThing2>(Wad, "THINGS", Lump);
			for (int i = 0; i < Things.Length; ++i)
			{
				IntThing thing = new IntThing();
				thing.thingid = Things[i].thingid;
				thing.x = Things[i].x << Constants.FRACBITS;
				thing.y = Things[i].y << Constants.FRACBITS;
				thing.z = Things[i].z;
				thing.angle = Things[i].angle;
				thing.type = Things[i].type;
				thing.flags = Things[i].flags;
				thing.special = Things[i].special;
				thing.args[0] = Things[i].args[0];
				thing.args[1] = Things[i].args[1];
				thing.args[2] = Things[i].args[2];
				thing.args[3] = Things[i].args[3];
				thing.args[4] = Things[i].args[4];
				Level.Things.Add(thing);
			}
		}
		else
		{
			MapThing[] mt = Util.ReadMapLump<MapThing>(Wad, "THINGS", Lump);
			for (int i = 0; i < mt.Length; ++i)
			{
				IntThing thing = new IntThing();
				thing.x = mt[i].x << Constants.FRACBITS;
				thing.y = mt[i].y << Constants.FRACBITS;
				thing.angle = mt[i].angle;
				thing.type = mt[i].type;
				thing.flags = mt[i].flags;
				thing.z = 0;
				thing.special = 0;
				thing.args[0] = 0;
				thing.args[1] = 0;
				thing.args[2] = 0;
				thing.args[3] = 0;
				thing.args[4] = 0;
				Level.Things.Add(thing);
			}
		}
	}

	private unsafe void LoadLines()
	{
		if (Extended)
		{
			MapLineDef2[] Lines = Util.ReadMapLump<MapLineDef2>(Wad, "LINEDEFS", Lump);
			for (int i = 0; i < Lines.Length; ++i)
			{
				IntLineDef line = new IntLineDef();
				line.special = Lines[i].special;
				line.args[0] = Lines[i].args[0];
				line.args[1] = Lines[i].args[1];
				line.args[2] = Lines[i].args[2];
				line.args[3] = Lines[i].args[3];
				line.args[4] = Lines[i].args[4];
				line.v1 = Lines[i].v1;
				line.v2 = Lines[i].v2;
				line.flags = Lines[i].flags;
				line.sidenum[0] = Lines[i].sidenum[0];
				line.sidenum[1] = Lines[i].sidenum[1];
				if (line.sidenum[0] == Constants.NO_MAP_INDEX)
					line.sidenum[0] = Constants.MAX_INT;
				if (line.sidenum[1] == Constants.NO_MAP_INDEX)
					line.sidenum[1] = Constants.MAX_INT;
				Level.Lines.Add(line);
			}
		}
		else
		{
			MapLineDef[] ml = Util.ReadMapLump<MapLineDef>(Wad, "LINEDEFS", Lump);
			for (int i = 0; i < ml.Length; ++i)
			{
				IntLineDef line = new IntLineDef();
				line.v1 = ml[i].v1;
				line.v2 = ml[i].v2;
				line.flags = ml[i].flags;
				line.sidenum[0] = ml[i].sidenum[0];
				line.sidenum[1] = ml[i].sidenum[1];
				if (line.sidenum[0] == Constants.NO_MAP_INDEX)
					line.sidenum[0] = Constants.MAX_INT;
				if (line.sidenum[1] == Constants.NO_MAP_INDEX)
					line.sidenum[1] = Constants.MAX_INT;

				// Store the special and tag in the args array so we don't lose them
				line.special = 0;
				line.args[0] = ml[i].special;
				line.args[1] = ml[i].tag;
				Level.Lines.Add(line);
			}
		}
	}

	private unsafe void LoadVertices()
	{
		MapVertex[] verts = Util.ReadMapLump<MapVertex>(Wad, "VERTEXES", Lump);

		Level.NumVertices = verts.Length;
		Level.Vertices = new WideVertex[Level.NumVertices];

		for (int i = 0; i < Level.NumVertices; ++i)
		{
			fixed (WideVertex* setVertex = &Level.Vertices[i])
			{
				fixed (MapVertex* vertex = &verts[i])
				{
					setVertex->x = vertex->x << Constants.FRACBITS;
					setVertex->y = vertex->y << Constants.FRACBITS;
					setVertex->index = 0; // we don't need this value for non-UDMF maps
				}
			}
		}
	}

	private unsafe void LoadSides()
	{
		MapSideDef[] Sides = Util.ReadMapLump<MapSideDef>(Wad, "SIDEDEFS", Lump);
		Level.Sides.Resize(Sides.Length);
		for (int i = 0; i < Sides.Length; ++i)
		{
			fixed (IntSideDef* newSide = &Level.Sides.Data[i])
			{
				fixed (MapSideDef* side = &Sides[i])
				{
					newSide->textureoffset = Sides[i].textureoffset;
					newSide->rowoffset = Sides[i].rowoffset;
					Util.ByteCopy(newSide->toptexture, side->toptexture, 8);
					Util.ByteCopy(newSide->bottomtexture, side->bottomtexture, 8);
					Util.ByteCopy(newSide->midtexture, side->midtexture, 8);

					newSide->sector = side->sector;
					if (newSide->sector == Constants.NO_MAP_INDEX)
						newSide->sector = Constants.MAX_INT;
				}
			}
		}
	}

	private unsafe void LoadSectors()
	{
		MapSector[] Sectors =Util.ReadMapLump<MapSector>(Wad, "SECTORS", Lump);
		Level.Sectors.Resize(Sectors.Length);

		for (int i = 0; i < Sectors.Length; ++i)
		{
			fixed (IntSector* setSector = &Level.Sectors.Data[i])
			{
				fixed (MapSector* sector = &Sectors[i])
                {
					setSector->ceilingheight = sector->ceilingheight;
					setSector->floorheight = sector->floorheight;
					setSector->lightlevel = sector->lightlevel;
					setSector->special = sector->special;
					setSector->tag = sector->tag;
					Util.ByteCopy(setSector->ceilingpic, sector->ceilingpic, 8);
					Util.ByteCopy(setSector->floorpic, sector->floorpic, 8);
				}
			}
		}
	}

	private void GetPolySpots()
	{
		//if (Extended && CheckPolyobjs)
		//{
		//	int spot1;
		//	int spot2;
		//	int anchor;
		//	int i;

		//	// Determine if this is a Hexen map by looking for things of type 3000
		//	// Only Hexen maps use them, and they are the polyobject anchors
		//	for (i = 0; i < Level.NumThings(); ++i)
		//	{
		//		if (Level.Things[i].type == Globals.PO_HEX_ANCHOR_TYPE)
		//		{
		//			break;
		//		}
		//	}

		//	if (i < Level.NumThings())
		//	{
		//		spot1 = Globals.PO_HEX_SPAWN_TYPE;
		//		spot2 = Globals.PO_HEX_SPAWNCRUSH_TYPE;
		//		anchor = Globals.PO_HEX_ANCHOR_TYPE;
		//	}
		//	else
		//	{
		//		spot1 = Globals.PO_SPAWN_TYPE;
		//		spot2 = Globals.PO_SPAWNCRUSH_TYPE;
		//		anchor = Globals.PO_ANCHOR_TYPE;
		//	}

		//	for (i = 0; i < Level.NumThings(); ++i)
		//	{
		//		if (Level.Things[i].type == spot1 || Level.Things[i].type == spot2 || Level.Things[i].type == Globals.PO_SPAWNHURT_TYPE || Level.Things[i].type == anchor)
		//		{
		//			FNodeBuilder.FPolyStart newvert = new FNodeBuilder.FPolyStart();
		//			newvert.x = Level.Things[i].x;
		//			newvert.y = Level.Things[i].y;
		//			newvert.polynum = Level.Things[i].angle;
		//			if (Level.Things[i].type == anchor)
		//			{
		//				PolyAnchors.Push(newvert);
		//			}
		//			else
		//			{
		//				PolyStarts.Push(newvert);
		//			}
		//		}
		//	}
		//}
	}

	private unsafe MapNodeEx[] NodesToEx(MapNode[] nodes, int count)
	{
		if (count == 0)
			return Array.Empty<MapNodeEx>();

		MapNodeEx[] Nodes = new MapNodeEx[Level.Nodes.Length];
		for (int x = 0; x < nodes.Length; ++x)
		{
			ushort child;

			Nodes[x].x = nodes[x].x;
			Nodes[x].y = nodes[x].y;
			Nodes[x].dx = nodes[x].dx;
			Nodes[x].dy = nodes[x].dy;

			for (int i = 0; i < 2 * 4; i++)
				Nodes[x].bbox[i] = nodes[x].bbox[i];

			for (int i = 0; i < 2; ++i)
			{
				child = nodes[x].children[i];
				if ((child & Constants.NF_SUBSECTOR) != 0)
					Nodes[x].children[i] = child + (Constants.NFX_SUBSECTOR - Constants.NF_SUBSECTOR);
				else
					Nodes[x].children[i] = child;
			}
		}
		return Nodes;
	}

	private MapSubsectorEx[] SubsectorsToEx(MapSubsector[] ssec, int count)
	{
		if (count == 0)
			return Array.Empty<MapSubsectorEx>();

		MapSubsectorEx[] data = new MapSubsectorEx[Level.Subsectors.Length];
		for (int x = 0; x < count; ++x)
		{
			data[x].numlines = (ssec[x].numlines);
			data[x].firstline = ssec[x].firstline;
		}

		return data;
	}

	private MapSegGLEx[] SegGLsToEx(MapSegGL[] segs, int count)
	{
		if (count == 0)
			return Array.Empty<MapSegGLEx>();

		MapSegGLEx[] data = new MapSegGLEx[count];
		for (int x = 0; x < count; ++x)
		{
			data[x].v1 = segs[x].v1;
			data[x].v2 = segs[x].v2;
			data[x].linedef = segs[x].linedef;
			data[x].side = segs[x].side;
			data[x].partner = segs[x].partner;
		}

		return data;
	}

	private byte[] FixReject(byte[] oldreject)
	{
		int rejectSize = (Level.NumSectors() * Level.NumSectors() + 7) / 8;
		byte[] newreject = new byte[rejectSize];

		for (int y = 0; y < Level.NumSectors(); ++y)
		{
			int oy = (int)Level.OrgSectorMap[y];
			for (int x = 0; x < Level.NumSectors(); ++x)
			{
				int ox = (int)Level.OrgSectorMap[x];
				int pnum = y * Level.NumSectors() + x;
				int opnum = oy * Level.NumOrgSectors + ox;

                newreject[pnum >> 3] |= (byte)(oldreject[opnum >> 3] & (1 << (opnum & 7)));
            }
		}
		return newreject;
	}

	private bool CheckForFracSplitters(MapNodeEx[] nodes)
	{
		for (int i = 0; i < nodes.Length; ++i)
			if (0 != ((nodes[i].x | nodes[i].y | nodes[i].dx | nodes[i].dy) & 0x0000FFFF))
				return true;
		return false;
	}

	private unsafe void WriteLines(FWadWriter writer)
	{
		if (Extended)
		{
			MapLineDef2[] Lines = new MapLineDef2[Level.NumLines()];
			for (int i = 0; i < Level.NumLines(); ++i)
			{
				fixed (MapLineDef2* setLine = &Lines[i])
				{
					fixed (IntLineDef* line = &Level.Lines.Data[i])
					{
						setLine->special = (byte)line->special;
						setLine->args[0] = (byte)line->args[0];
						setLine->args[1] = (byte)line->args[1];
						setLine->args[2] = (byte)line->args[2];
						setLine->args[3] = (byte)line->args[3];
						setLine->args[4] = (byte)line->args[4];
						setLine->v1 = (ushort)(line->v1);
						setLine->v2 = (ushort)(line->v2);
						setLine->flags = (short)(ushort)(line->flags);
						setLine->sidenum[0] = (ushort)(line->sidenum[0]);
						setLine->sidenum[1] = (ushort)(line->sidenum[1]);
					}
				}
			}
			byte[] data = Util.StructArrayToBytes(Lines);
			writer.WriteLump("LINEDEFS", data, data.Length);
		}
		else
		{
			MapLineDef[] ld = new MapLineDef[Level.NumLines()];
			for (int i = 0; i < Level.NumLines(); ++i)
			{
				fixed (MapLineDef* setLine = &ld[i])
				{
					fixed (IntLineDef* line = &Level.Lines.Data[i])
					{
						setLine->v1 = (ushort)line->v1;
						setLine->v2 = (ushort)line->v2;
						setLine->flags = (short)line->flags;
						setLine->sidenum[0] = (ushort)line->sidenum[0];
						setLine->sidenum[1] = (ushort)line->sidenum[1];
						setLine->special = (short)line->args[0];
						setLine->tag = (short)line->args[1];
					}
				}
			}
			byte[] data = Util.StructArrayToBytes(ld);
			writer.WriteLump("LINEDEFS", data, data.Length);
		}
	}

	private void WriteVertices(FWadWriter writer, int count)
	{
		WideVertex[] vertdata = Level.Vertices;
		short[] verts = new short[count * 2];

		for (int i = 0; i < count; ++i)
		{
			verts[i * 2] = (short)(vertdata[i].x >> Constants.FRACBITS);
			verts[i * 2 + 1] = (short)(vertdata[i].y >> Constants.FRACBITS);
		}

		byte[] data = Util.StructArrayToBytes(verts);
		writer.WriteLump("VERTEXES", data, data.Length);

		//if (count >= 32768)
			//printf("   VERTEXES is past the normal limit. (%d vertices)\n", count);
	}

	private unsafe void WriteSectors(FWadWriter writer)
	{
		MapSector[] Sectors = new MapSector[Level.NumSectors()];
		for (int i = 0; i < Level.NumSectors(); ++i)
		{
			fixed (IntSector* sector = &Level.Sectors.Data[i])
			{
				fixed (MapSector* setSector = &Sectors[i])
				{
					setSector->ceilingheight = sector->ceilingheight;
					setSector->floorheight = sector->floorheight;
					setSector->lightlevel = sector->lightlevel;
					setSector->special = sector->special;
					setSector->tag = sector->tag;
					Util.ByteCopy(setSector->ceilingpic, sector->ceilingpic, 8);
					Util.ByteCopy(setSector->floorpic, sector->floorpic, 8);
				}
			}
		}

		byte[] data = Util.StructArrayToBytes(Sectors);
		writer.WriteLump("SECTORS", data, data.Length);
	}

	private unsafe void WriteSides(FWadWriter writer)
	{
		MapSideDef[] Sides = new MapSideDef[Level.NumSides()];

		for (int i = 0; i < Level.NumSides(); ++i)
		{
			fixed (MapSideDef* setSide = &Sides[i])
			{
				fixed (IntSideDef* side = &Level.Sides.Data[i])
				{
					setSide->textureoffset = Level.Sides[i].textureoffset;
					setSide->rowoffset = Level.Sides[i].rowoffset;
					Util.ByteCopy(setSide->toptexture, side->toptexture, 8);
					Util.ByteCopy(setSide->bottomtexture, side->bottomtexture, 8);
					Util.ByteCopy(setSide->midtexture, side->midtexture, 8);
					setSide->sector = (ushort)side->sector;
				}
			}
		}

		byte[] data = Util.StructArrayToBytes(Sides);
		writer.WriteLump("SIDEDEFS", data, data.Length);
	}

	private void WriteSegs(FWadWriter writer)
	{
		Debug.Assert(Level.NumVertices < 65536);
		MapSeg[] segdata = new MapSeg[Level.Segs.Length];

		for (int i = 0; i < Level.Segs.Length; ++i)
		{
			segdata[i].v1 = (ushort)Level.Segs[i].v1;
			segdata[i].v2 = (ushort)Level.Segs[i].v2;
			segdata[i].angle = Level.Segs[i].angle;
			segdata[i].linedef = Level.Segs[i].linedef;
			segdata[i].side = Level.Segs[i].side;
			segdata[i].offset = Level.Segs[i].offset;
		}

		byte[] data = Util.StructArrayToBytes(segdata);
		writer.WriteLump("SEGS", data, data.Length);

		//if (Level.NumSegs >= 65536)
			//printf("   SEGS is too big for any port. (%d segs)\n", Level.NumSegs);
		//else if (Level.NumSegs >= 32768)
			//printf("   SEGS is too big for vanilla Doom and some ports. (%d segs)\n", Level.NumSegs);
	}

	private void WriteSSectors(FWadWriter writer)
	{
		WriteSSectors2(writer, "SSECTORS", Level.Subsectors);
	}

	private void WriteNodes(FWadWriter writer)
	{
		WriteNodes2(writer, "NODES", Level.Nodes);
	}

	private void WriteBlockmap(FWadWriter writer)
	{
		if (m_options.BlockmapMode == EBlockmapMode.EBM_Create0)
		{
			writer.CreateLabel("BLOCKMAP");
			return;
		}

		int count = Level.Blockmap.Length;
		ushort[] blocks = Level.Blockmap;
		for (int i = 0; i < count; ++i)
			blocks[i] = blocks[i];

		byte[] data = Util.StructArrayToBytes(blocks);
		writer.WriteLump("BLOCKMAP", data, data.Length);

		for (int i = 0; i < count; ++i)
			blocks[i] = blocks[i];

		if (count >= 65536)
		{
			//printf("   BLOCKMAP is so big that ports will have to recreate it.\n" + "   Vanilla Doom cannot handle it at all. If this map is for ZDoom 2+,\n" + "   you should use the -b switch to save space in the wad.\n");
		}
		else if (count >= 32768)
		{
			//printf("   BLOCKMAP is too big for vanilla Doom.\n");
		}
	}

	private void WriteReject(FWadWriter writer)
	{
		if (m_options.RejectMode == ERejectMode.ERM_Create0 || Level.Reject == null)
			writer.CreateLabel("REJECT");
		else if (Level.Reject != null)
			writer.WriteLump("REJECT", Level.Reject, Level.RejectSize);
	}

	private void WriteGLVertices(FWadWriter writer, bool v5)
	{		
		int count = (Level.GLVertices.Length - Level.NumOrgVerts);
		WideVertex[] vertdata = Level.GLVertices;

		int[] verts = new int[count * 2 + 1];
		for (int i = 0; i < count; ++i)
		{
			verts[i * 2 + 1] = vertdata[Level.NumOrgVerts + i].x;
			verts[i * 2 + 2] = vertdata[Level.NumOrgVerts + i].y;
		}

		byte[] data = Util.StructArrayToBytes(verts);
		data[0] = (byte)'g';
		data[1] = (byte)'N';
		data[2] = (byte)'d';
		data[3] = (byte)(v5 ? '5' : '2');

		writer.WriteLump("GL_VERT", data,  data.Length);

		if (count > 65536)
		{
			//printf("   GL_VERT is too big. (%d GL vertices)\n", count / 2);
		}
	}

	private void WriteGLSegs(FWadWriter writer, bool v5)
	{
		if (v5)
		{
			WriteGLSegs5(writer);
			return;
		}

		int count = Level.GLSegs.Length;
		MapSegGL[] segdata = new MapSegGL[count];

		for (int i = 0; i < count; ++i)
		{
			if (Level.GLSegs[i].v1 < (uint)Level.NumOrgVerts)
				segdata[i].v1 = (ushort)Level.GLSegs[i].v1;
			else
				segdata[i].v1 = (ushort)(0x8000 | (ushort)(Level.GLSegs[i].v1 - Level.NumOrgVerts));

			if (Level.GLSegs[i].v2 < (uint)Level.NumOrgVerts)
				segdata[i].v2 = (ushort)Level.GLSegs[i].v2;
			else
				segdata[i].v2 = (ushort)(0x8000 | (ushort)(Level.GLSegs[i].v2 - Level.NumOrgVerts));

			segdata[i].linedef = (ushort)Level.GLSegs[i].linedef;
			segdata[i].side = Level.GLSegs[i].side;
			segdata[i].partner = (ushort)Level.GLSegs[i].partner;
		}
		byte[] data = Util.StructArrayToBytes(segdata);
		writer.WriteLump("GL_SEGS", data, data.Length);

		if (count >= 65536)
		{
			//printf("   GL_SEGS is too big for any port. (%d GL segs)\n", count);
		}
		else if (count >= 32768)
		{
			//printf("   GL_SEGS is too big for some ports. (%d GL segs)\n", count);
		}
	}

	private void WriteGLSegs5(FWadWriter writer)
	{
		int count = Level.GLSegs.Length;
		MapSegGLEx[]  segdata = new MapSegGLEx[count];

		for (int i = 0; i < count; ++i)
		{
			if (Level.GLSegs[i].v1 < (uint)Level.NumOrgVerts)
				segdata[i].v1 = Level.GLSegs[i].v1;
			else
				segdata[i].v1 = (int)(0x80000000u | Level.GLSegs[i].v1 - Level.NumOrgVerts);

			if (Level.GLSegs[i].v2 < (uint)Level.NumOrgVerts)
				segdata[i].v2 = Level.GLSegs[i].v2;
			else
				segdata[i].v2 = (int)(0x80000000u | Level.GLSegs[i].v2 - Level.NumOrgVerts);

			segdata[i].linedef = Level.GLSegs[i].linedef;
			segdata[i].side = Level.GLSegs[i].side;
			segdata[i].partner = Level.GLSegs[i].partner;
		}
		byte[] data = Util.StructArrayToBytes(segdata);
		writer.WriteLump("GL_SEGS", data, data.Length);
	}

	private void WriteGLSSect(FWadWriter writer, bool v5)
	{
		if (!v5)
			WriteSSectors2(writer, "GL_SSECT", Level.GLSubsectors);
		else
			WriteSSectors5(writer, "GL_SSECT", Level.GLSubsectors);
	}

	private void WriteGLNodes(FWadWriter writer, bool v5)
	{
		if (!v5)
			WriteNodes2(writer, "GL_NODES", Level.GLNodes);
		else
			WriteNodes5(writer, "GL_NODES", Level.GLNodes);
	}

	private void WriteBSPZ(FWadWriter writer, string label)
	{
		//ZLibOut zout = new ZLibOut(@writer);

		//if (!CompressNodes)
		//{
		//	printf("   Nodes are so big that compression has been forced.\n");
		//}

		//writer.StartWritingLump(label);
		//writer.AddToLump("ZNOD", 4);
		//WriteVerticesZ(zout, Level.Vertices[Level.NumOrgVerts], Level.NumOrgVerts, Level.NumVertices - Level.NumOrgVerts);
		//WriteSubsectorsZ(zout, Level.Subsectors, Level.NumSubsectors);
		//WriteSegsZ(zout, Level.Segs, Level.NumSegs);
		//WriteNodesZ(zout, Level.Nodes, Level.NumNodes, 1);
	}

	private void WriteGLBSPZ(FWadWriter writer, string label)
	{
		//ZLibOut zout = new ZLibOut(writer);
		//bool fracsplitters = CheckForFracSplitters(Level.GLNodes, Level.NumGLNodes);
		//int nodever;

		//if (!CompressGLNodes)
		//{
		//	printf("   GL Nodes are so big that compression has been forced.\n");
		//}

		//writer.StartWritingLump(label);
		//if (fracsplitters)
		//{
		//	writer.AddToLump("ZGL3", 4);
		//	nodever = 3;
		//}
		//else if (Level.NumLines() < 65535)
		//{
		//	writer.AddToLump("ZGLN", 4);
		//	nodever = 1;
		//}
		//else
		//{
		//	writer.AddToLump("ZGL2", 4);
		//	nodever = 2;
		//}
		//WriteVerticesZ(zout, Level.GLVertices[Level.NumOrgVerts], Level.NumOrgVerts, Level.NumGLVertices - Level.NumOrgVerts);
		//WriteSubsectorsZ(zout, Level.GLSubsectors, Level.NumGLSubsectors);
		//WriteGLSegsZ(zout, Level.GLSegs, Level.NumGLSegs, nodever);
		//WriteNodesZ(zout, Level.GLNodes, Level.NumGLNodes, nodever);
	}

	
	//private void WriteVerticesZ(ZLibOut writer, WideVertex[] verts, int orgverts, int newverts)
	//{
	//	writer << (uint)orgverts << (uint)newverts;

	//	for (int i = 0; i < newverts; ++i)
	//	{
	//		writer << verts[i].x << verts[i].y;
	//	}
	//}

	//private void WriteSubsectorsZ(ZLibOut writer, MapSubsectorEx[] subs, int numsubs)
	//{
	//	writer << (uint)numsubs;

	//	for (int i = 0; i < numsubs; ++i)
	//	{
	//		writer << (uint)subs[i].numlines;
	//	}
	//}

	//private void WriteSegsZ(ZLibOut writer, MapSegEx[] segs, int numsegs)
	//{
	//	writer << (uint)numsegs;

	//	for (int i = 0; i < numsegs; ++i)
	//	{
	//		writer << (uint)segs[i].v1 << (uint)segs[i].v2 << (ushort)segs[i].linedef << (byte)segs[i].side;
	//	}
	//}

	//private void WriteGLSegsZ(ZLibOut writer, MapSegGLEx[] segs, int numsegs, int nodever)
	//{
	//	writer << (uint)numsegs;

	//	if (nodever < 2)
	//	{
	//		for (int i = 0; i < numsegs; ++i)
	//		{
	//			writer << (uint)segs[i].v1 << (uint)segs[i].partner << (ushort)segs[i].linedef << (byte)segs[i].side;
	//		}
	//	}
	//	else
	//	{
	//		for (int i = 0; i < numsegs; ++i)
	//		{
	//			writer << (uint)segs[i].v1 << (uint)segs[i].partner << (uint)segs[i].linedef << (byte)segs[i].side;
	//		}
	//	}
	//}

	//private void WriteNodesZ(ZLibOut writer, MapNodeEx[] nodes, int numnodes, int nodever)
	//{
	//	writer << (uint)numnodes;

	//	for (int i = 0; i < numnodes; ++i)
	//	{
	//		if (nodever < 3)
	//		{
	//			writer << (short)(nodes[i].x >> 16) << (short)(nodes[i].y >> 16) << (short)(nodes[i].dx >> 16) << (short)(nodes[i].dy >> 16);
	//		}
	//		else
	//		{
	//			writer << (uint)nodes[i].x << (uint)nodes[i].y << (uint)nodes[i].dx << (uint)nodes[i].dy;
	//		}
	//		for (int j = 0; j < 2; ++j)
	//		{
	//			for (int k = 0; k < 4; ++k)
	//			{
	//				writer << (short)nodes[i].bbox[j][k];
	//			}
	//		}
	//		writer << (uint)nodes[i].children[0] << (uint)nodes[i].children[1];
	//	}
	//}	

	private void WriteBSPX(FWadWriter writer, string label)
	{
		//if (!CompressNodes)
		//{
		//	printf("   Nodes are so big that extended format has been forced.\n");
		//}

		writer.StartWritingLump(label);
		writer.AddToLump(Util.GetStringBytes("XNOD"), 4);
		WriteVerticesX(writer,Level.Vertices.Skip(Level.NumOrgVerts).ToArray(), Level.NumOrgVerts, Level.NumVertices - Level.NumOrgVerts);
		WriteSubsectorsX(writer, Level.Subsectors);
		WriteSegsX(writer, Level.Segs);
		WriteNodesX(writer, Level.Nodes, 1);
	}

	private void WriteGLBSPX(FWadWriter writer, string label)
	{
		bool fracsplitters = CheckForFracSplitters(Level.GLNodes);
		int nodever;

		if (!m_options.CompressGLNodes)
		{
			//printf("   GL Nodes are so big that extended format has been forced.\n");
		}

		writer.StartWritingLump(label);
		if (fracsplitters)
		{
			writer.AddToLump(Util.GetStringBytes("XGL3"), 4);
			nodever = 3;
		}
		else if (Level.NumLines() < 65535)
		{
			writer.AddToLump(Util.GetStringBytes("XGLN"), 4);
			nodever = 1;
		}
		else
		{
			writer.AddToLump(Util.GetStringBytes("XGL2"), 4);
			nodever = 2;
		}

		WriteVerticesX(writer, Level.GLVertices.Skip(Level.NumOrgVerts).ToArray(), Level.NumOrgVerts, Level.GLVertices.Length - Level.NumOrgVerts);
		WriteSubsectorsX(writer, Level.GLSubsectors);
		WriteGLSegsX(writer, Level.GLSegs, nodever);
		WriteNodesX(writer, Level.GLNodes, nodever);
	}

	private void WriteVerticesX(FWadWriter writer, WideVertex[] verts, int orgverts, int newverts)
	{
		writer.WriteUint((uint)orgverts);
		writer.WriteUint((uint)newverts);

		for (int i = 0; i < newverts; ++i)
		{
			writer.WriteInt(verts[i].x);
			writer.WriteInt(verts[i].y);
		}
	}

	private static void WriteSubsectorsX(FWadWriter writer, MapSubsectorEx[] subs)
	{
		writer.WriteUint((uint)subs.Length);

		for (int i = 0; i < subs.Length; ++i)
			writer.WriteUint((uint)subs[i].numlines);
	}

	private static void WriteSegsX(FWadWriter writer, MapSegEx[] segs)
	{
		writer.WriteUint((uint)segs.Length);

		for (int i = 0; i < segs.Length; ++i)
		{
			writer.WriteUint((uint)segs[i].v1);
			writer.WriteUint((uint)segs[i].v2);
			writer.WriteUshort(segs[i].linedef);
			writer.WriteByte((byte)segs[i].side);
		}
	}

	private static void WriteGLSegsX(FWadWriter writer, MapSegGLEx[] segs, int nodever)
	{
		writer.WriteUint((uint)segs.Length);

		if (nodever < 2)
		{
			for (int i = 0; i < segs.Length; ++i)
			{
				writer.WriteUint((uint)segs[i].v1);
				writer.WriteUint((uint)segs[i].partner);
				writer.WriteUshort((ushort)segs[i].linedef);
				writer.WriteByte((byte)segs[i].side);
			}
		}
		else
		{
			for (int i = 0; i < segs.Length; ++i)
			{
				writer.WriteUint((uint)segs[i].v1);
				writer.WriteUint((uint)segs[i].partner);
				writer.WriteUint((uint)segs[i].linedef);
				writer.WriteByte((byte)segs[i].side);
			}
		}
	}

	private unsafe void WriteNodesX(FWadWriter writer, MapNodeEx[] nodes, int nodever)
	{
		writer.WriteUint((uint)nodes.Length);

		for (int i = 0; i < nodes.Length; ++i)
		{
			if (nodever < 3)
			{
				writer.WriteShort((short)(nodes[i].x >> 16));
				writer.WriteShort((short)(nodes[i].y >> 16));
				writer.WriteShort((short)(nodes[i].dx >> 16));
				writer.WriteShort((short)(nodes[i].dy >> 16));
			}
			else
			{
				writer.WriteUint((uint)nodes[i].x);
				writer.WriteUint((uint)nodes[i].y);
				writer.WriteUint((uint)nodes[i].dx);
				writer.WriteUint((uint)nodes[i].dy);
			}

			for (int j = 0; j < 2 * 4; ++j)
				writer.WriteShort((short)nodes[i].bbox[j]);

			writer.WriteUint((uint)nodes[i].children[0]);
			writer.WriteUint((uint)nodes[i].children[1]);
		}
	}

	private static void WriteSSectors2(FWadWriter writer, string name, MapSubsectorEx[] subs)
	{
		MapSubsector[] ssec = new MapSubsector[subs.Length];
		for (int i = 0; i < subs.Length; ++i)
		{
			ssec[i].firstline = (ushort)subs[i].firstline;
			ssec[i].numlines = (ushort)subs[i].numlines;
		}

		byte[] data = Util.StructArrayToBytes(ssec);
		writer.WriteLump(name, data, data.Length);

		//if (subs.Length >= 65536)
			//printf("   %s is too big. (%d subsectors)\n", name, count);
	}

	private unsafe void WriteNodes2(FWadWriter writer, string name, MapNodeEx[] zaNodes)
	{
		short[] nodes = new short[zaNodes.Length * Marshal.SizeOf<MapNode>() / 2];
		int nodePos = 0;

		for (int i = 0; i < zaNodes.Length; ++i)
		{
			nodes[nodePos++] = (short)(zaNodes[i].x >> 16);
			nodes[nodePos++] = (short)(zaNodes[i].y >> 16);
			nodes[nodePos++] = (short)(zaNodes[i].dx >> 16);
			nodes[nodePos++] = (short)(zaNodes[i].dy >> 16);

			for (int iNode = 0; iNode < 2 * 4; iNode++)
				nodes[nodePos++] = zaNodes[i].bbox[iNode];

			for (int j = 0; j < 2; ++j)
			{
				uint child = zaNodes[i].children[j];
				if ((child & Constants.NFX_SUBSECTOR) != 0)
					nodes[nodePos++] = (short)(child - (Constants.NFX_SUBSECTOR + Constants.NF_SUBSECTOR));
				else
					nodes[nodePos++] = (short)child;
			}
		}

		byte[] data = Util.StructArrayToBytes(nodes);
		writer.WriteLump(name, data, data.Length);

		//if (count >= 32768)
		//{
		//	//printf("   %s is too big. (%d nodes)\n", name, count);
		//}
	}

	private unsafe void WriteNodes5(FWadWriter writer, string name, MapNodeEx[] zaNodes)
	{
		MapNodeExO[] nodes = new MapNodeExO[zaNodes.Length];
		for (int i = 0; i < zaNodes.Length; ++i)
		{
			for (int iCoord = 0; iCoord < 2 * 4; iCoord++)
				nodes[i].bbox[iCoord] = zaNodes[i].bbox[iCoord];

			nodes[i].x = (short)(zaNodes[i].x >> 16);
			nodes[i].y = (short)(zaNodes[i].y >> 16);
			nodes[i].dx = (short)(zaNodes[i].dx >> 16);
			nodes[i].dy = (short)(zaNodes[i].dy >> 16);

			for (int j = 0; j < 2; ++j)
				nodes[i].children[j] =	zaNodes[i].children[j];
		}
		byte[] data = Util.StructArrayToBytes(nodes);
		writer.WriteLump(name, data, data.Length);
	}

	private static void WriteSSectors5(FWadWriter writer, string name, MapSubsectorEx[] subs)
	{
		MapSubsectorEx[] ssec = new MapSubsectorEx[subs.Length];
		for (int i = 0; i < subs.Length; ++i)
		{
			ssec[i].firstline = subs[i].firstline;
			ssec[i].numlines = subs[i].numlines;
		}

		byte[] data = Util.StructArrayToBytes(ssec);
		writer.WriteLump(name, data, data.Length);
	}
}