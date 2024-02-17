using System.Runtime.InteropServices;

namespace Kryolite.RocksDb;

internal static class Interop
{
    [DllImport("rocksdb", EntryPoint = "rocksdb_open_column_families")]
    public static extern nint rocksdb_open_column_families(nint handle, nint path_handle, int length, [In] string[] column_family_names, [In] nint[] column_family_options, [In, Out] nint[] array2, out nint errorptr);

    [DllImport("rocksdb", EntryPoint = "rocksdb_options_create")]
    public static extern nint rocksdb_options_create();

    [DllImport("rocksdb", EntryPoint = "rocksdb_options_destroy")]
    public static extern void rocksdb_options_destroy(nint handle);

    [DllImport("rocksdb", EntryPoint = "rocksdb_column_family_handle_destroy")]
    public static extern void rocksdb_column_family_handle_destroy(nint handle);

    [DllImport("rocksdb", EntryPoint = "rocksdb_close")]
    public static extern nint rocksdb_close(nint handle);

    [DllImport("rocksdb", EntryPoint = "rocksdb_options_set_create_if_missing")]
    public static extern nint rocksdb_options_set_create_if_missing(nint handle, bool value);

    [DllImport("rocksdb", EntryPoint = "rocksdb_options_set_create_missing_column_families")]
    public static extern nint rocksdb_options_set_create_missing_column_families(nint handle, bool value);

    [DllImport("rocksdb", EntryPoint = "rocksdb_options_set_wal_dir")]
    public static extern nint rocksdb_options_set_wal_dir(nint handle, nint path);

    [DllImport("rocksdb", EntryPoint = "rocksdb_options_set_keep_log_file_num")]
    public static extern nint rocksdb_options_set_keep_log_file_num(nint handle, nuint value);

    [DllImport("rocksdb", EntryPoint = "rocksdb_free")]
    public static extern nint rocksdb_free(nint handle);

    [DllImport("rocksdb", EntryPoint = "rocksdb_get_cf")]
    public static extern nint rocksdb_get_cf(nint handle, nint read_opts, nint cf, nint key, long keylen, out nint vallen, out nint errorptr);

    [DllImport("rocksdb", EntryPoint = "rocksdb_put_cf")]
    public static extern void rocksdb_put_cf(nint handle, nint write_opts, nint cf, nint key, long keylen, nint val, nint vallen, out nint errorptr);

    [DllImport("rocksdb", EntryPoint = "rocksdb_readoptions_create")]
    public static extern nint rocksdb_readoptions_create();

    [DllImport("rocksdb", EntryPoint = "rocksdb_readoptions_destroy")]
    public static extern void rocksdb_readoptions_destroy(nint handle);

    [DllImport("rocksdb", EntryPoint = "rocksdb_writeoptions_create")]
    public static extern nint rocksdb_writeoptions_create();

    [DllImport("rocksdb", EntryPoint = "rocksdb_writeoptions_destroy")]
    public static extern void rocksdb_writeoptions_destroy(nint handle);
    [DllImport("rocksdb", EntryPoint = "rocksdb_writebatch_create")]
    public static extern nint rocksdb_writebatch_create();

    [DllImport("rocksdb", EntryPoint = "rocksdb_writebatch_destroy")]
    public static extern void rocksdb_writebatch_destroy(nint handle);

    [DllImport("rocksdb", EntryPoint = "rocksdb_writebatch_put_cf")]
    public static extern void rocksdb_writebatch_put_cf(nint handle, nint cf, nint key, long keylen, nint val, nint vallen);

    [DllImport("rocksdb", EntryPoint = "rocksdb_writebatch_delete_cf")]
    public static extern void rocksdb_writebatch_delete_cf(nint handle, nint cf, nint key, long keylen);

    [DllImport("rocksdb", EntryPoint = "rocksdb_multi_get_cf")]
    public unsafe static extern void rocksdb_multi_get_cf(nint handle, nint read_opts, nint[] cfs, int num_keys, nint[] keys, int[] key_sizes, nint[] values, int[] value_sizes, nint[] errors);

    [DllImport("rocksdb", EntryPoint = "rocksdb_delete_cf")]
    public static extern void rocksdb_delete_cf(nint handle, nint write_opts, nint cf, nint key, long keylen, out nint errorptr);

    [DllImport("rocksdb", EntryPoint = "rocksdb_create_iterator_cf")]
    public static extern nint rocksdb_create_iterator_cf(nint handle, nint read_opts, nint cf);

