using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using AquaMai.Config.Attributes;
using HarmonyLib;
using Manager;
using MelonLoader;
using PartyLink;

namespace AquaMai.Mods.WorldsLink;

[ConfigSection(
    en: "Enable WorldsLink Multiplayer",
    zh: "启用 WorldsLink 多人游戏",
    defaultOn: true)]
public static class FutariPatch
{
    private static readonly Dictionary<NFSocket, FutariSocket> redirect = new();
    private static FutariClient client;
    private static bool isInit=false;

    static MethodBase packet_writeunit;
    public static void OnBeforePatch()
    {
        Log.Info("Starting WorldsLink patch...");

        packet_writeunit = typeof(Packet).GetMethod("write_uint", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static,null, new System.Type[] { typeof(PacketType), typeof(int), typeof(uint)},null);
        if(packet_writeunit==null)Log.Error("write_uint not found");

        client = new FutariClient("A000", "70.49.234.104", 20101);
    }
    
    // Patch for logging
    // SocketBase:: public void sendClass(ICommandParam info)
    [HarmonyPrefix]
    [HarmonyPatch(typeof(SocketBase), "sendClass", typeof(ICommandParam))]
    private static bool sendClass(SocketBase __instance, ICommandParam info)
    {
        // For logging only, log the actual type of info and the actual type of this class
        Log.Debug($"SendClass: {info.GetType().Name} from {__instance.GetType().Name}");
        return true;
    }
    
    // Patch for error logging
    // SocketBase:: protected void error(string message, int no)
    [HarmonyPrefix]
    [HarmonyPatch(typeof(SocketBase), "error", typeof(string), typeof(int))]
    private static bool error(string message, int no)
    {
        Log.Error($"Error: {message} ({no})");
        return true;
    }
    
    // Patch to always enable send
    // SocketBase:: public static bool checkSendEnable(NFSocket socket)
    [HarmonyPrefix]
    [HarmonyPatch(typeof(SocketBase), "checkSendEnable", typeof(NFSocket))]
    private static bool checkSendEnable(NFSocket socket, ref bool __result)
    {
        __result = true;
        return false;
    }
    
    // Other patches not in NFSocket
    // public static IPAddress MyIpAddress(int mockID)
    [HarmonyPrefix]
    [HarmonyPatch(typeof(PartyLink.Util), "MyIpAddress", typeof(int))]
    private static bool MyIpAddress(int mockID, ref IPAddress __result)
    {
        __result = new IPAddress(FutariExt.MyStubIP());
        return false;
    }
    
    // public static uint ToNetworkByteOrderU32(this IPAddress ip)
    [HarmonyPrefix]
    [HarmonyPatch(typeof(PartyLink.Util), "ToNetworkByteOrderU32")]
    private static bool ToNetworkByteOrderU32(this IPAddress ip, ref uint __result)
    {
        __result = BitConverter.ToUInt32(ip.GetAddressBytes(), 0);
        return false;
    }

    //Skip DeliveryChecker
    [HarmonyPostfix]
    [HarmonyPatch("PartyLink.DeliveryChecker+Manager", "isOk")]
    private static void DeliveryCheckerIsOk(ref bool __result)
    {
        MelonLogger.Msg("DeliveryCheckerIsOk:true");
        __result = true;
    }

