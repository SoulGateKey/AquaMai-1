using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using AquaMai.Config.Attributes;
using HarmonyLib;
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

    public static void OnBeforePatch()
    {
        Log.Info("Starting WorldsLink patch...");
        var keychip = AMDaemon.System.KeychipId.ShortValue;
        Log.Info($"Keychip ID: {keychip}");
        if (string.IsNullOrEmpty(keychip))
        {
            Log.Error("Keychip ID is empty. WorldsLink will not work.");
            // return;
            
            // For testing: Create a random keychip (10-digit number)
            keychip = "A" + new Random().Next(1000000000, int.MaxValue);
        }
        new FutariClient(keychip, "violet", 20101).ConnectAsync();
    }
    
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
        // Let's not log this, there's too many of them
        // Log.Debug("NFPoll");
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
}