public static class ByteExtensions
{
    public static byte[] Reverse(this byte[] bytes)
    {
        Array.Reverse(bytes);
        return bytes;
    }

    public static byte[] ToKey(this long val)
    {
        var bytes = BitConverter.GetBytes(val);
        Array.Reverse(bytes);
        return bytes;
    }

    public static byte[] ToKey(this ulong val)
    {
        var bytes = BitConverter.GetBytes(val);
        Array.Reverse(bytes);
        return bytes;
    }
}