    // private void CheckAuth_Proc()
    [HarmonyPrefix]
    [HarmonyPatch(typeof(OperationManager), "CheckAuth_Proc")]
    private static bool CheckAuth_Proc()
    {
        if (isInit) return true;
        Log.Info("CheckAuth_Proc");
        
        var keychip = AMDaemon.System.KeychipId.ShortValue;
        Log.Info($"Keychip ID: {keychip}");
        if (string.IsNullOrEmpty(keychip))
        {
            Log.Error("Keychip ID is empty. WorldsLink will not work.");
            // return;
            
            // For testing: Create a random keychip (10-digit number)
            keychip = "A" + new Random().Next(1000000000, int.MaxValue);
        }
        client.keychip = keychip;
        client.ConnectAsync();

        isInit = true;
        return true; // Allow the original method to run
    }
    #region NFSocket
    [HarmonyPostfix]
    [HarmonyPatch(typeof(NFSocket), MethodType.Constructor, typeof(AddressFamily), typeof(SocketType), typeof(ProtocolType), typeof(int))]
    private static void NFCreate(NFSocket __instance, AddressFamily addressFamily, SocketType socketType, ProtocolType protocolType, int mockID)
    {
        Log.Debug("new NFSocket(AddressFamily, SocketType, ProtocolType, int)");
        var futari = new FutariSocket(addressFamily, socketType, protocolType, mockID);
        redirect.Add(__instance, futari);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(NFSocket), MethodType.Constructor, typeof(Socket))]
    private static void NFCreate2(NFSocket __instance, Socket nfSocket)
    {
        Log.Warn("new NFSocket(Socket) -- We shouldn't get here.");
        throw new NotImplementedException();
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NFSocket), "Poll")]
    private static bool NFPoll(NFSocket socket, SelectMode mode, ref bool __result)
    {
        FutariSocket.Poll(redirect[socket], mode);
        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NFSocket), "Send")]
    private static bool NFSend(NFSocket __instance, byte[] buffer, int offset, int size, SocketFlags socketFlags)
    {
        Log.Debug("NFSend");
        redirect[__instance].Send(buffer, offset, size, socketFlags);
        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NFSocket), "SendTo")]
    private static bool NFSendTo(NFSocket __instance, byte[] buffer, int offset, int size, SocketFlags socketFlags, EndPoint remoteEP)
    {
        Log.Debug("NFSendTo");
        redirect[__instance].SendTo(buffer, offset, size, socketFlags, remoteEP);
        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NFSocket), "Receive")]
    private static bool NFReceive(NFSocket __instance, byte[] buffer, int offset, int size, SocketFlags socketFlags, out SocketError errorCode)
    {
        Log.Debug("NFReceive");
        redirect[__instance].Receive(buffer, offset, size, socketFlags, out errorCode);
        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NFSocket), "ReceiveFrom")]
    private static bool NFReceiveFrom(NFSocket __instance, byte[] buffer, SocketFlags socketFlags, ref EndPoint remoteEP)
    {
        Log.Debug("NFReceiveFrom");
        redirect[__instance].ReceiveFrom(buffer, socketFlags, ref remoteEP);
        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NFSocket), "Bind")]
    private static bool NFBind(NFSocket __instance, EndPoint localEndP)
    {
        Log.Debug("NFBind");
        redirect[__instance].Bind(localEndP);
        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NFSocket), "Listen")]
    private static bool NFListen(NFSocket __instance, int backlog)
    {
        Log.Debug("NFListen");
        redirect[__instance].Listen(backlog);
        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NFSocket), "Accept")]
    private static bool NFAccept(NFSocket __instance, ref NFSocket __result)
    {
        Log.Debug("NFAccept");
        var futariSocket = redirect[__instance].Accept();
        var mockSocket = new NFSocket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp, 0);
        redirect.Add(mockSocket, futariSocket);
        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NFSocket), "ConnectAsync")]
    private static bool NFConnectAsync(NFSocket __instance, SocketAsyncEventArgs e, int mockID)
    {
        Log.Debug("NFConnectAsync");
        redirect[__instance].ConnectAsync(e, mockID);
        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NFSocket), "SetSocketOption")]
    private static bool NFSetSocketOption(NFSocket __instance, SocketOptionLevel optionLevel, SocketOptionName optionName, bool optionValue)
    {
        Log.Debug("NFSetSocketOption");
        redirect[__instance].SetSocketOption(optionLevel, optionName, optionValue);
        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NFSocket), "Close")]
    private static bool NFClose(NFSocket __instance)
    {
        Log.Debug("NFClose");
        redirect[__instance].Close();
        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NFSocket), "Shutdown")]
    private static bool NFShutdown(NFSocket __instance, SocketShutdown how)
    {
        Log.Debug("NFShutdown");
        redirect[__instance].Shutdown(how);
        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NFSocket), "RemoteEndPoint", MethodType.Getter)]
    private static bool NFGetRemoteEndPoint(NFSocket __instance, ref EndPoint __result)
    {
        Log.Debug("NFGetRemoteEndPoint");
        __result = redirect[__instance].RemoteEndPoint;
        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NFSocket), "LocalEndPoint", MethodType.Getter)]
    private static bool NFGetLocalEndPoint(NFSocket __instance, ref EndPoint __result)
    {
        Log.Debug("NFGetLocalEndPoint");
        __result = redirect[__instance].LocalEndPoint;
        return false;
    }
    #endregion
    #region Packet
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Packet), "encrypt")]
    private static bool PacketEncrypt(Packet __instance, PacketType ____encrypt, PacketType ____plane)
    {
        ____encrypt.ClearAndResize(____plane.Count);
        Array.Copy(____plane.GetBuffer(), 0, ____encrypt.GetBuffer(), 0, ____plane.Count);
        ____encrypt.ChangeCount(____plane.Count);
        packet_writeunit.Invoke(null, new object[] { ____plane, 0, (uint)____plane.Count });
        return true;
    }
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Packet), "decode")]
    private static bool PacketDecode(Packet __instance, BufferType buffer, IpAddress address, PacketType ____encrypt, PacketType ____plane)
    {
        ____plane.ClearAndResize(____encrypt.Count);
        Array.Copy(____encrypt.GetBuffer(), 0, ____plane.GetBuffer(), 0, ____encrypt.Count);
        ____plane.ChangeCount(____encrypt.Count);
        packet_writeunit.Invoke(null, new object[] { ____plane, 0, (uint)____plane.Count });
        return true;
    }
    #endregion
}