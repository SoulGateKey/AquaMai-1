#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace AquaMai.Mods.WorldsLink;

public static class FutariExt
{
    public static uint KeychipToStubIp(string keychip)
    {
        return uint.Parse("1" + keychip.Substring(2));
    }

    public static byte[] View(this byte[] buffer, int offset, int size)
    {
        var array = new byte[size];
        Array.Copy(buffer, offset, array, 0, size);
        return array;
    }
    
    public static V? Get<K, V>(this ConcurrentDictionary<K, V> dict, K key) where V : class
    {
        return dict.GetValueOrDefault(key);
    }
    
    // Call a function using reflection
    public static void Call(this object obj, string method, params object[] args)
    {
        obj.GetType().GetMethod(method)?.Invoke(obj, args);
    }

    public static uint MyStubIP() => KeychipToStubIp(AMDaemon.System.KeychipId.ShortValue);
}