    [DllImport("rocksdb", EntryPoint = "rocksdb_iter_destroy")]
    public static extern void rocksdb_iter_destroy(nint handle);

    [DllImport("rocksdb", EntryPoint = "rocksdb_readoptions_set_prefix_same_as_start")]
    public static extern void rocksdb_readoptions_set_prefix_same_as_start(nint handle, bool prefixSameAsStart);

    [DllImport("rocksdb", EntryPoint = "rocksdb_readoptions_set_iterate_lower_bound")]
    public static extern void rocksdb_readoptions_set_iterate_lower_bound(nint handle, nint key, int keylen);

    [DllImport("rocksdb", EntryPoint = "rocksdb_readoptions_set_iterate_upper_bound")]
    public static extern void rocksdb_readoptions_set_iterate_upper_bound(nint handle, nint key, int keylen);

    [DllImport("rocksdb", EntryPoint = "rocksdb_iter_seek_to_first")]
    public static extern void rocksdb_iter_seek_to_first(nint handle);

    [DllImport("rocksdb", EntryPoint = "rocksdb_iter_seek_to_last")]
    public static extern void rocksdb_iter_seek_to_last(nint handle);

    [DllImport("rocksdb", EntryPoint = "rocksdb_iter_seek")]
    public static unsafe extern void rocksdb_iter_seek(nint handle, byte* key, int key_len);

    [DllImport("rocksdb", EntryPoint = "rocksdb_iter_seek_for_prev")]
    public static unsafe extern void rocksdb_iter_seek_for_prev(nint handle, byte* key, int key_len);

    [DllImport("rocksdb", EntryPoint = "rocksdb_iter_valid")]
    public static extern bool rocksdb_iter_valid(nint handle);

    [DllImport("rocksdb", EntryPoint = "rocksdb_iter_next")]
    public static extern void rocksdb_iter_next(nint handle);

    [DllImport("rocksdb", EntryPoint = "rocksdb_iter_prev")]
    public static extern void rocksdb_iter_prev(nint handle);

    [DllImport("rocksdb", EntryPoint = "rocksdb_iter_key")]
    public static extern nint rocksdb_iter_key(nint handle, out int len);

    [DllImport("rocksdb", EntryPoint = "rocksdb_iter_value")]
    public static extern nint rocksdb_iter_value(nint handle, out int len);

    [DllImport("rocksdb", EntryPoint = "rocksdb_create_column_family")]
    public static extern nint rocksdb_create_column_family(nint handle, nint opts, string name, out nint errorptr);

    [DllImport("rocksdb", EntryPoint = "rocksdb_drop_column_family")]
    public static extern void rocksdb_drop_column_family(nint handle, nint cf, out nint errorptr);

    [DllImport("rocksdb", EntryPoint = "rocksdb_checkpoint_object_create")]
    public static extern nint rocksdb_checkpoint_object_create(nint handle);

    [DllImport("rocksdb", EntryPoint = "rocksdb_checkpoint_create")]
    public static extern void rocksdb_checkpoint_create(nint handle, string path, ulong log_size, out nint errorptr);

    [DllImport("rocksdb", EntryPoint = "rocksdb_checkpoint_object_destroy")]
    public static extern void rocksdb_checkpoint_object_destroy(nint handle);

    [DllImport("rocksdb", EntryPoint = "rocksdb_writebatch_clear")]
    public static extern void rocksdb_writebatch_clear(nint handle);

    [DllImport("rocksdb", EntryPoint = "rocksdb_write")]
    public static extern void rocksdb_write(nint handle, nint opts, nint batch, out nint errorptr);

    [DllImport("rocksdb", EntryPoint = "rocksdb_flush")]
    public static extern void rocksdb_flush(nint handle, nint opts, out nint errorptr);

    [DllImport("rocksdb", EntryPoint = "rocksdb_flushoptions_create")]
    public static extern nint rocksdb_flushoptions_create();

    [DllImport("rocksdb", EntryPoint = "rocksdb_flushoptions_set_wait")]
    public static extern void rocksdb_flushoptions_set_wait(nint handle, bool set_wait);

    [DllImport("rocksdb", EntryPoint = "rocksdb_flushoptions_destroy")]
    public static extern void rocksdb_flushoptions_destroy(nint handle);

    [DllImport("rocksdb", EntryPoint = "rocksdb_flush")]
    public static extern void rocksdb_flush(nint handle, nint opts);
}
