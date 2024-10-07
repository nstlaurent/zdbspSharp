using System.Runtime.InteropServices;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace zdbspSharp;

static class Util
{
    public static int AddCount<T>(this List<T> list, T item)
    {
        int count = list.Count;
        list.Add(item);
        return count;
    }

    public static int AddCount<T>(this DynamicArray<T> list, T item)
    {
        int count = list.Length;
        list.Add(item);
        return count;
    }

    public static unsafe void ByteCopy(byte* dest, byte* source, int count)
    {
        for (int i = 0; i < count; i++)
            dest[i] = source[i];
    }

    public static T[] ReadMapLump<[DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors)] T>
        (FWadReader wad, string name, int index) where T : struct
    {
	    return ReadLump<T>(wad, wad.FindMapLump(name, index));
    }

    public static T[] ReadLump<[DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors)] T>
        (FWadReader wad, int index) where T : struct
    {
        if (index >= wad.Header.NumLumps)
            return Array.Empty<T>();

        wad.ReadStream.Seek(wad.Lumps[index].FilePos, SeekOrigin.Begin);
        byte[] data = new byte[wad.Lumps[index].Size];
        wad.ReadStream.Read(data, 0, wad.Lumps[index].Size);
        return ByteToArrayStruct<T>(data);
    }

    public static byte[] ReadLumpBytes(FWadReader wad, int index)
    {
        wad.ReadStream.Seek(wad.Lumps[index].FilePos, SeekOrigin.Begin);
        byte[] data = new byte[wad.Lumps[index].Size];
        wad.ReadStream.Read(data, 0, wad.Lumps[index].Size);
        return data;
    }

    public static T ReadStuctureFromStream<[DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors)] T>
        (Stream stream) where T : struct
    {
        byte[] bytes = new byte[Marshal.SizeOf<T>()];
        ReadStream(stream, bytes, bytes.Length);

        GCHandle handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        T obj = Marshal.PtrToStructure<T>(handle.AddrOfPinnedObject());
        handle.Free();
        return obj;
    }

    public static byte[] StructureToBytes<T>(in T data) where T : struct
    {
        int size = Marshal.SizeOf(data);
        byte[] arr = new byte[size];

        IntPtr ptr = Marshal.AllocHGlobal(size);
        Marshal.StructureToPtr(data, ptr, true);
        Marshal.Copy(ptr, arr, 0, size);
        Marshal.FreeHGlobal(ptr);
        return arr;
    }

    public static byte[] StructureToBytes<T>(in T data, byte[] arr, int offset) where T : struct
    {
        int size = Marshal.SizeOf(data);

        IntPtr ptr = Marshal.AllocHGlobal(size);
        Marshal.StructureToPtr(data, ptr, true);
        Marshal.Copy(ptr, arr, offset, size);
        Marshal.FreeHGlobal(ptr);
        return arr;
    }

    public static byte[] StructArrayToBytes<T>(T[] data) where T : struct
    {
        int structSize = Marshal.SizeOf<T>();
        byte[] structBytes = new byte[structSize * data.Length];

        for (int i = 0; i < data.Length; i++)
            StructureToBytes(data[i], structBytes, i * structSize);
        return structBytes;
    }

    public static T[] ByteToArrayStruct<[DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors)] T>
        (byte[] data) where T : struct
    {
        int structSize = Marshal.SizeOf<T>();
        T[] arr = new T[data.Length / structSize];
        for (int i = 0; i < arr.Length; i++)
        {
            byte[] bytes = data.Skip(structSize * i).Take(structSize).ToArray();
            GCHandle handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            T obj = Marshal.PtrToStructure<T>(handle.AddrOfPinnedObject());
            handle.Free();
            arr[i] = obj;
        }

        return arr;
    }

    public static byte[] GetStringBytes(string str) =>
        Encoding.UTF8.GetBytes(str);

    public static void ReadStream(Stream stream, byte[] data, int size)
    {
        int readSize = 0;
        while (readSize < size)
            readSize += stream.Read(data, 0, size - readSize);
    }

    public static uint PointToAngle(int x, int y)
    {
        double ang = Math.Atan2(y, x);
        const double rad2bam = (1 << 30) / Math.PI;
        double dbam = ang * rad2bam;
        // Convert to signed first since negative double to unsigned is undefined.
        return (uint)((int)(dbam) << 1);
    }

    public static int PointOnSide(int x, int y, int x1, int y1, int dx, int dy)
    {
        // For most cases, a simple dot product is enough.
        double d_dx = (dx);
        double d_dy = (dy);
        double d_x = (x);
        double d_y = (y);
        double d_x1 = (x1);
        double d_y1 = (y1);

        double s_num = (d_y1 - d_y) * d_dx - (d_x1 - d_x) * d_dy;

        if (Math.Abs(s_num) < 17179869184)    // 4<<32
        {
            // Either the point is very near the line, or the segment defining
            // the line is very short: Do a more expensive test to determine
            // just how far from the line the point is.
            double l = d_dx * d_dx + d_dy * d_dy;       // double l = sqrt(d_dx*d_dx+d_dy*d_dy);
            double dist = s_num * s_num / l;        // double dist = fabs(s_num)/l;
            if (dist < Constants.SIDE_EPSILON * Constants.SIDE_EPSILON) // if (dist < SIDE_EPSILON)
                return 0;
        }
        return s_num > 0.0 ? -1 : 1;
    }

    public static int Scale(int a, int b, int c)
    {
        return (int)(a * b / (double)c);
    }

    public static int DivScale30(int a, int b)
    {
        return (int)(a / b * (double)(1 << 30));
    }

    public static int MulScale30(int a, int b)
    {
        return (int)(a * b / (double)(1 << 30));
    }

    public static int DMulScale30(int a, int b, int c, int d)
    {
        return (int)((a * b + c * d) / (double)(1 << 30));
    }

    public static int DMulScale32(int a, int b, int c, int d)
    {
        return (int)((a * b + c * d) / 4294967296.0);
    }

    public static int LittleLong(int x) => x;
    public static uint LittleLong(uint x) => x;
    public static int LittleLong(long x) => (int)x;
    public static short LittleShort(short x) => x;
    public static ushort LittleShort(ushort x) => x;
}
