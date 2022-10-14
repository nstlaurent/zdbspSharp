namespace zdbspSharp;

public static class Constants
{
	public const uint NF_SUBSECTOR = 0x8000;
	public const uint NFX_SUBSECTOR = 0x80000000;
	public const double SIDE_EPSILON = 6.5536;

	public static string[] MapLumpNames = { "THINGS", "LINEDEFS", "SIDEDEFS", "VERTEXES", "SEGS", "SSECTORS", "NODES", "SECTORS", "REJECT", "BLOCKMAP", "BEHAVIOR", "SCRIPTS" };

	public static bool[] MapLumpRequired = { true, true, true, true, false, false, false, true, false, false, false, false };

	public static string[] GLLumpNames = { "GL_VERT", "GL_SEGS", "GL_SSECT", "GL_NODES", "GL_PVS" };

	public const int BoxOffset = 4;

	public const int FRACBITS = 16;
	public const int BLOCKBITS = 7;
	public const int BLOCKSIZE = 128;
	public const ushort NO_MAP_INDEX = 0xffff;
	public const uint ANGLE_MAX = 0xffffffff;
	public const uint ANGLE_180 = (1u << 31);
	public const uint ANGLE_EPSILON = 5000;

	// C# has a lot of checking on int vs uint. C just lets you set them so we need separate constants.
	public const int MAX_INT = -1;
	public const uint MAX_UINT = uint.MaxValue;
}
